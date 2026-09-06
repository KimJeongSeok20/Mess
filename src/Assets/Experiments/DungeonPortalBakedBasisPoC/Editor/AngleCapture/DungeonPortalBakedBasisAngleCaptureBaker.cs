using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DungeonPortalTransportPoC.Editor
{
    /// <summary>
    /// Isolated V2-canonical five-angle, same-pose Baseline/Full extension seam for the receiver-bounce baker.
    /// Nothing in this partial writes the legacy receiver-capture root or a production asset.
    /// The persisted workspace is always D000, unbaked, and proxy-free.
    /// </summary>
    public static partial class DungeonPortalReceiverBounceBaker
    {
        private const string AngleCaptureToolVersion =
            "DungeonPortalBakedBasisAngleCaptureBaker/3";
        private const string AngleCaptureRoot =
            DungeonPortalBakedBasisDoorAngleCapture.EndpointNativeV2OwnedAssetRoot;
        private const string AngleCaptureProxyName =
            "__DPBB_TRANSIENT_SHADOWS_ONLY_GI_DOOR_PROXY__";
        private const string AngleCapturePartialMarkerName =
            "__ANGLE_CAPTURE_INCOMPLETE__.txt";
        private const string DoorPositiveRendererName = "DungeonDoorProbe_PositiveZ";
        private const string DoorNegativeRendererName = "DungeonDoorProbe_NegativeZ";
        private const string DoorEdgeRendererName = "DungeonDoorProbe_Edge";
        private const float AngleCaptureOpenAngleDegrees = 90f;
        private const float EffectChangedTexelDelta = 0.0001f;
        private const float MinimumChangedTexelFraction = 0.001f;
        private const float MinimumMeanAbsoluteRgbDifference = 0.000001f;
        private const float MinimumMaximumAbsoluteRgbDifference = 0.001f;
        private const int EffectMaximumTrianglesPerRenderer = 4096;
        private const long ApproximatePersistedStorageBytes = 900L * 1024L * 1024L;
        private const long RecommendedFreeStorageBytes = 2L * 1024L * 1024L * 1024L;

        private static readonly ReceiverSpec EndpointNativeV2StartSpec = new ReceiverSpec(
            "StartRoom_R000",
            "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/Canonical/StartRoom_R000.prefab",
            21.602848f,
            // The connected angle workspace is authoritative for atlas count. Renderer
            // cardinality remains independently fixed against the V2 canonical inventory.
            0,
            78);
        private static readonly ReceiverSpec EndpointNativeV2AdminSpec = new ReceiverSpec(
            "AdminstrativeSegregation_R000",
            "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/Canonical/AdminstrativeSegregation_R000.prefab",
            57.865467f,
            0,
            302);

        private static readonly Vector3[] EffectSurfaceBarycentricSamples =
        {
            new Vector3(1f / 3f, 1f / 3f, 1f / 3f),
            new Vector3(0.6f, 0.2f, 0.2f),
            new Vector3(0.2f, 0.6f, 0.2f),
            new Vector3(0.2f, 0.2f, 0.6f)
        };

        private static readonly DoorAngleCaptureContract[] AngleCaptureContracts =
        {
            new DoorAngleCaptureContract(
                DungeonPortalBakedBasisDoorAngleCapture.BaselineStateId,
                0f,
                0f,
                false,
                0f,
                true),
            new DoorAngleCaptureContract("D025_Baseline", 0.25f, 22.5f, false, 0f, true),
            new DoorAngleCaptureContract("D025_Full", 0.25f, 22.5f, true, 1f, true),
            new DoorAngleCaptureContract("D050_Baseline", 0.50f, 45f, false, 0f, true),
            new DoorAngleCaptureContract("D050_Full", 0.50f, 45f, true, 1f, true),
            new DoorAngleCaptureContract("D075_Baseline", 0.75f, 67.5f, false, 0f, true),
            new DoorAngleCaptureContract("D075_Full", 0.75f, 67.5f, true, 1f, true),
            new DoorAngleCaptureContract("D100_Baseline", 1.00f, 90f, false, 0f, true),
            new DoorAngleCaptureContract("D100_Full", 1.00f, 90f, true, 1f, true)
        };

        public readonly struct DoorAngleCaptureContract
        {
            public DoorAngleCaptureContract(
                string stateId,
                float openFraction,
                float angleDegrees,
                bool injectorEnabled,
                float injectorBounceIntensity,
                bool useTransientDoorProxy)
            {
                StateId = stateId;
                OpenFraction = openFraction;
                AngleDegrees = angleDegrees;
                InjectorEnabled = injectorEnabled;
                InjectorBounceIntensity = injectorBounceIntensity;
                UseTransientDoorProxy = useTransientDoorProxy;
            }

            public string StateId { get; }
            public float OpenFraction { get; }
            public float AngleDegrees { get; }
            public bool InjectorEnabled { get; }
            public float InjectorBounceIntensity { get; }
            public bool UseTransientDoorProxy { get; }
        }

        public readonly struct DoorAngleCaptureStorageEstimate
        {
            public DoorAngleCaptureStorageEstimate(long approximatePersistedBytes, long recommendedFreeBytes)
            {
                ApproximatePersistedBytes = approximatePersistedBytes;
                RecommendedFreeBytes = recommendedFreeBytes;
            }

            public long ApproximatePersistedBytes { get; }
            public long RecommendedFreeBytes { get; }
        }

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Baked Basis PoC/" +
            "Capture Endpoint-Native V2 Door Angles/StartRoom_R000 (D000-D100)")]
        public static void CaptureDoorAngleBasisStartFromMenu()
        {
            LogResult(CaptureDoorAngleBasisStart());
        }

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Baked Basis PoC/" +
            "Capture Endpoint-Native V2 Door Angles/AdminstrativeSegregation_R000 (D000-D100)")]
        public static void CaptureDoorAngleBasisAdminFromMenu()
        {
            LogResult(CaptureDoorAngleBasisAdmin());
        }

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Baked Basis PoC/" +
            "Capture Endpoint-Native V2 Door Angles/All Receivers (D000-D100)")]
        public static void CaptureDoorAngleBasisAllFromMenu()
        {
            LogResult(CaptureDoorAngleBasisAll());
        }

        /// <summary>Unity -executeMethod entry point. This starts eighteen stored bakes plus two proof bakes.</summary>
        public static void CaptureDoorAngleBasisAllCli()
        {
            ThrowIfFailed(CaptureDoorAngleBasisAll());
        }

        public static void CaptureDoorAngleBasisStartCli()
        {
            ThrowIfFailed(CaptureDoorAngleBasisStart());
        }

        public static void CaptureDoorAngleBasisAdminCli()
        {
            ThrowIfFailed(CaptureDoorAngleBasisAdmin());
        }

        public static string CaptureDoorAngleBasisStart()
        {
            return CaptureDoorAngleReceiver(EndpointNativeV2StartSpec);
        }

        public static string CaptureDoorAngleBasisAdmin()
        {
            return CaptureDoorAngleReceiver(EndpointNativeV2AdminSpec);
        }

        public static string CaptureDoorAngleBasisAll()
        {
            string start = CaptureDoorAngleReceiver(EndpointNativeV2StartSpec);
            if (!IsPass(start))
                return start;
            string administrative = CaptureDoorAngleReceiver(EndpointNativeV2AdminSpec);
            if (!IsPass(administrative))
                return administrative;
            return "PASS DungeonPortalBakedBasis endpoint-native V2 canonical angle capture completed for " +
                   "StartRoom_R000 and AdminstrativeSegregation_R000. Persisted estimate=" +
                   FormatMiB(ApproximatePersistedStorageBytes) + " MiB; recommended free=" +
                   FormatMiB(RecommendedFreeStorageBytes) + " MiB.";
        }

        /// <summary>
        /// Produces one material, independently verifiable result inside an existing
        /// fail-closed StartRoom staging folder. This deliberately leaves the incomplete
        /// marker in place because the remaining D025-D100 states are not captured yet.
        /// </summary>
        public static string CaptureDoorAngleBasisStartD000Checkpoint()
        {
            ReceiverSpec spec = EndpointNativeV2StartSpec;
            if (!TryValidateEditorState(out string preflightFailure))
                return "FAIL: " + preflightFailure;
            if (captureInProgress)
                return "FAIL: another receiver/angle capture is already in progress.";

            captureInProgress = true;
            EditorSessionSnapshot snapshot = null;
            ProductionInputGuard[] inputGuards = null;
            bool thisRunStartedBake = false;
            string result = null;
            try
            {
                snapshot = EditorSessionSnapshot.Capture();
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;
                inputGuards = ProductionInputGuard.CaptureFor(spec);

                string receiverFolder = GetAngleReceiverFolder(spec);
                string markerPath = GetAnglePartialMarkerPath(spec);
                if ((!AssetDatabase.IsValidFolder(receiverFolder) &&
                     !Directory.Exists(GetOwnedAnglePhysicalPath(receiverFolder))) ||
                    (AssetDatabase.LoadAssetAtPath<TextAsset>(markerPath) == null &&
                     !File.Exists(GetOwnedAnglePhysicalPath(markerPath))))
                {
                    throw new InvalidOperationException(
                        "D000 checkpoint requires the existing fail-closed StartRoom staging folder.");
                }
                if (AssetDatabase.LoadAssetAtPath<DungeonPortalBakedBasisDoorAngleCapture>(
                        GetAngleCaptureAssetPath(spec)) != null)
                {
                    throw new InvalidOperationException(
                        "Refusing a partial checkpoint because the complete capture asset already exists.");
                }

                string stateFolder = GetAngleStateFolder(
                    spec,
                    DungeonPortalBakedBasisDoorAngleCapture.BaselineStateId);
                if (AssetDatabase.IsValidFolder(stateFolder) ||
                    Directory.Exists(GetOwnedAnglePhysicalPath(stateFolder)))
                {
                    throw new InvalidOperationException(
                        "D000 checkpoint output already exists and will not be overwritten: '" +
                        stateFolder + "'.");
                }

                LightingSettings lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(
                    GetAngleLightingSettingsPath(spec));
                if (lightingSettings == null || EditorUtility.IsDirty(lightingSettings))
                    throw new InvalidOperationException("The staged angle-capture LightingSettings is missing or dirty.");
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GetAngleWorkspacePath(spec)) == null)
                    BuildAngleCaptureWorkspace(spec, lightingSettings);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GetAngleWorkspacePath(spec)) == null)
                    throw new InvalidOperationException("The canonical angle workspace could not be reconstructed.");
                RepairLegacyEditorOnlyAngleWorkspaceMarker(spec);

                string canonicalWorkspaceHash = RestoreAngleCaptureCanonicalWorkspace(
                    spec,
                    lightingSettings,
                    null);
                string lightingHash = GetDependencyHash(GetAngleLightingSettingsPath(spec));
                DungeonPortalBakedBasisDoorAngleCapture.AngleState state =
                    BakeAndCaptureDoorAngleState(
                        spec,
                        lightingSettings,
                        canonicalWorkspaceHash,
                        lightingHash,
                        AngleCaptureContracts[0],
                        0,
                        0,
                        ref thisRunStartedBake);

                ProductionInputGuard.AssertUnchanged(inputGuards);
                string restoredHash = GetDependencyHash(GetAngleWorkspacePath(spec));
                if (!string.Equals(restoredHash, canonicalWorkspaceHash, StringComparison.Ordinal))
                    throw new InvalidOperationException("Canonical workspace hash drifted after D000 checkpoint.");

                string receiptPath = receiverFolder + "/StartRoom_R000_D000_CHECKPOINT.txt";
                AssertOwnedAngleCapturePath(receiptPath);
                string physicalReceipt = GetOwnedAnglePhysicalPath(receiptPath);
                if (AssetDatabase.LoadMainAssetAtPath(receiptPath) != null || File.Exists(physicalReceipt))
                    throw new InvalidOperationException("D000 checkpoint receipt already exists.");

                var receipt = new System.Text.StringBuilder(2048);
                receipt.AppendLine("DungeonPortalBakedBasisAngleCapture/checkpoint/v1");
                receipt.AppendLine("status=PASS");
                receipt.AppendLine("receiver=" + spec.RoomId);
                receipt.AppendLine("state=" + state.stateId);
                receipt.AppendLine("stateHash=" + state.stateHash);
                receipt.AppendLine("elapsedSeconds=" +
                    state.elapsedSeconds.ToString("R", CultureInfo.InvariantCulture));
                receipt.AppendLine("lightmapCount=" + state.lightmaps.Length);
                receipt.AppendLine("rendererCount=" + state.renderers.Length);
                receipt.AppendLine("probeCount=" + state.probes.Length);
                receipt.AppendLine("workspaceDependencyHash=" + canonicalWorkspaceHash);
                receipt.AppendLine("lightingSettingsDependencyHash=" + lightingHash);
                receipt.AppendLine("remainingStatesCaptured=false");
                receipt.AppendLine("incompleteMarkerIntentionallyRetained=true");
                for (int i = 0; i < state.lightmaps.Length; i++)
                {
                    receipt.AppendLine("lightmap[" + i + "].color=" + state.lightmaps[i].colorAssetPath);
                    receipt.AppendLine("lightmap[" + i + "].colorHash=" +
                        state.lightmaps[i].colorDependencyHash);
                    receipt.AppendLine("lightmap[" + i + "].direction=" +
                        state.lightmaps[i].directionAssetPath);
                    receipt.AppendLine("lightmap[" + i + "].directionHash=" +
                        state.lightmaps[i].directionDependencyHash);
                }
                File.WriteAllText(physicalReceipt, receipt.ToString());
                AssetDatabase.ImportAsset(receiptPath, ImportAssetOptions.ForceSynchronousImport);
                if (AssetDatabase.LoadAssetAtPath<TextAsset>(receiptPath) == null)
                    throw new InvalidOperationException("D000 checkpoint receipt did not import.");

                result = "PASS StartRoom_R000 D000 checkpoint\n" +
                         "state=" + state.stateId + "\n" +
                         "stateHash=" + state.stateHash + "\n" +
                         "lightmaps=" + state.lightmaps.Length + " renderers=" +
                         state.renderers.Length + " probes=" + state.probes.Length + "\n" +
                         "receipt=" + receiptPath + "\n" +
                         "remainingStatesCaptured=false";
            }
            catch (Exception exception)
            {
                result = "FAIL: StartRoom_R000 D000 checkpoint threw " + exception;
            }
            finally
            {
                try
                {
                    if (thisRunStartedBake && Lightmapping.isRunning)
                        Lightmapping.Cancel();
                    if (snapshot != null)
                    {
                        snapshot.Restore();
                        snapshot.AssertRestoredClean();
                    }
                    if (inputGuards != null)
                        ProductionInputGuard.AssertUnchanged(inputGuards);
                }
                catch (Exception restoreFailure)
                {
                    result = "FAIL: D000 checkpoint could not restore editor state: " +
                             restoreFailure + "\nPrevious result: " + result;
                }
                finally
                {
                    captureInProgress = false;
                }
            }

            return result ?? "FAIL: D000 checkpoint ended without a result.";
        }

        private static void RepairLegacyEditorOnlyAngleWorkspaceMarker(ReceiverSpec spec)
        {
            string workspacePath = GetAngleWorkspacePath(spec);
            Scene workspace = EditorSceneManager.OpenScene(workspacePath, OpenSceneMode.Single);
            AssertSoleWorkspaceScene(workspace, workspacePath, "angle marker migration");
            GameObject[] roots = workspace.GetRootGameObjects();
            if (roots.Length != 1 || roots[0] == null)
                throw new InvalidOperationException("Angle marker migration requires one workspace root.");
            GameObject realDoor = FindUniqueChildObject(roots[0].transform, DoorInstanceName);
            DungeonPortalBakedBasisAngleWorkspaceMarker[] markers =
                realDoor.GetComponentsInChildren<DungeonPortalBakedBasisAngleWorkspaceMarker>(true);
            if (markers.Length == 1 && markers[0] != null)
                return;
            if (markers.Length != 0)
                throw new InvalidOperationException("Angle workspace contains duplicate runtime markers.");

            int missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(realDoor);
            if (missingCount < 0 || missingCount > 1)
            {
                throw new InvalidOperationException(
                    "Expected zero or one stripped legacy Editor-only marker on the generated door. actual=" +
                    missingCount + ".");
            }
            if (missingCount == 1)
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(realDoor);
            Transform leaf = realDoor.transform.Find(DoorLeafPath);
            if (leaf == null)
                throw new InvalidOperationException("Angle marker migration could not resolve the door leaf.");
            var marker = realDoor.AddComponent<DungeonPortalBakedBasisAngleWorkspaceMarker>();
            marker.Configure(spec.RoomId, leaf.localRotation, Vector3.up, AngleCaptureOpenAngleDegrees);
            EditorUtility.SetDirty(marker);
            EditorSceneManager.MarkSceneDirty(workspace);
            if (!EditorSceneManager.SaveScene(workspace))
                throw new InvalidOperationException("Unable to save the migrated runtime angle marker.");
            AssetDatabase.ImportAsset(workspacePath, ImportAssetOptions.ForceSynchronousImport);
        }

        public static DoorAngleCaptureContract[] GetDoorAngleCaptureContractsForTest()
        {
            return (DoorAngleCaptureContract[])AngleCaptureContracts.Clone();
        }

        public static DoorAngleCaptureStorageEstimate GetDoorAngleCaptureStorageEstimateForTest()
        {
            return new DoorAngleCaptureStorageEstimate(
                ApproximatePersistedStorageBytes,
                RecommendedFreeStorageBytes);
        }

        public static int GetNoProxyEffectMaximumTrianglesPerRendererForTest()
        {
            return EffectMaximumTrianglesPerRenderer;
        }

        public static int ComputeNoProxyEffectTriangleStrideForTest(int triangleCount)
        {
            return ComputeEffectTriangleStride(triangleCount);
        }

        public static bool IsNoProxyEffectLightmapScaleOffsetValidForTest(Vector4 st)
        {
            return IsNoProxyEffectLightmapScaleOffsetValid(st);
        }

        public static Vector2 MapNoProxyEffectMeshUvToAtlasForTest(Vector2 meshUv, Vector4 st)
        {
            if (!IsNoProxyEffectLightmapScaleOffsetValid(st))
                throw new ArgumentException("The lightmap ST must be finite with positive scale.", nameof(st));
            ValidateNormalizedUv(meshUv, "test mesh UV2");
            return MapMeshUvToAtlas(meshUv, st, "test");
        }

        public static Quaternion ComputeDoorAnglePoseForTest(
            Quaternion closedLocalRotation,
            Vector3 localHingeAxis,
            float canonicalOpenAngleDegrees,
            float openFraction)
        {
            if (!IsFinite(openFraction) || openFraction < 0f || openFraction > 1f ||
                !IsFinite(canonicalOpenAngleDegrees) || canonicalOpenAngleDegrees <= 0f ||
                !IsFinite(localHingeAxis) || localHingeAxis.sqrMagnitude <= Mathf.Epsilon)
            {
                throw new ArgumentOutOfRangeException(nameof(openFraction));
            }
            return closedLocalRotation * Quaternion.AngleAxis(
                canonicalOpenAngleDegrees * openFraction,
                localHingeAxis.normalized);
        }

        public static bool TryNormalizeOwnedAnglePathForTest(
            string assetPath,
            out string canonicalAssetPath,
            out string error)
        {
            return TryGetCanonicalAngleCapturePath(
                assetPath,
                out canonicalAssetPath,
                out _,
                out error);
        }

        private static string CaptureDoorAngleReceiver(ReceiverSpec spec)
        {
            if (!TryValidateEditorState(out string preflightFailure))
                return "FAIL: " + preflightFailure;
            if (captureInProgress)
                return "FAIL: another receiver/angle capture is already in progress.";

            captureInProgress = true;
            EditorSessionSnapshot snapshot = null;
            ProductionInputGuard[] inputGuards = null;
            bool thisRunStartedBake = false;
            string result = null;
            try
            {
                snapshot = EditorSessionSnapshot.Capture();
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;
                inputGuards = ProductionInputGuard.CaptureFor(spec);

                if (TryUseCompletedAngleCapture(spec, inputGuards, out string completedResult))
                {
                    result = completedResult;
                    goto CaptureFinished;
                }

                BeginAngleCaptureOutput(spec);
                LightingSettings lightingSettings = CreateAngleCaptureLightingSettingsClone(spec);
                BuildAngleCaptureWorkspace(spec, lightingSettings);
                string canonicalWorkspaceHash = RestoreAngleCaptureCanonicalWorkspace(
                    spec,
                    lightingSettings,
                    null);
                string lightingHash = GetDependencyHash(GetAngleLightingSettingsPath(spec));
                DungeonPortalBakedBasisDoorAngleCapture.CaptureProvenance provenance =
                    BuildAngleCaptureProvenance(
                        spec,
                        inputGuards,
                        canonicalWorkspaceHash,
                        lightingHash);

                DungeonPortalBakedBasisDoorAngleCapture.DoorProxyProvenance proxyProvenance =
                    CaptureAngleProxyProvenance(spec, lightingSettings, canonicalWorkspaceHash);

                DungeonPortalBakedBasisDoorAngleCapture.AngleState baseline = default;
                var matchedBaselineStates = new DungeonPortalBakedBasisDoorAngleCapture.AngleState[4];
                var fullStates = new DungeonPortalBakedBasisDoorAngleCapture.AngleState[4];
                int expectedLightmapCount = 0;
                int expectedRendererCount = 0;
                baseline = BakeAndCaptureDoorAngleState(
                    spec,
                    lightingSettings,
                    canonicalWorkspaceHash,
                    lightingHash,
                    AngleCaptureContracts[0],
                    expectedLightmapCount,
                    expectedRendererCount,
                    ref thisRunStartedBake);
                expectedLightmapCount = baseline.lightmaps.Length;
                expectedRendererCount = baseline.renderers.Length;

                for (int poseIndex = 0; poseIndex < matchedBaselineStates.Length; poseIndex++)
                {
                    DoorAngleCaptureContract baselineContract = AngleCaptureContracts[1 + (poseIndex * 2)];
                    DoorAngleCaptureContract fullContract = AngleCaptureContracts[2 + (poseIndex * 2)];
                    DungeonPortalBakedBasisDoorAngleCapture.AngleState matchedBaseline =
                        BakeAndCaptureDoorAngleState(
                            spec,
                            lightingSettings,
                            canonicalWorkspaceHash,
                            lightingHash,
                            baselineContract,
                            expectedLightmapCount,
                            expectedRendererCount,
                            ref thisRunStartedBake);
                    DungeonPortalBakedBasisDoorAngleCapture.AngleState full =
                        BakeAndCaptureDoorAngleState(
                            spec,
                            lightingSettings,
                            canonicalWorkspaceHash,
                            lightingHash,
                            fullContract,
                            expectedLightmapCount,
                            expectedRendererCount,
                            ref thisRunStartedBake);
                    string baselineLayoutFailure = string.Empty;
                    string fullLayoutFailure = string.Empty;
                    string pairFailure = string.Empty;
                    if (!DungeonPortalBakedBasisDoorAngleCapture.TryValidateReceiverLayout(
                            baseline,
                            matchedBaseline,
                            out baselineLayoutFailure) ||
                        !DungeonPortalBakedBasisDoorAngleCapture.TryValidateReceiverLayout(
                            baseline,
                            full,
                            out fullLayoutFailure) ||
                        !DungeonPortalBakedBasisDoorAngleCapture.TryValidateMatchedPosePair(
                            matchedBaseline,
                            full,
                            out pairFailure))
                    {
                        throw new InvalidOperationException(
                            baselineContract.StateId + "/" + fullContract.StateId +
                            " same-pose pair validation failed. baselineLayout=" + baselineLayoutFailure +
                            " fullLayout=" + fullLayoutFailure + " pair=" + pairFailure);
                    }
                    matchedBaselineStates[poseIndex] = matchedBaseline;
                    fullStates[poseIndex] = full;
                }

                DungeonPortalBakedBasisDoorAngleCapture.NoProxyEffectProof effectProof =
                    BakeAndMeasureD050NoProxyControl(
                        spec,
                        lightingSettings,
                        canonicalWorkspaceHash,
                        lightingHash,
                        fullStates[1],
                        ref thisRunStartedBake);
                if (!DungeonPortalBakedBasisDoorAngleCapture.TryValidateEffectProof(
                        effectProof,
                        AngleCaptureOpenAngleDegrees,
                        out string effectFailure))
                {
                    throw new InvalidOperationException(effectFailure);
                }

                ProductionInputGuard.AssertUnchanged(inputGuards);
                AssertAngleLightingSettingsHash(lightingSettings, lightingHash);
                string restoredHash = RestoreAngleCaptureCanonicalWorkspace(
                    spec,
                    lightingSettings,
                    canonicalWorkspaceHash);
                if (!string.Equals(restoredHash, canonicalWorkspaceHash, StringComparison.Ordinal))
                    throw new InvalidOperationException("Canonical angle workspace dependency hash drifted.");

                string capturePath = GetAngleCaptureAssetPath(spec);
                var capture = ScriptableObject.CreateInstance<DungeonPortalBakedBasisDoorAngleCapture>();
                capture.name = spec.RoomId + "_DoorAngleCapture";
                capture.ConfigureAuthoring(
                    spec.RoomId,
                    StableDoorwayId,
                    AngleCaptureOpenAngleDegrees,
                    provenance,
                    proxyProvenance,
                    baseline,
                    matchedBaselineStates,
                    fullStates,
                    effectProof);
                if (!capture.TryValidate(out string structuralFailure))
                {
                    UnityEngine.Object.DestroyImmediate(capture);
                    throw new InvalidOperationException(
                        "Angle-capture asset failed structural validation before persistence: " + structuralFailure);
                }
                AssertOwnedAngleCapturePath(capturePath);
                if (AssetDatabase.LoadMainAssetAtPath(capturePath) != null)
                {
                    UnityEngine.Object.DestroyImmediate(capture);
                    throw new InvalidOperationException("Angle-capture asset path already exists: '" + capturePath + "'.");
                }
                AssetDatabase.CreateAsset(capture, capturePath);
                EditorUtility.SetDirty(capture);
                AssetDatabase.SaveAssetIfDirty(capture);
                AssetDatabase.ImportAsset(capturePath, ImportAssetOptions.ForceSynchronousImport);

                capture = AssetDatabase.LoadAssetAtPath<DungeonPortalBakedBasisDoorAngleCapture>(capturePath);
                ValidatePersistedAngleCapture(capture, spec, inputGuards);
                CompleteAngleCaptureOutput(spec);
                ProductionInputGuard.AssertUnchanged(inputGuards);

                result = "PASS DungeonPortalBakedBasis endpoint-native V2 canonical door-angle capture\n" +
                         "receiver=" + spec.RoomId + "\n" +
                         "asset=" + capturePath + "\n" +
                         "workspace=" + GetAngleWorkspacePath(spec) + "\n" +
                         "states=D000 zero/bypass; matched Baseline+Full pairs at D025,D050,D075,D100\n" +
                         "proof=D050 ShadowsOnly proxy versus no-proxy PASS\n" +
                         "productionMutation=none";
            CaptureFinished:;
            }
            catch (Exception exception)
            {
                result = "FAIL: door-angle capture for '" + spec.RoomId + "' threw " + exception +
                         "\nOwned partial output was preserved fail-closed at '" +
                         GetAngleReceiverFolder(spec) + "'.";
            }
            finally
            {
                try
                {
                    if (thisRunStartedBake && Lightmapping.isRunning)
                        Lightmapping.Cancel();
                    if (snapshot != null)
                    {
                        snapshot.Restore();
                        snapshot.AssertRestoredClean();
                    }
                    if (inputGuards != null)
                        ProductionInputGuard.AssertUnchanged(inputGuards);
                }
                catch (Exception restoreFailure)
                {
                    result = "FAIL: angle capture could not restore the editor session or prove production " +
                             "inputs unchanged: " + restoreFailure + "\nPrevious result: " + result;
                }
                finally
                {
                    captureInProgress = false;
                }
            }

            return result ?? "FAIL: door-angle capture ended without a result.";
        }

        private static bool TryUseCompletedAngleCapture(
            ReceiverSpec spec,
            ProductionInputGuard[] inputGuards,
            out string result)
        {
            result = string.Empty;
            string receiverFolder = GetAngleReceiverFolder(spec);
            bool folderExists = AssetDatabase.IsValidFolder(receiverFolder) ||
                                Directory.Exists(GetOwnedAnglePhysicalPath(receiverFolder));
            if (!folderExists)
                return false;

            string markerPath = GetAnglePartialMarkerPath(spec);
            DungeonPortalBakedBasisDoorAngleCapture capture =
                AssetDatabase.LoadAssetAtPath<DungeonPortalBakedBasisDoorAngleCapture>(
                    GetAngleCaptureAssetPath(spec));
            if (capture == null || AssetDatabase.LoadAssetAtPath<TextAsset>(markerPath) != null ||
                File.Exists(GetOwnedAnglePhysicalPath(markerPath)))
            {
                throw new InvalidOperationException(
                    "Angle-capture receiver folder already exists but is incomplete. It is not overwritten: '" +
                    receiverFolder + "'.");
            }

            ValidatePersistedAngleCapture(capture, spec, inputGuards);
            result = "PASS DungeonPortalBakedBasis endpoint-native V2 canonical door-angle capture already complete\n" +
                     "receiver=" + spec.RoomId + "\nasset=" + GetAngleCaptureAssetPath(spec);
            return true;
        }

        private static void BeginAngleCaptureOutput(ReceiverSpec spec)
        {
            EnsureAngleCaptureFolder(AngleCaptureRoot);
            string receiverFolder = GetAngleReceiverFolder(spec);
            if (AssetDatabase.IsValidFolder(receiverFolder) ||
                Directory.Exists(GetOwnedAnglePhysicalPath(receiverFolder)))
            {
                throw new InvalidOperationException(
                    "Refusing to overwrite an existing angle-capture receiver folder: '" + receiverFolder + "'.");
            }
            EnsureAngleCaptureFolder(receiverFolder);
            string markerPath = GetAnglePartialMarkerPath(spec);
            string physicalMarker = GetOwnedAnglePhysicalPath(markerPath);
            File.WriteAllText(
                physicalMarker,
                "DungeonPortalBakedBasisAngleCapture/incomplete/v3\nsource=endpoint-native-v2-canonical\nreceiver=" + spec.RoomId +
                "\ncreatedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\n");
            AssetDatabase.ImportAsset(markerPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.LoadAssetAtPath<TextAsset>(markerPath) == null)
                throw new InvalidOperationException("Unable to persist angle-capture ownership marker.");
        }

        private static void CompleteAngleCaptureOutput(ReceiverSpec spec)
        {
            string markerPath = GetAnglePartialMarkerPath(spec);
            AssertOwnedAngleCapturePath(markerPath);
            if (AssetDatabase.LoadAssetAtPath<TextAsset>(markerPath) == null ||
                !AssetDatabase.DeleteAsset(markerPath))
            {
                throw new InvalidOperationException(
                    "Unable to remove the completed angle-capture ownership marker: '" + markerPath + "'.");
            }
        }

        private static LightingSettings CreateAngleCaptureLightingSettingsClone(ReceiverSpec spec)
        {
            LightingSettings source = AssetDatabase.LoadAssetAtPath<LightingSettings>(OfficialLightingSettingsPath);
            if (source == null || EditorUtility.IsDirty(source))
                throw new InvalidOperationException("Official LightingSettings is missing or dirty.");

            string folder = GetAngleLightingFolder(spec);
            EnsureAngleCaptureFolder(folder);
            string clonePath = GetAngleLightingSettingsPath(spec);
            AssertOwnedAngleCapturePath(clonePath);
            if (AssetDatabase.LoadMainAssetAtPath(clonePath) != null ||
                !AssetDatabase.CopyAsset(OfficialLightingSettingsPath, clonePath))
            {
                throw new InvalidOperationException(
                    "Unable to create the isolated angle-capture LightingSettings clone: '" + clonePath + "'.");
            }
            AssetDatabase.ImportAsset(clonePath, ImportAssetOptions.ForceSynchronousImport);
            LightingSettings clone = AssetDatabase.LoadAssetAtPath<LightingSettings>(clonePath);
            if (clone == null || EditorUtility.IsDirty(clone))
                throw new InvalidOperationException("Angle-capture LightingSettings clone did not import cleanly.");
            return clone;
        }

        private static void BuildAngleCaptureWorkspace(ReceiverSpec spec, LightingSettings lightingSettings)
        {
            string workspaceFolder = GetAngleWorkspaceFolder(spec);
            string workspacePath = GetAngleWorkspacePath(spec);
            EnsureAngleCaptureFolder(workspaceFolder);
            AssertOwnedAngleCapturePath(workspacePath);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(workspacePath) != null)
                throw new InvalidOperationException("Angle-capture workspace already exists: '" + workspacePath + "'.");

            Scene workspace = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssertSoleWorkspaceScene(workspace, workspacePath, "angle workspace construction");
            EditorSceneManager.SetActiveScene(workspace);
            Lightmapping.lightingSettings = lightingSettings;
            ConfigureFlatBlackEnvironment();
            ClearWorkspaceBakeData(workspace, workspacePath);

            GameObject roomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.RoomPrefabPath);
            GameObject doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath);
            if (roomPrefab == null || doorPrefab == null)
                throw new InvalidOperationException("Required production room/door prefab is missing.");

            GameObject room = PrefabUtility.InstantiatePrefab(roomPrefab, workspace) as GameObject;
            if (room == null)
                throw new InvalidOperationException("Unable to instantiate the production receiver prefab.");
            room.name = GetRoomInstanceName(spec);
            room.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            room.transform.localScale = Vector3.one;

            Doorway doorway = ResolveExactStableDoorway(room, spec.RoomId);
            ConfigureDoorwayVisualsForRealDoor(doorway.transform);
            ApplyReceiverP0BakeState(room, spec);
            GameObject realDoor = InstantiateDoorUsingDunGenPlacement(doorPrefab, workspace, doorway);
            Transform leaf = BindAngleDoorClosed(realDoor, doorway);
            var marker = realDoor.AddComponent<DungeonPortalBakedBasisAngleWorkspaceMarker>();
            marker.Configure(spec.RoomId, leaf.localRotation, Vector3.up, AngleCaptureOpenAngleDegrees);

            MarkReceiverContributeGiWithBakeExclusions(room, realDoor.transform);
            DisableReceiverReflectionProbes(room, realDoor.transform);
            ApplyDungeonRenderingLayerPolicy(room);
            ApplyDungeonRenderingLayerPolicy(realDoor);
            AssertAngleDoorPresentation(realDoor, leaf, marker, 0f);

            int cullingMask = CalculateActualGameObjectLayerUnion(room, realDoor.transform, leaf);
            if (cullingMask != ExpectedCullingMask)
            {
                throw new InvalidOperationException(
                    "Angle workspace receiver+door layer union drifted. expected=" + ExpectedCullingMask +
                    " actual=" + cullingMask + ".");
            }
            CreateCanonicalInjector(doorway.transform, cullingMask, spec.ExpectedInjectorRange);

            EditorSceneManager.MarkSceneDirty(workspace);
            if (!EditorSceneManager.SaveScene(workspace, workspacePath, false))
                throw new InvalidOperationException("Unable to save isolated angle workspace: '" + workspacePath + "'.");
            AssetDatabase.ImportAsset(workspacePath, ImportAssetOptions.ForceSynchronousImport);
            AngleWorkspaceObjects objects = ResolveAngleWorkspaceObjects(workspace, spec);
            AssertAngleDoorPresentation(objects.RealDoor, objects.DoorLeaf, objects.Marker, 0f);
            AssertNoAngleProxy(objects.RealDoor);
        }

        private static Transform BindAngleDoorClosed(GameObject realDoor, Doorway placementOwner)
        {
            Transform leaf = realDoor != null ? realDoor.transform.Find(DoorLeafPath) : null;
            if (leaf == null)
                throw new InvalidOperationException("The real door prefab has no '" + DoorLeafPath + "' leaf.");
            DunGen.Door[] doors = realDoor.GetComponentsInChildren<DunGen.Door>(true);
            if (doors.Length != 1)
                throw new InvalidOperationException("Expected exactly one DunGen.Door on the real door prefab.");
            Tile tile = placementOwner.GetComponentInParent<Tile>();
            if (tile == null)
                throw new InvalidOperationException("The exact doorway has no parent DunGen.Tile.");
            doors[0].DoorwayA = placementOwner;
            doors[0].TileA = tile;
            doors[0].IsOpen = false;
            EditorUtility.SetDirty(doors[0]);
            return leaf;
        }

        private static string RestoreAngleCaptureCanonicalWorkspace(
            ReceiverSpec spec,
            LightingSettings lightingSettings,
            string expectedDependencyHash)
        {
            if (Lightmapping.isRunning)
                throw new InvalidOperationException("Cannot restore angle workspace while lightmapping is running.");
            string workspacePath = GetAngleWorkspacePath(spec);
            Scene workspace = EditorSceneManager.OpenScene(workspacePath, OpenSceneMode.Single);
            AssertSoleWorkspaceScene(workspace, workspacePath, "angle canonical restore");
            EditorSceneManager.SetActiveScene(workspace);
            Lightmapping.lightingSettings = lightingSettings;
            ConfigureFlatBlackEnvironment();
            AngleWorkspaceObjects objects = ResolveAngleWorkspaceObjects(workspace, spec);
            AssertNoAngleProxy(objects.RealDoor);
            ApplyReceiverP0BakeState(objects.Room, spec, objects.RealDoor.transform);
            DisableReceiverReflectionProbes(objects.Room, objects.RealDoor.transform);
            ApplyDungeonRenderingLayerPolicy(objects.Room);
            ApplyDungeonRenderingLayerPolicy(objects.RealDoor);
            ApplyAngleDoorPose(objects, 0f);
            ConfigureCanonicalInjector(
                objects.Injector,
                new BakeStateSpec(DungeonPortalBakedBasisDoorAngleCapture.BaselineStateId, false, 0f),
                ExpectedCullingMask,
                spec.ExpectedInjectorRange);
            ClearWorkspaceBakeData(workspace, workspacePath);
            Lightmapping.lightingSettings = lightingSettings;
            ConfigureCanonicalInjector(
                objects.Injector,
                new BakeStateSpec(DungeonPortalBakedBasisDoorAngleCapture.BaselineStateId, false, 0f),
                ExpectedCullingMask,
                spec.ExpectedInjectorRange);
            AssertAngleDoorPresentation(objects.RealDoor, objects.DoorLeaf, objects.Marker, 0f);
            AssertNoAngleProxy(objects.RealDoor);
            EditorSceneManager.MarkSceneDirty(workspace);
            if (!EditorSceneManager.SaveScene(workspace))
                throw new InvalidOperationException("Unable to save canonical angle workspace: '" + workspacePath + "'.");
            AssetDatabase.ImportAsset(workspacePath, ImportAssetOptions.ForceSynchronousImport);
            if (workspace.isDirty || Lightmapping.lightingDataAsset != null ||
                (LightmapSettings.lightmaps != null && LightmapSettings.lightmaps.Length != 0))
            {
                throw new InvalidOperationException("Canonical angle workspace is not saved, clear, and proxy-free.");
            }
            string hash = GetDependencyHash(workspacePath);
            if (!string.IsNullOrWhiteSpace(expectedDependencyHash) &&
                !string.Equals(hash, expectedDependencyHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Canonical angle workspace hash changed after a transient bake. expected='" +
                    expectedDependencyHash + "' actual='" + hash + "'.");
            }
            return hash;
        }

        private static AngleWorkspaceObjects ResolveAngleWorkspaceObjects(Scene workspace, ReceiverSpec spec)
        {
            string workspacePath = GetAngleWorkspacePath(spec);
            AssertSoleWorkspaceScene(workspace, workspacePath, "angle workspace topology validation");
            GameObject[] roots = workspace.GetRootGameObjects();
            if (roots.Length != 1 || roots[0] == null ||
                !string.Equals(roots[0].name, GetRoomInstanceName(spec), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Angle workspace must contain exactly one canonical receiver root.");
            }
            GameObject room = roots[0];
            if (!string.Equals(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(room),
                    spec.RoomPrefabPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Angle workspace room prefab link drifted.");
            }
            Doorway doorwayComponent = ResolveExactStableDoorway(room, spec.RoomId);
            GameObject realDoor = FindUniqueChildObject(room.transform, DoorInstanceName);
            if (!string.Equals(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(realDoor),
                    DoorPrefabPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Angle workspace door prefab link drifted.");
            }
            Transform leaf = realDoor.transform.Find(DoorLeafPath);
            if (leaf == null)
                throw new InvalidOperationException("Angle workspace real door has no leaf.");
            Transform injectorTransform = doorwayComponent.transform.Find(InjectorName);
            Light injector = injectorTransform != null ? injectorTransform.GetComponent<Light>() : null;
            if (injector == null || injectorTransform.GetComponents<Light>().Length != 1)
                throw new InvalidOperationException("Angle workspace has no unique canonical injector.");
            DungeonPortalBakedBasisAngleWorkspaceMarker[] markers =
                realDoor.GetComponentsInChildren<DungeonPortalBakedBasisAngleWorkspaceMarker>(true);
            if (markers.Length != 1 || markers[0] == null ||
                !string.Equals(markers[0].RoomId, spec.RoomId, StringComparison.Ordinal) ||
                markers[0].LocalHingeAxis.sqrMagnitude <= Mathf.Epsilon ||
                !Approximately(markers[0].OpenAngleDegrees, AngleCaptureOpenAngleDegrees, 0.0001f))
            {
                throw new InvalidOperationException("Angle workspace marker identity/hinge contract drifted.");
            }
            return new AngleWorkspaceObjects(
                room,
                doorwayComponent.transform,
                realDoor,
                leaf,
                injector,
                markers[0]);
        }

        private static void ApplyAngleDoorPose(AngleWorkspaceObjects objects, float fraction)
        {
            if (objects == null)
                throw new ArgumentNullException(nameof(objects));
            objects.DoorLeaf.localRotation = ComputeDoorAnglePoseForTest(
                objects.Marker.ClosedLocalRotation,
                objects.Marker.LocalHingeAxis,
                objects.Marker.OpenAngleDegrees,
                fraction);
            DunGen.Door[] doors = objects.RealDoor.GetComponentsInChildren<DunGen.Door>(true);
            if (doors.Length != 1)
                throw new InvalidOperationException("Angle workspace no longer has exactly one DunGen.Door.");
            doors[0].IsOpen = fraction > 0f;
            EditorUtility.SetDirty(objects.DoorLeaf);
            EditorUtility.SetDirty(doors[0]);
            AssertAngleDoorPresentation(
                objects.RealDoor,
                objects.DoorLeaf,
                objects.Marker,
                fraction);
        }

        private static void AssertAngleDoorPresentation(
            GameObject realDoor,
            Transform leaf,
            DungeonPortalBakedBasisAngleWorkspaceMarker marker,
            float fraction)
        {
            if (realDoor == null || leaf == null || marker == null)
                throw new ArgumentNullException("Angle door presentation inputs must be non-null.");
            Quaternion expected = ComputeDoorAnglePoseForTest(
                marker.ClosedLocalRotation,
                marker.LocalHingeAxis,
                marker.OpenAngleDegrees,
                fraction);
            if (Quaternion.Angle(leaf.localRotation, expected) > 0.01f)
                throw new InvalidOperationException("Door leaf does not match the requested angle fraction.");
            Transform[] transforms = realDoor.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (GameObjectUtility.GetStaticEditorFlags(transforms[i].gameObject) != 0)
                {
                    throw new InvalidOperationException(
                        "Movable real-door subtree acquired static flags at '" + transforms[i].name + "'.");
                }
            }
            MeshRenderer baseRenderer = leaf.GetComponent<MeshRenderer>();
            MeshFilter baseFilter = leaf.GetComponent<MeshFilter>();
            if (baseRenderer == null || baseRenderer.enabled || baseFilter == null || baseFilter.sharedMesh == null)
                throw new InvalidOperationException("Disabled full-mesh Door_01 renderer contract drifted.");
            MeshRenderer positive = RequireUniqueNamedMeshRenderer(realDoor.transform, DoorPositiveRendererName);
            MeshRenderer negative = RequireUniqueNamedMeshRenderer(realDoor.transform, DoorNegativeRendererName);
            MeshRenderer edge = RequireUniqueNamedMeshRenderer(realDoor.transform, DoorEdgeRendererName);
            if (!positive.enabled || positive.forceRenderingOff ||
                !negative.enabled || negative.forceRenderingOff ||
                !edge.enabled || edge.forceRenderingOff)
            {
                throw new InvalidOperationException("Door split presentation renderers are not exactly enabled.");
            }
            LightProbeGroup[] groups = realDoor.GetComponentsInChildren<LightProbeGroup>(true);
            if (groups.Length != 1 || groups[0] == null || !groups[0].enabled ||
                groups[0].probePositions == null || groups[0].probePositions.Length != 8)
            {
                throw new InvalidOperationException("Real door did not preserve its enabled eight-point probe group.");
            }
            DunGen.Door[] doors = realDoor.GetComponentsInChildren<DunGen.Door>(true);
            if (doors.Length != 1 || doors[0].IsOpen != (fraction > 0f))
                throw new InvalidOperationException("DunGen.Door open state does not match the angle fraction.");
            AssertNoAngleProxy(realDoor);
        }

        private static MeshRenderer RequireUniqueNamedMeshRenderer(Transform root, string expectedName)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            MeshRenderer result = null;
            int count = 0;
            for (int i = 0; i < transforms.Length; i++)
            {
                if (!string.Equals(transforms[i].name, expectedName, StringComparison.Ordinal))
                    continue;
                result = transforms[i].GetComponent<MeshRenderer>();
                if (result == null)
                    throw new InvalidOperationException("Named door renderer has no MeshRenderer: '" + expectedName + "'.");
                count++;
            }
            if (count != 1)
                throw new InvalidOperationException("Expected one door renderer named '" + expectedName + "', found " + count + ".");
            return result;
        }

        private static void AssertNoAngleProxy(GameObject realDoor)
        {
            Transform[] transforms = realDoor.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null &&
                    transforms[i].name.StartsWith(AngleCaptureProxyName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Transient angle door proxy leaked into presentation/workspace state.");
                }
            }
        }

        private static DungeonPortalBakedBasisDoorAngleCapture.CaptureProvenance
            BuildAngleCaptureProvenance(
                ReceiverSpec spec,
                ProductionInputGuard[] inputGuards,
                string workspaceHash,
                string lightingHash)
        {
            return new DungeonPortalBakedBasisDoorAngleCapture.CaptureProvenance
            {
                toolVersion = AngleCaptureToolVersion,
                unityVersion = Application.unityVersion,
                receiverRoomId = spec.RoomId,
                stableDoorwayId = StableDoorwayId,
                workspaceScenePath = GetAngleWorkspacePath(spec),
                canonicalWorkspaceDependencyHash = workspaceHash,
                lightingSettingsClonePath = GetAngleLightingSettingsPath(spec),
                lightingSettingsCloneDependencyHash = lightingHash,
                capturedUtcIso8601 = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                productionInputs = ProductionInputGuard.ToFingerprints(inputGuards)
            };
        }

        private static DungeonPortalBakedBasisDoorAngleCapture.DoorProxyProvenance
            CaptureAngleProxyProvenance(
                ReceiverSpec spec,
                LightingSettings lightingSettings,
                string canonicalWorkspaceHash)
        {
            string workspacePath = GetAngleWorkspacePath(spec);
            Scene workspace = EditorSceneManager.OpenScene(workspacePath, OpenSceneMode.Single);
            AssertSoleWorkspaceScene(workspace, workspacePath, "angle proxy provenance inspection");
            Lightmapping.lightingSettings = lightingSettings;
            AngleWorkspaceObjects objects = ResolveAngleWorkspaceObjects(workspace, spec);
            AssertAngleDoorPresentation(objects.RealDoor, objects.DoorLeaf, objects.Marker, 0f);
            DoorProxySource source = DoorProxySource.Capture(objects);
            DungeonPortalBakedBasisDoorAngleCapture.DoorProxyProvenance result = source.BuildProvenance();
            if (!string.Equals(GetDependencyHash(workspacePath), canonicalWorkspaceHash, StringComparison.Ordinal) ||
                workspace.isDirty)
            {
                throw new InvalidOperationException("Read-only door proxy provenance inspection changed the workspace.");
            }
            return result;
        }

        private static DungeonPortalBakedBasisDoorAngleCapture.AngleState
            BakeAndCaptureDoorAngleState(
                ReceiverSpec spec,
                LightingSettings lightingSettings,
                string canonicalWorkspaceHash,
                string lightingHash,
                DoorAngleCaptureContract contract,
                int expectedLightmapCount,
                int expectedRendererCount,
                ref bool thisRunStartedBake)
        {
            Exception failure = null;
            DoorProxySession proxySession = null;
            DungeonPortalBakedBasisDoorAngleCapture.AngleState result = default;
            try
            {
                AssertAngleLightingSettingsHash(lightingSettings, lightingHash);
                string workspacePath = GetAngleWorkspacePath(spec);
                Scene workspace = EditorSceneManager.OpenScene(workspacePath, OpenSceneMode.Single);
                AssertSoleWorkspaceScene(workspace, workspacePath, "angle state " + contract.StateId);
                if (workspace.isDirty ||
                    !string.Equals(GetDependencyHash(workspacePath), canonicalWorkspaceHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Angle state did not start from the canonical saved workspace.");
                }

                EditorSceneManager.SetActiveScene(workspace);
                Lightmapping.lightingSettings = lightingSettings;
                ConfigureFlatBlackEnvironment();
                AngleWorkspaceObjects objects = ResolveAngleWorkspaceObjects(workspace, spec);
                AssertNoAngleProxy(objects.RealDoor);
                ApplyReceiverP0BakeState(objects.Room, spec, objects.RealDoor.transform);
                DisableReceiverReflectionProbes(objects.Room, objects.RealDoor.transform);
                ApplyDungeonRenderingLayerPolicy(objects.Room);
                ApplyDungeonRenderingLayerPolicy(objects.RealDoor);
                ApplyAngleDoorPose(objects, contract.OpenFraction);
                ConfigureAngleInjector(objects.Injector, spec, contract);
                AssertAngleLightingSettingsHash(lightingSettings, lightingHash);

                // This signature intentionally covers only the dynamic door. Receiver
                // lightmap index/ST changes are legitimate bake outputs, while any door
                // mesh/material/shader/keyword/queue/MPB/renderer/pose drift is not.
                string doorPresentationBefore = CaptureDoorPresentationSignature(objects);
                proxySession = DoorProxySession.Begin(objects, contract);
                proxySession.AssertBakeState();

                DateTime startedUtc = DateTime.UtcNow;
                thisRunStartedBake = true;
                bool baked = Lightmapping.Bake();
                double elapsedSeconds = (DateTime.UtcNow - startedUtc).TotalSeconds;
                if (!baked || Lightmapping.isRunning)
                {
                    throw new InvalidOperationException(
                        "Synchronous angle bake failed. receiver=" + spec.RoomId + " state=" +
                        contract.StateId + " baked=" + baked + " running=" + Lightmapping.isRunning + ".");
                }

                AssertAngleLightingSettingsHash(lightingSettings, lightingHash);
                proxySession.RestorePresentationAndDestroyProxy();
                proxySession = null;
                AssertAngleDoorPresentation(
                    objects.RealDoor,
                    objects.DoorLeaf,
                    objects.Marker,
                    contract.OpenFraction);
                string doorPresentationAfter = CaptureDoorPresentationSignature(objects);
                if (!string.Equals(doorPresentationBefore, doorPresentationAfter, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Transient door proxy did not restore exact real-door presentation parity for " +
                        contract.StateId + ".");
                }

                RendererCapture[] rendererCaptures = CaptureRenderers(
                    objects.Room,
                    objects.RealDoor.transform,
                    objects.Doorway,
                    spec);
                if (expectedRendererCount > 0 && rendererCaptures.Length != expectedRendererCount)
                {
                    throw new InvalidOperationException(
                        "Receiver renderer count differs from D000. expected=" + expectedRendererCount +
                        " actual=" + rendererCaptures.Length + ".");
                }
                FullRendererInventory inventory = CaptureFullRendererInventory(
                    objects.Room,
                    objects.RealDoor,
                    objects.Doorway);
                AssertInventoryContainsNoAngleProxy(inventory.Entries);

                string stateFolder = GetAngleStateFolder(spec, contract.StateId);
                if (AssetDatabase.IsValidFolder(stateFolder) ||
                    Directory.Exists(GetOwnedAnglePhysicalPath(stateFolder)))
                {
                    throw new InvalidOperationException(
                        "Angle state output folder already exists and will not be overwritten: '" +
                        stateFolder + "'.");
                }
                EnsureAngleCaptureFolder(stateFolder);
                DungeonPortalReceiverResponseCapture.CaptureLightmap[] lightmaps =
                    CaptureAngleLightmaps(
                        rendererCaptures,
                        stateFolder,
                        spec,
                        expectedLightmapCount);
                DungeonPortalReceiverResponseCapture.ProbeSample[] probes =
                    CapturePoseAwareAngleProbeGrid(
                        objects,
                        contract,
                        out string probeLocalPositionSignature);

                result = new DungeonPortalBakedBasisDoorAngleCapture.AngleState
                {
                    captured = true,
                    stateId = contract.StateId,
                    openFraction = contract.OpenFraction,
                    angleDegrees = contract.AngleDegrees,
                    injectorEnabled = contract.InjectorEnabled,
                    injectorBounceIntensity = contract.InjectorBounceIntensity,
                    elapsedSeconds = elapsedSeconds,
                    capturedUtcIso8601 = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    stateFolderPath = stateFolder,
                    receiverLayoutSignature = ComputeRendererLayoutSignature(rendererCaptures),
                    fullRendererParitySignature = inventory.Signature,
                    doorPresentationParityVerified = true,
                    probeSampleCount = probes.Length,
                    probeLocalPositionSignature = probeLocalPositionSignature,
                    probeStencilPolicy = PoseAwareAngleProbeStencilPolicy,
                    injector = BuildInjectorProvenance(objects.Injector),
                    lightmaps = lightmaps,
                    renderers = ExtractRenderers(rendererCaptures),
                    probes = probes,
                    fullRendererInventory = inventory.Entries
                };
                result.stateHash = ComputeAngleStateHash(result);
                ValidateAngleStateArtifacts(result, spec, expectedLightmapCount, expectedRendererCount);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                try
                {
                    if (proxySession != null)
                        proxySession.RestorePresentationAndDestroyProxy();
                    if (thisRunStartedBake && Lightmapping.isRunning)
                        Lightmapping.Cancel();
                }
                catch (Exception cleanupFailure)
                {
                    failure = failure == null
                        ? cleanupFailure
                        : new AggregateException("Angle state and transient proxy cleanup both failed.", failure, cleanupFailure);
                }

                try
                {
                    RestoreAngleCaptureCanonicalWorkspace(
                        spec,
                        lightingSettings,
                        canonicalWorkspaceHash);
                }
                catch (Exception restoreFailure)
                {
                    failure = failure == null
                        ? restoreFailure
                        : new AggregateException("Angle state and canonical restore both failed.", failure, restoreFailure);
                }
            }

            if (failure != null)
                throw new InvalidOperationException("Angle state '" + contract.StateId + "' failed.", failure);
            return result;
        }

        private static void ConfigureAngleInjector(
            Light injector,
            ReceiverSpec spec,
            DoorAngleCaptureContract contract)
        {
            ConfigureCanonicalInjector(
                injector,
                new BakeStateSpec(
                    contract.StateId,
                    contract.InjectorEnabled,
                    contract.InjectorBounceIntensity),
                ExpectedCullingMask,
                spec.ExpectedInjectorRange);
            DungeonPortalReceiverResponseCapture.InjectorProvenance value =
                BuildInjectorProvenance(injector);
            if (value.enabled != contract.InjectorEnabled ||
                !Approximately(value.bounceIntensity, contract.InjectorBounceIntensity, 0.00001f) ||
                value.lightmapBakeType != LightmapBakeType.Baked ||
                value.cullingMask != ExpectedCullingMask ||
                value.renderingLayerMask != RequiredDungeonRenderingLayerMask ||
                value.shadowRenderingLayerMask != RequiredDungeonRenderingLayerMask ||
                !Approximately(value.range, spec.ExpectedInjectorRange, 0.0005f))
            {
                throw new InvalidOperationException("Angle-state injector contract was not applied exactly.");
            }
        }

        private static string CaptureDoorPresentationSignature(AngleWorkspaceObjects objects)
        {
            Renderer[] renderers = objects.RealDoor.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != 4)
            {
                throw new InvalidOperationException(
                    "Expected the disabled full door plus three split renderers, actual=" +
                    renderers.Length + ".");
            }
            var records = new List<string>(renderers.Length);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                string path = GetStableRelativePath(objects.RealDoor.transform, renderer.transform);
                string type = renderer.GetType().FullName ?? renderer.GetType().Name;
                int ordinal = GetRendererComponentOrdinal(renderer);
                string parity = BuildRendererParityMetadata(renderer, objects.Doorway);
                records.Add(path + "|" + type + "|" + ordinal.ToString(CultureInfo.InvariantCulture) + "|" + parity);
            }
            records.Sort(StringComparer.Ordinal);
            var builder = new System.Text.StringBuilder(records.Count * 192);
            for (int i = 0; i < records.Count; i++)
                AppendString(builder, records[i]);
            return ComputeSha256(builder.ToString());
        }

        private static string CaptureReceiverLayoutFreeParitySignature(
            GameObject room,
            Transform realDoorTransform,
            Transform doorway)
        {
            if (room == null || realDoorTransform == null || doorway == null)
                throw new ArgumentNullException(room == null ? nameof(room) :
                    realDoorTransform == null ? nameof(realDoorTransform) : nameof(doorway));

            Renderer[] renderers = GetReceiverRenderers(room, realDoorTransform);
            if (renderers == null || renderers.Length == 0)
                throw new InvalidOperationException("D050 no-proxy layout-free receiver inventory is empty.");

            var records = new List<string>(renderers.Length);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                string relativePath = GetStableRelativePath(room.transform, renderer.transform);
                string typeName = renderer.GetType().FullName ?? renderer.GetType().Name;
                int componentOrdinal = GetRendererComponentOrdinal(renderer);
                string parity = BuildRendererLayoutFreeParityMetadata(renderer, doorway);
                records.Add(
                    relativePath + "|" + typeName + "|" +
                    componentOrdinal.ToString(CultureInfo.InvariantCulture) + "|" + parity);
            }
            records.Sort(StringComparer.Ordinal);
            var builder = new System.Text.StringBuilder(records.Count * 192);
            for (int i = 0; i < records.Count; i++)
                AppendString(builder, records[i]);
            return ComputeSha256(builder.ToString());
        }

        /// <summary>
        /// Mirrors BuildRendererParityMetadata exactly except for lightmapIndex and
        /// lightmapScaleOffset, which Unity may legitimately repack between the proxy
        /// and no-proxy bakes. This remains local to the proof path so the persisted
        /// nine-state strict parity contract is unchanged.
        /// </summary>
        private static string BuildRendererLayoutFreeParityMetadata(
            Renderer renderer,
            Transform doorway)
        {
            if (renderer == null || doorway == null)
                throw new ArgumentNullException(renderer == null ? nameof(renderer) : nameof(doorway));

            AssertRendererMaterialPropertyBlocksAreEmpty(renderer);
            var builder = new System.Text.StringBuilder(512);
            AppendString(builder, renderer.GetType().FullName ?? renderer.GetType().Name);
            builder.Append(renderer.enabled ? '1' : '0').Append('|');
            builder.Append(renderer.gameObject.activeSelf ? '1' : '0').Append('|');
            builder.Append(renderer.gameObject.activeInHierarchy ? '1' : '0').Append('|');
            builder.Append(renderer.gameObject.layer).Append('|');
            builder.Append(renderer.renderingLayerMask).Append('|');
            builder.Append((int)renderer.shadowCastingMode).Append('|');
            builder.Append(renderer.receiveShadows ? '1' : '0').Append('|');
            builder.Append(renderer.forceRenderingOff ? '1' : '0').Append('|');
            builder.Append((int)renderer.reflectionProbeUsage).Append('|');
            builder.Append((int)renderer.lightProbeUsage).Append('|');
            AppendObjectReferenceParity(builder, renderer.probeAnchor);
            AppendObjectReferenceParity(builder, renderer.lightProbeProxyVolumeOverride);
            builder.Append(renderer.sortingLayerID).Append('|');
            builder.Append(renderer.sortingOrder).Append('|');
            builder.Append(renderer.rendererPriority).Append('|');
            builder.Append((int)GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)).Append('|');
            AppendRendererGiAndScaleInLightmapParity(builder, renderer);
            AppendMeshParity(builder, GetRendererMesh(renderer));
            if (renderer is MeshRenderer meshRenderer)
            {
                if (meshRenderer.additionalVertexStreams != null &&
                    meshRenderer.additionalVertexStreams.HasVertexAttribute(VertexAttribute.TexCoord1))
                {
                    throw new InvalidOperationException(
                        "D050 no-proxy surface proof cannot safely resolve UV2 overridden by additional " +
                        "vertex streams. renderer='" + renderer.name + "'.");
                }
                AppendMeshParity(builder, meshRenderer.additionalVertexStreams);
            }
            else
                AppendString(builder, "<additionalVertexStreams-not-applicable>");

            Matrix4x4 doorwayToRenderer = doorway.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                    AppendFloat(builder, doorwayToRenderer[row, column]);
            }

            Material[] materials = renderer.sharedMaterials;
            builder.Append(materials != null ? materials.Length : 0).Append('|');
            if (materials != null)
            {
                for (int i = 0; i < materials.Length; i++)
                    AppendMaterialParity(builder, materials[i]);
            }
            return ComputeSha256(builder.ToString());
        }

        private static void AssertInventoryContainsNoAngleProxy(
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] entries)
        {
            if (entries == null || entries.Length == 0)
                throw new InvalidOperationException("Full renderer inventory is empty.");
            for (int i = 0; i < entries.Length; i++)
            {
                string path = entries[i].relativePath ?? string.Empty;
                if (path.IndexOf(AngleCaptureProxyName, StringComparison.Ordinal) >= 0)
                {
                    throw new InvalidOperationException(
                        "Transient door proxy leaked into captured renderer inventory.");
                }
            }
        }

        private static DungeonPortalReceiverResponseCapture.CaptureLightmap[] CaptureAngleLightmaps(
            RendererCapture[] renderers,
            string stateFolder,
            ReceiverSpec spec,
            int expectedLightmapCount)
        {
            if (LightmapSettings.lightmapsMode != LightmapsMode.CombinedDirectional)
            {
                throw new InvalidOperationException(
                    "Angle capture requires CombinedDirectional lightmaps. actual=" +
                    LightmapSettings.lightmapsMode + ".");
            }
            LightmapData[] sourceLightmaps = LightmapSettings.lightmaps;
            if (sourceLightmaps == null || sourceLightmaps.Length == 0)
                throw new InvalidOperationException("Angle bake produced no lightmaps.");

            List<int> usedIndices = CollectUsedLightmapIndices(renderers, sourceLightmaps.Length);
            if (expectedLightmapCount > 0 && usedIndices.Count != expectedLightmapCount)
            {
                throw new InvalidOperationException(
                    "Angle state used-atlas count differs from D000. receiver=" + spec.RoomId +
                    " expected=" + expectedLightmapCount + " actual=" + usedIndices.Count + ".");
            }
            string lightmapFolder = stateFolder + "/Lightmaps";
            EnsureAngleCaptureFolder(lightmapFolder);
            var captured = new DungeonPortalReceiverResponseCapture.CaptureLightmap[usedIndices.Count];
            for (int i = 0; i < usedIndices.Count; i++)
            {
                int sourceIndex = usedIndices[i];
                LightmapData source = sourceLightmaps[sourceIndex];
                if (source == null || source.lightmapColor == null || source.lightmapDir == null)
                {
                    throw new InvalidOperationException(
                        "CombinedDirectional atlas " + sourceIndex + " has no color+direction pair.");
                }
                Texture2D color = CopyAngleLightmapTexture(
                    source.lightmapColor,
                    lightmapFolder,
                    "LM" + sourceIndex + "_Color");
                Texture2D direction = CopyAngleLightmapTexture(
                    source.lightmapDir,
                    lightmapFolder,
                    "LM" + sourceIndex + "_Direction");
                Texture2D shadowMask = source.shadowMask != null
                    ? CopyAngleLightmapTexture(
                        source.shadowMask,
                        lightmapFolder,
                        "LM" + sourceIndex + "_ShadowMask")
                    : null;
                captured[i] = BuildLightmapRecord(sourceIndex, color, direction, shadowMask);
            }
            return captured;
        }

        private static Texture2D CopyAngleLightmapTexture(
            Texture2D source,
            string destinationFolder,
            string destinationStem)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(extension))
                throw new InvalidOperationException("Baked texture is not a persistent asset: '" + source.name + "'.");
            string destinationPath = destinationFolder + "/" + destinationStem + extension;
            AssertOwnedAngleCapturePath(destinationPath);
            if (AssetDatabase.LoadMainAssetAtPath(destinationPath) != null ||
                File.Exists(GetOwnedAnglePhysicalPath(destinationPath)) ||
                !AssetDatabase.CopyAsset(sourcePath, destinationPath))
            {
                throw new InvalidOperationException(
                    "Unable to copy baked angle texture from '" + sourcePath + "' to '" +
                    destinationPath + "'.");
            }
            AssetDatabase.ImportAsset(destinationPath, ImportAssetOptions.ForceSynchronousImport);
            Texture2D copied = AssetDatabase.LoadAssetAtPath<Texture2D>(destinationPath);
            if (copied == null)
                throw new InvalidOperationException("Copied angle lightmap did not reload: '" + destinationPath + "'.");
            return copied;
        }

        private static List<int> CollectUsedLightmapIndices(RendererCapture[] renderers, int sourceCount)
        {
            var used = new List<int>();
            for (int i = 0; i < renderers.Length; i++)
            {
                int index = renderers[i].Data.lightmapIndex;
                if (index < 0 || index >= sourceCount)
                    throw new InvalidOperationException("Receiver renderer has an invalid lightmap index " + index + ".");
                if (!used.Contains(index))
                    used.Add(index);
            }
            used.Sort();
            if (used.Count == 0)
                throw new InvalidOperationException("Receiver uses no baked lightmap atlases.");
            return used;
        }

        private static string ComputeAngleStateHash(
            DungeonPortalBakedBasisDoorAngleCapture.AngleState state)
        {
            var builder = new System.Text.StringBuilder(8192);
            builder.Append(state.captured ? '1' : '0').Append('|');
            AppendString(builder, state.stateId);
            AppendFloat(builder, state.openFraction);
            AppendFloat(builder, state.angleDegrees);
            builder.Append(state.injectorEnabled ? '1' : '0').Append('|');
            AppendFloat(builder, state.injectorBounceIntensity);
            AppendDouble(builder, state.elapsedSeconds);
            AppendString(builder, state.capturedUtcIso8601);
            AppendString(builder, state.stateFolderPath);
            AppendString(builder, state.receiverLayoutSignature);
            AppendString(builder, state.fullRendererParitySignature);
            builder.Append(state.doorPresentationParityVerified ? '1' : '0').Append('|');
            builder.Append(state.probeSampleCount).Append('|');
            AppendString(builder, state.probeLocalPositionSignature);
            AppendString(builder, state.probeStencilPolicy);
            AppendInjector(builder, state.injector);
            DungeonPortalReceiverResponseCapture.CaptureLightmap[] maps = state.lightmaps ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            for (int i = 0; i < maps.Length; i++)
                AppendLightmap(builder, maps[i]);
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] renderers = state.renderers ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            for (int i = 0; i < renderers.Length; i++)
                AppendRenderer(builder, renderers[i]);
            DungeonPortalReceiverResponseCapture.ProbeSample[] probes = state.probes ??
                Array.Empty<DungeonPortalReceiverResponseCapture.ProbeSample>();
            for (int i = 0; i < probes.Length; i++)
                AppendProbe(builder, probes[i]);
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] inventory =
                state.fullRendererInventory ??
                Array.Empty<DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry>();
            for (int i = 0; i < inventory.Length; i++)
                AppendFullRendererInventoryEntry(builder, inventory[i]);
            return ComputeSha256(builder.ToString());
        }

        private static void ValidateAngleStateArtifacts(
            DungeonPortalBakedBasisDoorAngleCapture.AngleState state,
            ReceiverSpec spec,
            int expectedLightmapCount,
            int expectedRendererCount)
        {
            if (!state.captured || string.IsNullOrWhiteSpace(state.stateId) ||
                !string.Equals(state.stateHash, ComputeAngleStateHash(state), StringComparison.Ordinal) ||
                !string.Equals(state.stateFolderPath, GetAngleStateFolder(spec, state.stateId), StringComparison.Ordinal) ||
                state.lightmaps == null || state.lightmaps.Length == 0 ||
                state.renderers == null || state.renderers.Length == 0 ||
                state.probes == null || state.probes.Length != 27 ||
                state.probeSampleCount != state.probes.Length ||
                !string.Equals(
                    state.probeLocalPositionSignature,
                    ComputeAngleProbeLocalPositionSignature(state.probes),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    state.probeStencilPolicy,
                    PoseAwareAngleProbeStencilPolicy,
                    StringComparison.Ordinal) ||
                state.fullRendererInventory == null || state.fullRendererInventory.Length == 0 ||
                (expectedLightmapCount > 0 && state.lightmaps.Length != expectedLightmapCount) ||
                (expectedRendererCount > 0 && state.renderers.Length != expectedRendererCount))
            {
                throw new InvalidOperationException("Angle state artifact payload failed its exact self-check.");
            }
            AssertInventoryContainsNoAngleProxy(state.fullRendererInventory);
            for (int i = 0; i < state.lightmaps.Length; i++)
                ValidateAngleLightmapRecord(state.lightmaps[i], state.stateFolderPath);
        }

        private static void ValidateAngleLightmapRecord(
            DungeonPortalReceiverResponseCapture.CaptureLightmap lightmap,
            string stateFolder)
        {
            ValidateAngleTextureReference(
                lightmap.colorTexture,
                lightmap.colorAssetPath,
                lightmap.colorDependencyHash,
                stateFolder);
            ValidateAngleTextureReference(
                lightmap.directionTexture,
                lightmap.directionAssetPath,
                lightmap.directionDependencyHash,
                stateFolder);
            if (lightmap.hasShadowMask)
            {
                ValidateAngleTextureReference(
                    lightmap.shadowMaskTexture,
                    lightmap.shadowMaskAssetPath,
                    lightmap.shadowMaskDependencyHash,
                    stateFolder);
            }
            else if (lightmap.shadowMaskTexture != null ||
                     !string.IsNullOrEmpty(lightmap.shadowMaskAssetPath) ||
                     !string.IsNullOrEmpty(lightmap.shadowMaskDependencyHash))
            {
                throw new InvalidOperationException("Lightmap shadow-mask nullability metadata is inconsistent.");
            }
        }

        private static void ValidateAngleTextureReference(
            Texture2D texture,
            string assetPath,
            string expectedHash,
            string stateFolder)
        {
            string canonicalPrefix = stateFolder + "/Lightmaps/";
            if (texture == null || string.IsNullOrWhiteSpace(assetPath) ||
                !assetPath.StartsWith(canonicalPrefix, StringComparison.Ordinal) ||
                !IsOwnedAngleCapturePath(assetPath) ||
                AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) != texture ||
                string.IsNullOrWhiteSpace(expectedHash) ||
                !string.Equals(GetDependencyHash(assetPath), expectedHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Persisted angle lightmap reference/hash is invalid: '" + assetPath + "'.");
            }
        }

        private static DungeonPortalBakedBasisDoorAngleCapture.NoProxyEffectProof
            BakeAndMeasureD050NoProxyControl(
                ReceiverSpec spec,
                LightingSettings lightingSettings,
                string canonicalWorkspaceHash,
                string lightingHash,
                DungeonPortalBakedBasisDoorAngleCapture.AngleState d050WithProxy,
                ref bool thisRunStartedBake)
        {
            Exception failure = null;
            DungeonPortalBakedBasisDoorAngleCapture.NoProxyEffectProof result = default;
            try
            {
                AssertAngleLightingSettingsHash(lightingSettings, lightingHash);
                string workspacePath = GetAngleWorkspacePath(spec);
                Scene workspace = EditorSceneManager.OpenScene(workspacePath, OpenSceneMode.Single);
                AssertSoleWorkspaceScene(workspace, workspacePath, "D050 no-proxy proof");
                if (workspace.isDirty ||
                    !string.Equals(GetDependencyHash(workspacePath), canonicalWorkspaceHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("No-proxy proof did not start from canonical D000.");
                }
                EditorSceneManager.SetActiveScene(workspace);
                Lightmapping.lightingSettings = lightingSettings;
                ConfigureFlatBlackEnvironment();
                AngleWorkspaceObjects objects = ResolveAngleWorkspaceObjects(workspace, spec);
                ApplyReceiverP0BakeState(objects.Room, spec, objects.RealDoor.transform);
                DisableReceiverReflectionProbes(objects.Room, objects.RealDoor.transform);
                ApplyDungeonRenderingLayerPolicy(objects.Room);
                ApplyDungeonRenderingLayerPolicy(objects.RealDoor);
                ApplyAngleDoorPose(objects, 0.5f);
                DoorAngleCaptureContract contract = GetRequiredAngleCaptureContract("D050_Full");
                ConfigureAngleInjector(objects.Injector, spec, contract);
                AssertNoAngleProxy(objects.RealDoor);
                string doorBefore = CaptureDoorPresentationSignature(objects);
                string receiverLayoutFreeParityBeforeBake =
                    CaptureReceiverLayoutFreeParitySignature(
                        objects.Room,
                        objects.RealDoor.transform,
                        objects.Doorway);

                thisRunStartedBake = true;
                bool baked = Lightmapping.Bake();
                if (!baked || Lightmapping.isRunning)
                    throw new InvalidOperationException("D050 no-proxy control bake did not complete synchronously.");
                AssertAngleLightingSettingsHash(lightingSettings, lightingHash);
                AssertAngleDoorPresentation(objects.RealDoor, objects.DoorLeaf, objects.Marker, 0.5f);
                AssertNoAngleProxy(objects.RealDoor);
                string doorAfter = CaptureDoorPresentationSignature(objects);
                if (!string.Equals(doorBefore, doorAfter, StringComparison.Ordinal))
                    throw new InvalidOperationException("No-proxy control changed the dynamic door presentation state.");
                string receiverLayoutFreeParityAfterBake =
                    CaptureReceiverLayoutFreeParitySignature(
                        objects.Room,
                        objects.RealDoor.transform,
                        objects.Doorway);

                RendererCapture[] currentCaptures = CaptureRenderers(
                    objects.Room,
                    objects.RealDoor.transform,
                    objects.Doorway,
                    spec);
                FullRendererInventory currentInventory = CaptureFullRendererInventory(
                    objects.Room,
                    objects.RealDoor,
                    objects.Doorway);
                AssertInventoryContainsNoAngleProxy(currentInventory.Entries);
                DungeonPortalReceiverResponseCapture.CaptureLightmap[] currentLayout =
                    CaptureCurrentLightmapLayout(currentCaptures);
                var currentState = new DungeonPortalBakedBasisDoorAngleCapture.AngleState
                {
                    lightmaps = currentLayout,
                    renderers = ExtractRenderers(currentCaptures),
                    probes = d050WithProxy.probes,
                    fullRendererInventory = currentInventory.Entries
                };
                if (!DungeonPortalBakedBasisDoorAngleCapture.TryValidateNoProxyReceiverCorrespondence(
                        d050WithProxy,
                        currentState,
                        receiverLayoutFreeParityBeforeBake,
                        receiverLayoutFreeParityAfterBake,
                        out string layoutFailure))
                {
                    throw new InvalidOperationException(
                        "D050 no-proxy proof receiver correspondence is invalid: " + layoutFailure);
                }

                EffectDifferenceMetrics metrics = MeasureReceiverLightmapDifference(
                    d050WithProxy,
                    currentCaptures);
                bool passed = metrics.SampledTexelCount > 0 && metrics.ChangedTexelCount > 0 &&
                              metrics.ChangedTexelFraction >= MinimumChangedTexelFraction &&
                              metrics.MeanAbsoluteRgbDifference >= MinimumMeanAbsoluteRgbDifference &&
                              metrics.MaximumAbsoluteRgbDifference >= MinimumMaximumAbsoluteRgbDifference;
                result = new DungeonPortalBakedBasisDoorAngleCapture.NoProxyEffectProof
                {
                    evaluated = true,
                    passed = passed,
                    stateId = DungeonPortalBakedBasisDoorAngleCapture.NoProxyProofStateId,
                    openFraction = 0.5f,
                    angleDegrees = 45f,
                    sampledTexelCount = metrics.SampledTexelCount,
                    changedTexelCount = metrics.ChangedTexelCount,
                    changedTexelFraction = metrics.ChangedTexelFraction,
                    meanAbsoluteRgbDifference = metrics.MeanAbsoluteRgbDifference,
                    maximumAbsoluteRgbDifference = metrics.MaximumAbsoluteRgbDifference,
                    requiredChangedTexelFraction = MinimumChangedTexelFraction,
                    requiredMeanAbsoluteRgbDifference = MinimumMeanAbsoluteRgbDifference,
                    requiredMaximumAbsoluteRgbDifference = MinimumMaximumAbsoluteRgbDifference,
                    comparisonPolicy =
                        "D050 Full receiver lightmap color, ShadowsOnly+ContributeGI proxy versus identical " +
                        "D050 no-proxy bake; exact sorted receiver identity/mesh/UV2 and ReceiverRoom inventory " +
                        "identity required. Atlas index/ST/dimensions may repack, but both layouts must be valid. " +
                        "The persisted inventory parity hash is excluded only here because it contains index/ST; " +
                        "Layout-free renderer/material/shader/keywords/queue/reflection/MPB/transform parity is " +
                        "exact before/after the control bake. Persistent-mesh UV2 triangle centroids plus three " +
                        "interior barycentric samples are mapped through each bake's own index/ST. Triangle " +
                        "ordinals use deterministic stride=ceil(triangleCount/4096), capped at 4096 per renderer, " +
                        "and are compared " +
                        "with bilinear linear-HDR RGBAHalf sampling; changed surface-sample delta>=1e-4. " +
                        "Control textures are not persisted."
                };
                if (!passed)
                {
                    throw new InvalidOperationException(
                        "Unity bake did not prove a material ShadowsOnly door-proxy effect. sampled=" +
                        metrics.SampledTexelCount + " changedFraction=" +
                        metrics.ChangedTexelFraction.ToString("R", CultureInfo.InvariantCulture) +
                        " meanAbs=" + metrics.MeanAbsoluteRgbDifference.ToString("R", CultureInfo.InvariantCulture) +
                        " maxAbs=" + metrics.MaximumAbsoluteRgbDifference.ToString("R", CultureInfo.InvariantCulture) + ".");
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                try
                {
                    if (thisRunStartedBake && Lightmapping.isRunning)
                        Lightmapping.Cancel();
                    RestoreAngleCaptureCanonicalWorkspace(
                        spec,
                        lightingSettings,
                        canonicalWorkspaceHash);
                }
                catch (Exception restoreFailure)
                {
                    failure = failure == null
                        ? restoreFailure
                        : new AggregateException("No-proxy proof and canonical restore both failed.", failure, restoreFailure);
                }
            }
            if (failure != null)
                throw new InvalidOperationException("D050 no-proxy effect proof failed.", failure);
            return result;
        }

        private static DungeonPortalReceiverResponseCapture.CaptureLightmap[]
            CaptureCurrentLightmapLayout(RendererCapture[] renderers)
        {
            if (LightmapSettings.lightmapsMode != LightmapsMode.CombinedDirectional)
                throw new InvalidOperationException("No-proxy proof requires CombinedDirectional lightmaps.");
            LightmapData[] maps = LightmapSettings.lightmaps;
            if (maps == null || maps.Length == 0)
                throw new InvalidOperationException("No-proxy proof bake produced no lightmaps.");
            List<int> indices = CollectUsedLightmapIndices(renderers, maps.Length);
            var result = new DungeonPortalReceiverResponseCapture.CaptureLightmap[indices.Count];
            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                LightmapData map = maps[index];
                if (map == null || map.lightmapColor == null || map.lightmapDir == null)
                    throw new InvalidOperationException("No-proxy proof atlas has no color+direction pair.");
                result[i] = BuildLightmapRecord(index, map.lightmapColor, map.lightmapDir, map.shadowMask);
            }
            return result;
        }

        private static EffectDifferenceMetrics MeasureReceiverLightmapDifference(
            DungeonPortalBakedBasisDoorAngleCapture.AngleState proxyState,
            RendererCapture[] currentRenderers)
        {
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] proxyRenderers =
                CloneAndSortEffectRenderers(proxyState.renderers);
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] noProxyRenderers =
                CloneAndSortEffectRenderers(ExtractRenderers(currentRenderers));
            if (proxyRenderers.Length == 0 || proxyRenderers.Length != noProxyRenderers.Length)
                throw new InvalidOperationException("Effect proof receiver renderer correspondence is empty or unequal.");

            Dictionary<int, DungeonPortalReceiverResponseCapture.CaptureLightmap> storedMaps =
                BuildStoredEffectLightmapLookup(proxyState.lightmaps);
            LightmapData[] currentMaps = LightmapSettings.lightmaps;
            if (storedMaps.Count == 0 || currentMaps == null || currentMaps.Length == 0)
                throw new InvalidOperationException("Effect proof has no proxy/current lightmaps.");

            long sampled = 0;
            long changed = 0;
            double absoluteSum = 0d;
            float maximum = 0f;
            var imageCache = new Dictionary<Texture, LinearHalfImage>();
            var meshCache = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            for (int rendererIndex = 0; rendererIndex < proxyRenderers.Length; rendererIndex++)
            {
                DungeonPortalReceiverResponseCapture.CaptureRenderer proxyRenderer =
                    proxyRenderers[rendererIndex];
                DungeonPortalReceiverResponseCapture.CaptureRenderer noProxyRenderer =
                    noProxyRenderers[rendererIndex];
                if (CompareEffectReceiverIdentity(proxyRenderer, noProxyRenderer) != 0)
                {
                    throw new InvalidOperationException(
                        "Effect proof receiver identity/mesh/UV2 differs at sorted entry " + rendererIndex + ".");
                }

                if (!storedMaps.TryGetValue(
                        proxyRenderer.lightmapIndex,
                        out DungeonPortalReceiverResponseCapture.CaptureLightmap storedMap) ||
                    storedMap.colorTexture == null ||
                    storedMap.colorWidth != storedMap.colorTexture.width ||
                    storedMap.colorHeight != storedMap.colorTexture.height)
                {
                    throw new InvalidOperationException(
                        "Effect proof proxy renderer has no valid persisted color atlas at sorted entry " +
                        rendererIndex + ".");
                }
                if (noProxyRenderer.lightmapIndex < 0 ||
                    noProxyRenderer.lightmapIndex >= currentMaps.Length ||
                    currentMaps[noProxyRenderer.lightmapIndex] == null ||
                    currentMaps[noProxyRenderer.lightmapIndex].lightmapColor == null)
                {
                    throw new InvalidOperationException(
                        "Effect proof no-proxy renderer has no valid current color atlas at sorted entry " +
                        rendererIndex + ".");
                }

                Texture2D currentTexture = currentMaps[noProxyRenderer.lightmapIndex].lightmapColor;
                ValidateEffectRendererLayout(
                    proxyRenderer,
                    storedMap.colorTexture.width,
                    storedMap.colorTexture.height,
                    "proxy",
                    rendererIndex);
                ValidateEffectRendererLayout(
                    noProxyRenderer,
                    currentTexture.width,
                    currentTexture.height,
                    "no-proxy",
                    rendererIndex);

                Mesh mesh = ResolvePersistentEffectMesh(proxyRenderer, meshCache);
                Vector2[] uv2;
                int[] triangles;
                ReadAndValidateEffectMeshSurface(mesh, proxyRenderer, out uv2, out triangles);
                LinearHalfImage proxyImage = GetOrReadLinearHalfImage(storedMap.colorTexture, imageCache);
                LinearHalfImage noProxyImage = GetOrReadLinearHalfImage(currentTexture, imageCache);

                long rendererSampleCount = 0;
                int triangleCount = triangles.Length / 3;
                int triangleStride = ComputeEffectTriangleStride(triangleCount);
                int selectedTriangleCount = 0;
                for (int triangleOrdinal = 0;
                     triangleOrdinal < triangleCount &&
                     selectedTriangleCount < EffectMaximumTrianglesPerRenderer;
                     triangleOrdinal += triangleStride, selectedTriangleCount++)
                {
                    int triangle = triangleOrdinal * 3;
                    Vector2 uvA = uv2[triangles[triangle]];
                    Vector2 uvB = uv2[triangles[triangle + 1]];
                    Vector2 uvC = uv2[triangles[triangle + 2]];
                    float twiceArea = Mathf.Abs(
                        (uvB.x - uvA.x) * (uvC.y - uvA.y) -
                        (uvB.y - uvA.y) * (uvC.x - uvA.x));
                    if (!IsFinite(twiceArea))
                        throw new InvalidOperationException("Effect proof UV2 triangle area is non-finite.");
                    if (twiceArea <= 0.000000000001f)
                        continue;

                    for (int sampleIndex = 0;
                         sampleIndex < EffectSurfaceBarycentricSamples.Length;
                         sampleIndex++)
                    {
                        Vector3 barycentric = EffectSurfaceBarycentricSamples[sampleIndex];
                        Vector2 meshUv = uvA * barycentric.x + uvB * barycentric.y + uvC * barycentric.z;
                        ValidateNormalizedUv(meshUv, "mesh UV2");
                        Vector2 proxyAtlasUv = MapMeshUvToAtlas(
                            meshUv,
                            proxyRenderer.lightmapScaleOffset,
                            "proxy entry " + rendererIndex + " path '" +
                            proxyRenderer.relativePath + "' triangle " + triangleOrdinal +
                            " sample " + sampleIndex);
                        Vector2 noProxyAtlasUv = MapMeshUvToAtlas(
                            meshUv,
                            noProxyRenderer.lightmapScaleOffset,
                            "no-proxy entry " + rendererIndex + " path '" +
                            noProxyRenderer.relativePath + "' triangle " + triangleOrdinal +
                            " sample " + sampleIndex);
                        Vector3 proxyRgb = SampleLinearHalfBilinear(proxyImage, proxyAtlasUv);
                        Vector3 noProxyRgb = SampleLinearHalfBilinear(noProxyImage, noProxyAtlasUv);
                        float dr = Mathf.Abs(proxyRgb.x - noProxyRgb.x);
                        float dg = Mathf.Abs(proxyRgb.y - noProxyRgb.y);
                        float db = Mathf.Abs(proxyRgb.z - noProxyRgb.z);
                        float localMaximum = Mathf.Max(dr, Mathf.Max(dg, db));
                        if (!IsFinite(dr) || !IsFinite(dg) || !IsFinite(db) || !IsFinite(localMaximum))
                            throw new InvalidOperationException("Effect proof sampled HDR difference is non-finite.");
                        sampled++;
                        rendererSampleCount++;
                        absoluteSum += (dr + dg + db) / 3d;
                        if (localMaximum >= EffectChangedTexelDelta)
                            changed++;
                        maximum = Mathf.Max(maximum, localMaximum);
                    }
                }
                if (rendererSampleCount == 0)
                    throw new InvalidOperationException(
                        "Effect proof receiver mesh has no non-degenerate UV2 triangle samples at sorted entry " +
                        rendererIndex + ".");
            }
            if (sampled <= 0 || sampled > int.MaxValue || changed > int.MaxValue)
                throw new InvalidOperationException("Effect proof surface-sample cardinality is invalid.");
            return new EffectDifferenceMetrics(
                (int)sampled,
                (int)changed,
                (float)changed / sampled,
                (float)(absoluteSum / sampled),
                maximum);
        }

        private static int ComputeEffectTriangleStride(int triangleCount)
        {
            if (triangleCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(triangleCount));
            return (int)Math.Max(
                1L,
                ((long)triangleCount + EffectMaximumTrianglesPerRenderer - 1L) /
                EffectMaximumTrianglesPerRenderer);
        }

        private static DungeonPortalReceiverResponseCapture.CaptureRenderer[] CloneAndSortEffectRenderers(
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] source)
        {
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] result = source != null
                ? (DungeonPortalReceiverResponseCapture.CaptureRenderer[])source.Clone()
                : Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            Array.Sort(result, CompareEffectReceiverIdentity);
            return result;
        }

        private static int CompareEffectReceiverIdentity(
            DungeonPortalReceiverResponseCapture.CaptureRenderer left,
            DungeonPortalReceiverResponseCapture.CaptureRenderer right)
        {
            int result = string.CompareOrdinal(left.relativePath, right.relativePath);
            if (result != 0) return result;
            result = left.rendererBucketIndex.CompareTo(right.rendererBucketIndex);
            if (result != 0) return result;
            result = left.componentOrdinal.CompareTo(right.componentOrdinal);
            if (result != 0) return result;
            result = string.CompareOrdinal(left.meshAssetGuid, right.meshAssetGuid);
            if (result != 0) return result;
            result = left.meshLocalId.CompareTo(right.meshLocalId);
            if (result != 0) return result;
            return string.CompareOrdinal(left.meshUv2Hash, right.meshUv2Hash);
        }

        private static Dictionary<int, DungeonPortalReceiverResponseCapture.CaptureLightmap>
            BuildStoredEffectLightmapLookup(
                DungeonPortalReceiverResponseCapture.CaptureLightmap[] source)
        {
            source = source ?? Array.Empty<DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            var result = new Dictionary<int, DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            for (int i = 0; i < source.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.CaptureLightmap map = source[i];
                if (map.sourceLightmapIndex < 0 || map.colorTexture == null ||
                    map.colorWidth <= 0 || map.colorHeight <= 0 ||
                    result.ContainsKey(map.sourceLightmapIndex))
                {
                    throw new InvalidOperationException(
                        "Effect proof proxy lightmap inventory is invalid or duplicated at entry " + i + ".");
                }
                result.Add(map.sourceLightmapIndex, map);
            }
            return result;
        }

        private static void ValidateEffectRendererLayout(
            DungeonPortalReceiverResponseCapture.CaptureRenderer renderer,
            int atlasWidth,
            int atlasHeight,
            string label,
            int sortedEntry)
        {
            Vector4 st = renderer.lightmapScaleOffset;
            if (renderer.lightmapIndex < 0 || atlasWidth <= 0 || atlasHeight <= 0 ||
                !IsNoProxyEffectLightmapScaleOffsetValid(st))
            {
                throw new InvalidOperationException(
                    "Effect proof " + label + " lightmap index/ST/range is invalid at sorted entry " +
                    sortedEntry + ". index=" + renderer.lightmapIndex + " ST=(" +
                    st.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                    st.y.ToString("R", CultureInfo.InvariantCulture) + "," +
                    st.z.ToString("R", CultureInfo.InvariantCulture) + "," +
                    st.w.ToString("R", CultureInfo.InvariantCulture) + ").");
            }
        }

        private static bool IsNoProxyEffectLightmapScaleOffsetValid(Vector4 st)
        {
            // Unity may place the padded chart rectangle slightly outside the atlas
            // (for example, a small negative offset). The actual mesh UV2 samples are
            // mapped through this ST and range-checked individually below. Rejecting
            // the raw padded rectangle would incorrectly reject a valid Unity bake.
            return IsFinite(st) && st.x > 0f && st.y > 0f;
        }

        private static Mesh ResolvePersistentEffectMesh(
            DungeonPortalReceiverResponseCapture.CaptureRenderer renderer,
            Dictionary<string, Mesh> cache)
        {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (string.IsNullOrWhiteSpace(renderer.meshAssetGuid) || renderer.meshLocalId == 0L ||
                string.IsNullOrWhiteSpace(renderer.meshUv2Hash))
            {
                throw new InvalidOperationException("Effect proof receiver mesh identity is incomplete.");
            }

            string key = renderer.meshAssetGuid + ":" +
                         renderer.meshLocalId.ToString(CultureInfo.InvariantCulture);
            if (!cache.TryGetValue(key, out Mesh mesh))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(renderer.meshAssetGuid);
                if (string.IsNullOrWhiteSpace(assetPath))
                    throw new InvalidOperationException("Effect proof receiver mesh GUID cannot be resolved: " + key + ".");

                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                for (int i = 0; i < assets.Length; i++)
                {
                    if (!(assets[i] is Mesh candidate) ||
                        !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                            candidate,
                            out string candidateGuid,
                            out long candidateLocalId) ||
                        !string.Equals(candidateGuid, renderer.meshAssetGuid, StringComparison.Ordinal) ||
                        candidateLocalId != renderer.meshLocalId)
                    {
                        continue;
                    }
                    if (mesh != null)
                        throw new InvalidOperationException("Effect proof receiver mesh identity resolves ambiguously: " + key + ".");
                    mesh = candidate;
                }
                if (mesh == null || !EditorUtility.IsPersistent(mesh))
                    throw new InvalidOperationException("Effect proof receiver mesh GUID/localID cannot be resolved: " + key + ".");
                cache.Add(key, mesh);
            }

            string actualUv2Hash = ComputeUv2Hash(mesh);
            if (!string.Equals(actualUv2Hash, renderer.meshUv2Hash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Effect proof persistent mesh UV2 hash drifted for '" + renderer.relativePath + "'.");
            }
            return mesh;
        }

        private static void ReadAndValidateEffectMeshSurface(
            Mesh mesh,
            DungeonPortalReceiverResponseCapture.CaptureRenderer renderer,
            out Vector2[] uv2,
            out int[] triangles)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));
            try
            {
                uv2 = mesh.uv2;
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    if (mesh.GetTopology(subMesh) != MeshTopology.Triangles)
                    {
                        throw new InvalidOperationException(
                            "Effect proof receiver mesh contains a non-triangle submesh: '" + mesh.name + "'.");
                    }
                }
                triangles = mesh.triangles;
            }
            catch (Exception exception) when (!(exception is InvalidOperationException))
            {
                throw new InvalidOperationException(
                    "Effect proof cannot read persistent receiver mesh surface: '" + mesh.name + "'.",
                    exception);
            }

            if (uv2 == null || uv2.Length == 0 || uv2.Length != mesh.vertexCount)
            {
                throw new InvalidOperationException(
                    "Effect proof receiver mesh has no complete stored UV2 channel: '" + mesh.name + "'.");
            }
            for (int i = 0; i < uv2.Length; i++)
                ValidateNormalizedUv(uv2[i], "mesh UV2 vertex " + i);

            if (triangles == null || triangles.Length == 0 || triangles.Length % 3 != 0)
                throw new InvalidOperationException("Effect proof receiver mesh triangle payload is invalid: '" + mesh.name + "'.");
            for (int i = 0; i < triangles.Length; i++)
            {
                if (triangles[i] < 0 || triangles[i] >= uv2.Length)
                {
                    throw new InvalidOperationException(
                        "Effect proof receiver mesh triangle index is out of UV2 range: '" +
                        renderer.relativePath + "'.");
                }
            }
        }

        private static Vector2 MapMeshUvToAtlas(Vector2 meshUv, Vector4 st, string label)
        {
            Vector2 atlasUv = new Vector2(meshUv.x * st.x + st.z, meshUv.y * st.y + st.w);
            if (!IsFinite(atlasUv.x) || !IsFinite(atlasUv.y) ||
                atlasUv.x < -0.00001f || atlasUv.y < -0.00001f ||
                atlasUv.x > 1.00001f || atlasUv.y > 1.00001f)
            {
                throw new InvalidOperationException(
                    "Effect proof " + label + " mesh UV2 maps outside its lightmap atlas. meshUV=(" +
                    meshUv.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                    meshUv.y.ToString("R", CultureInfo.InvariantCulture) + ") ST=(" +
                    st.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                    st.y.ToString("R", CultureInfo.InvariantCulture) + "," +
                    st.z.ToString("R", CultureInfo.InvariantCulture) + "," +
                    st.w.ToString("R", CultureInfo.InvariantCulture) + ") atlasUV=(" +
                    atlasUv.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                    atlasUv.y.ToString("R", CultureInfo.InvariantCulture) + ").");
            }
            return new Vector2(Mathf.Clamp01(atlasUv.x), Mathf.Clamp01(atlasUv.y));
        }

        private static void ValidateNormalizedUv(Vector2 uv, string label)
        {
            if (!IsFinite(uv.x) || !IsFinite(uv.y) ||
                uv.x < -0.00001f || uv.y < -0.00001f ||
                uv.x > 1.00001f || uv.y > 1.00001f)
            {
                throw new InvalidOperationException("Effect proof " + label + " is non-finite or outside [0,1].");
            }
        }

        private static LinearHalfImage GetOrReadLinearHalfImage(
            Texture source,
            Dictionary<Texture, LinearHalfImage> cache)
        {
            if (source == null || cache == null)
                throw new ArgumentNullException(source == null ? nameof(source) : nameof(cache));
            if (cache.TryGetValue(source, out LinearHalfImage cached))
                return cached;

            Texture2D readback = ReadTextureToLinearHalf(source);
            try
            {
                var raw = readback.GetRawTextureData<ushort>();
                int expectedLength = checked(readback.width * readback.height * 4);
                if (raw.Length != expectedLength)
                    throw new InvalidOperationException("Effect proof RGBAHalf readback size is invalid.");
                var copy = new ushort[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                    copy[i] = raw[i];
                var image = new LinearHalfImage(readback.width, readback.height, copy);
                cache.Add(source, image);
                return image;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        private static Vector3 SampleLinearHalfBilinear(LinearHalfImage image, Vector2 uv)
        {
            ValidateNormalizedUv(uv, "atlas UV");
            float x = Mathf.Clamp01(uv.x) * image.Width - 0.5f;
            float y = Mathf.Clamp01(uv.y) * image.Height - 0.5f;
            int baseX = Mathf.FloorToInt(x);
            int baseY = Mathf.FloorToInt(y);
            int x0 = Mathf.Clamp(baseX, 0, image.Width - 1);
            int y0 = Mathf.Clamp(baseY, 0, image.Height - 1);
            int x1 = Mathf.Clamp(baseX + 1, 0, image.Width - 1);
            int y1 = Mathf.Clamp(baseY + 1, 0, image.Height - 1);
            float tx = x - baseX;
            float ty = y - baseY;
            var result = new Vector3(
                SampleLinearHalfChannel(image, x0, y0, x1, y1, tx, ty, 0),
                SampleLinearHalfChannel(image, x0, y0, x1, y1, tx, ty, 1),
                SampleLinearHalfChannel(image, x0, y0, x1, y1, tx, ty, 2));
            if (!IsFinite(result))
                throw new InvalidOperationException("Effect proof bilinear HDR sample is non-finite.");
            return result;
        }

        private static float SampleLinearHalfChannel(
            LinearHalfImage image,
            int x0,
            int y0,
            int x1,
            int y1,
            float tx,
            float ty,
            int channel)
        {
            float bottomLeft = ReadFiniteHalf(image, x0, y0, channel);
            float bottomRight = ReadFiniteHalf(image, x1, y0, channel);
            float topLeft = ReadFiniteHalf(image, x0, y1, channel);
            float topRight = ReadFiniteHalf(image, x1, y1, channel);
            float bottom = bottomLeft + (bottomRight - bottomLeft) * tx;
            float top = topLeft + (topRight - topLeft) * tx;
            float value = bottom + (top - bottom) * ty;
            if (!IsFinite(value))
                throw new InvalidOperationException("Effect proof bilinear HDR channel is non-finite.");
            return value;
        }

        private static float ReadFiniteHalf(LinearHalfImage image, int x, int y, int channel)
        {
            int offset = checked(((y * image.Width) + x) * 4 + channel);
            float value = HalfToFloat(image.Raw[offset]);
            if (!IsFinite(value))
                throw new InvalidOperationException("Effect proof RGBAHalf texel contains NaN or infinity.");
            return value;
        }

        private static Texture2D ReadTextureToLinearHalf(Texture source)
        {
            if (source == null || source.width <= 0 || source.height <= 0)
                throw new ArgumentException("HDR readback source is invalid.", nameof(source));
            RenderTexture temporary = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            var result = new Texture2D(
                source.width,
                source.height,
                TextureFormat.RGBAHalf,
                false,
                true);
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                result.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
                result.Apply(false, false);
                return result;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(result);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static float HalfToFloat(ushort half)
        {
            uint sign = (uint)(half >> 15) & 0x00000001u;
            int exponent = (half >> 10) & 0x0000001f;
            uint mantissa = (uint)half & 0x000003ffu;
            uint value;
            if (exponent == 0)
            {
                if (mantissa == 0)
                {
                    value = sign << 31;
                }
                else
                {
                    int unbiasedExponent = -14;
                    while ((mantissa & 0x00000400u) == 0)
                    {
                        mantissa <<= 1;
                        unbiasedExponent--;
                    }
                    mantissa &= 0x000003ffu;
                    uint adjustedExponent = (uint)(unbiasedExponent + 127);
                    value = (sign << 31) | (adjustedExponent << 23) | (mantissa << 13);
                }
            }
            else if (exponent == 31)
            {
                value = (sign << 31) | 0x7f800000u | (mantissa << 13);
            }
            else
            {
                uint adjustedExponent = (uint)(exponent - 15 + 127);
                value = (sign << 31) | (adjustedExponent << 23) | (mantissa << 13);
            }
            return BitConverter.Int32BitsToSingle(unchecked((int)value));
        }

        private static void ValidatePersistedAngleCapture(
            DungeonPortalBakedBasisDoorAngleCapture capture,
            ReceiverSpec spec,
            ProductionInputGuard[] inputGuards)
        {
            string capturePath = GetAngleCaptureAssetPath(spec);
            string structuralFailure = string.Empty;
            if (capture == null || EditorUtility.IsDirty(capture) ||
                !string.Equals(AssetDatabase.GetAssetPath(capture), capturePath, StringComparison.Ordinal) ||
                !capture.TryValidate(out structuralFailure))
            {
                throw new InvalidOperationException(
                    "Persisted angle-capture asset is missing, dirty, misplaced, or invalid: " +
                    structuralFailure);
            }
            if (!string.Equals(capture.ReceiverRoomId, spec.RoomId, StringComparison.Ordinal) ||
                !string.Equals(capture.StableDoorwayId, StableDoorwayId, StringComparison.Ordinal) ||
                !Approximately(capture.CanonicalOpenAngleDegrees, AngleCaptureOpenAngleDegrees, 0.0001f))
            {
                throw new InvalidOperationException("Persisted angle-capture identity differs from its receiver contract.");
            }

            DungeonPortalBakedBasisDoorAngleCapture.CaptureProvenance provenance = capture.Provenance;
            if (!string.Equals(provenance.toolVersion, AngleCaptureToolVersion, StringComparison.Ordinal) ||
                !string.Equals(provenance.unityVersion, Application.unityVersion, StringComparison.Ordinal) ||
                !string.Equals(provenance.receiverRoomId, spec.RoomId, StringComparison.Ordinal) ||
                !string.Equals(provenance.stableDoorwayId, StableDoorwayId, StringComparison.Ordinal) ||
                !string.Equals(provenance.workspaceScenePath, GetAngleWorkspacePath(spec), StringComparison.Ordinal) ||
                !string.Equals(provenance.lightingSettingsClonePath, GetAngleLightingSettingsPath(spec), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Persisted angle-capture provenance differs from its exact contract.");
            }
            ProductionInputGuard.AssertFingerprintsMatch(provenance.productionInputs, inputGuards);

            LightingSettings lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(
                GetAngleLightingSettingsPath(spec));
            if (lightingSettings == null || EditorUtility.IsDirty(lightingSettings) ||
                !string.Equals(
                    GetDependencyHash(GetAngleLightingSettingsPath(spec)),
                    provenance.lightingSettingsCloneDependencyHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Persisted angle LightingSettings clone hash differs.");
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GetAngleWorkspacePath(spec)) == null ||
                !string.Equals(
                    GetDependencyHash(GetAngleWorkspacePath(spec)),
                    provenance.canonicalWorkspaceDependencyHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Persisted canonical angle workspace hash differs.");
            }

            DungeonPortalBakedBasisDoorAngleCapture.AngleState baseline = capture.Baseline;
            ValidateAngleStateArtifacts(baseline, spec, 0, 0);
            DungeonPortalBakedBasisDoorAngleCapture.AngleState[] matchedBaselines =
                capture.MatchedBaselineAngleStates;
            DungeonPortalBakedBasisDoorAngleCapture.AngleState[] states = capture.FullAngleStates;
            if (matchedBaselines.Length != 4 || states.Length != 4)
                throw new InvalidOperationException("Persisted angle capture does not contain four matched pairs.");
            for (int i = 0; i < states.Length; i++)
            {
                ValidateAngleStateArtifacts(
                    matchedBaselines[i],
                    spec,
                    baseline.lightmaps.Length,
                    baseline.renderers.Length);
                ValidateAngleStateArtifacts(
                    states[i],
                    spec,
                    baseline.lightmaps.Length,
                    baseline.renderers.Length);
                string baselineLayoutFailure = string.Empty;
                string fullLayoutFailure = string.Empty;
                string pairFailure = string.Empty;
                if (!DungeonPortalBakedBasisDoorAngleCapture.TryValidateReceiverLayout(
                        baseline,
                        matchedBaselines[i],
                        out baselineLayoutFailure) ||
                    !DungeonPortalBakedBasisDoorAngleCapture.TryValidateReceiverLayout(
                        baseline,
                        states[i],
                        out fullLayoutFailure) ||
                    !DungeonPortalBakedBasisDoorAngleCapture.TryValidateMatchedPosePair(
                        matchedBaselines[i],
                        states[i],
                        out pairFailure))
                {
                    throw new InvalidOperationException(
                        "Persisted angle pair differs: baselineLayout=" + baselineLayoutFailure +
                        " fullLayout=" + fullLayoutFailure + " pair=" + pairFailure);
                }
            }
            ValidatePersistedProxyProvenance(capture.ProxyProvenance);
            ProductionInputGuard.AssertUnchanged(inputGuards);
        }

        private static void ValidatePersistedProxyProvenance(
            DungeonPortalBakedBasisDoorAngleCapture.DoorProxyProvenance provenance)
        {
            StaticEditorFlags expectedFlags =
                StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic;
            if (provenance.staticEditorFlags != (int)expectedFlags ||
                !IsPersistentDependencyMatch(
                    provenance.sourceMeshAssetPath,
                    provenance.sourceMeshDependencyHash))
            {
                throw new InvalidOperationException("Persisted door-proxy mesh/static provenance differs.");
            }
            string[] paths = provenance.sourceMaterialAssetPaths ?? Array.Empty<string>();
            string[] hashes = provenance.sourceMaterialDependencyHashes ?? Array.Empty<string>();
            if (paths.Length == 0 || paths.Length != hashes.Length)
                throw new InvalidOperationException("Persisted door-proxy material provenance is incomplete.");
            for (int i = 0; i < paths.Length; i++)
            {
                if (!IsPersistentDependencyMatch(paths[i], hashes[i]))
                    throw new InvalidOperationException("Persisted door-proxy material dependency changed at slot " + i + ".");
            }
        }

        private static bool IsPersistentDependencyMatch(string assetPath, string expectedHash)
        {
            return !string.IsNullOrWhiteSpace(assetPath) &&
                   !string.IsNullOrWhiteSpace(expectedHash) &&
                   AssetDatabase.LoadMainAssetAtPath(assetPath) != null &&
                   string.Equals(GetDependencyHash(assetPath), expectedHash, StringComparison.Ordinal);
        }

        private static void AssertAngleLightingSettingsHash(
            LightingSettings lightingSettings,
            string expectedHash)
        {
            string path = GetDependencyAssetPath(lightingSettings);
            if (lightingSettings == null || EditorUtility.IsDirty(lightingSettings) ||
                !IsOwnedAngleCapturePath(path) ||
                !string.Equals(GetDependencyHash(path), expectedHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Angle-capture LightingSettings clone changed during capture.");
            }
        }

        private static string GetDependencyAssetPath(UnityEngine.Object asset)
        {
            return asset != null ? AssetDatabase.GetAssetPath(asset) : string.Empty;
        }

        private static string GetAngleReceiverFolder(ReceiverSpec spec)
        {
            return AngleCaptureRoot + "/" + spec.RoomId;
        }

        private static string GetAngleCaptureAssetPath(ReceiverSpec spec)
        {
            return GetAngleReceiverFolder(spec) + "/" + spec.RoomId + "_DoorAngleCapture.asset";
        }

        private static string GetAngleWorkspaceFolder(ReceiverSpec spec)
        {
            return GetAngleReceiverFolder(spec) + "/Workspace";
        }

        private static string GetAngleWorkspacePath(ReceiverSpec spec)
        {
            return GetAngleWorkspaceFolder(spec) + "/" + spec.RoomId + "_AngleCapture.unity";
        }

        private static string GetAngleLightingFolder(ReceiverSpec spec)
        {
            return GetAngleReceiverFolder(spec) + "/Lighting";
        }

        private static string GetAngleLightingSettingsPath(ReceiverSpec spec)
        {
            return GetAngleLightingFolder(spec) + "/" + spec.RoomId + "_AngleCapture.lighting";
        }

        private static string GetAngleStateFolder(ReceiverSpec spec, string stateId)
        {
            GetRequiredAngleCaptureContract(stateId);
            return GetAngleReceiverFolder(spec) + "/States/" + stateId;
        }

        private static DoorAngleCaptureContract GetRequiredAngleCaptureContract(string stateId)
        {
            for (int i = 0; i < AngleCaptureContracts.Length; i++)
            {
                if (string.Equals(AngleCaptureContracts[i].StateId, stateId, StringComparison.Ordinal))
                    return AngleCaptureContracts[i];
            }
            throw new InvalidOperationException("Requested a non-canonical angle state: '" + stateId + "'.");
        }

        private static string GetAnglePartialMarkerPath(ReceiverSpec spec)
        {
            return GetAngleReceiverFolder(spec) + "/" + AngleCapturePartialMarkerName;
        }

        private static void EnsureAngleCaptureFolder(string folder)
        {
            AssertOwnedAngleCapturePath(folder);
            EnsureAssetFolder(folder);
            if (!AssetDatabase.IsValidFolder(folder))
                throw new InvalidOperationException("Unity did not register owned angle folder: '" + folder + "'.");
        }

        private static bool IsOwnedAngleCapturePath(string path)
        {
            return TryGetCanonicalAngleCapturePath(path, out _, out _, out _);
        }

        private static void AssertOwnedAngleCapturePath(string path)
        {
            if (!TryGetCanonicalAngleCapturePath(
                    path,
                    out string canonical,
                    out _,
                    out string failure) ||
                !string.Equals(canonical, path.Replace('\\', '/'), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Path is not canonical below the owned angle-capture root: '" + path + "'. " + failure);
            }
        }

        private static string GetOwnedAnglePhysicalPath(string path)
        {
            if (!TryGetCanonicalAngleCapturePath(
                    path,
                    out string canonical,
                    out string physical,
                    out string failure) ||
                !string.Equals(canonical, path.Replace('\\', '/'), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Owned angle path validation failed: " + failure);
            }
            return physical;
        }

        private static bool TryGetCanonicalAngleCapturePath(
            string assetPath,
            out string canonicalAssetPath,
            out string physicalPath,
            out string error)
        {
            canonicalAssetPath = string.Empty;
            physicalPath = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                error = "path is empty";
                return false;
            }
            string normalized = assetPath.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            if (segments.Length < 2 || !string.Equals(segments[0], "Assets", StringComparison.Ordinal))
            {
                error = "path is not Assets-relative";
                return false;
            }
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(segments[i]) ||
                    string.Equals(segments[i], ".", StringComparison.Ordinal) ||
                    string.Equals(segments[i], "..", StringComparison.Ordinal) ||
                    segments[i].IndexOf(':') >= 0)
                {
                    error = "path contains an empty, dot, traversal, or drive segment";
                    return false;
                }
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string assetsRoot = Path.GetFullPath(Application.dataPath);
            string ownedRoot = Path.GetFullPath(Path.Combine(
                projectRoot,
                AngleCaptureRoot.Replace('/', Path.DirectorySeparatorChar)));
            string candidate = Path.GetFullPath(Path.Combine(
                projectRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            string ownedPrefix = ownedRoot.TrimEnd(
                                     Path.DirectorySeparatorChar,
                                     Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!string.Equals(candidate, ownedRoot, StringComparison.OrdinalIgnoreCase) &&
                !candidate.StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                error = "physical path escapes the owned DPBB angle-capture root";
                return false;
            }
            string assetsPrefix = assetsRoot.TrimEnd(
                                      Path.DirectorySeparatorChar,
                                      Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                error = "canonical path is not below Assets";
                return false;
            }
            if (ContainsExistingReparsePoint(assetsRoot, candidate))
            {
                error = "an existing path segment is a reparse point";
                return false;
            }
            canonicalAssetPath = "Assets/" + candidate.Substring(assetsPrefix.Length)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            physicalPath = candidate;
            return true;
        }

        private static bool ContainsExistingReparsePoint(string assetsRoot, string candidate)
        {
            DirectoryInfo current = Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : new FileInfo(candidate).Directory;
            string root = Path.GetFullPath(assetsRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            while (current != null)
            {
                string currentPath = Path.GetFullPath(current.FullName)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!currentPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    break;
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                    return true;
                if (string.Equals(currentPath, root, StringComparison.OrdinalIgnoreCase))
                    break;
                current = current.Parent;
            }
            return false;
        }

        private static string FormatMiB(long bytes)
        {
            return (bytes / (1024d * 1024d)).ToString("F0", CultureInfo.InvariantCulture);
        }

        private sealed class AngleWorkspaceObjects
        {
            public AngleWorkspaceObjects(
                GameObject room,
                Transform doorway,
                GameObject realDoor,
                Transform doorLeaf,
                Light injector,
                DungeonPortalBakedBasisAngleWorkspaceMarker marker)
            {
                Room = room;
                Doorway = doorway;
                RealDoor = realDoor;
                DoorLeaf = doorLeaf;
                Injector = injector;
                Marker = marker;
            }

            public GameObject Room { get; }
            public Transform Doorway { get; }
            public GameObject RealDoor { get; }
            public Transform DoorLeaf { get; }
            public Light Injector { get; }
            public DungeonPortalBakedBasisAngleWorkspaceMarker Marker { get; }
        }

        private readonly struct LinearHalfImage
        {
            public LinearHalfImage(int width, int height, ushort[] raw)
            {
                if (width <= 0 || height <= 0 || raw == null || raw.Length != checked(width * height * 4))
                    throw new ArgumentException("Linear RGBAHalf image payload is invalid.", nameof(raw));
                Width = width;
                Height = height;
                Raw = raw;
            }

            public int Width { get; }
            public int Height { get; }
            public ushort[] Raw { get; }
        }

        private readonly struct EffectDifferenceMetrics
        {
            public EffectDifferenceMetrics(
                int sampledTexelCount,
                int changedTexelCount,
                float changedTexelFraction,
                float meanAbsoluteRgbDifference,
                float maximumAbsoluteRgbDifference)
            {
                SampledTexelCount = sampledTexelCount;
                ChangedTexelCount = changedTexelCount;
                ChangedTexelFraction = changedTexelFraction;
                MeanAbsoluteRgbDifference = meanAbsoluteRgbDifference;
                MaximumAbsoluteRgbDifference = maximumAbsoluteRgbDifference;
            }

            public int SampledTexelCount { get; }
            public int ChangedTexelCount { get; }
            public float ChangedTexelFraction { get; }
            public float MeanAbsoluteRgbDifference { get; }
            public float MaximumAbsoluteRgbDifference { get; }
        }

        private sealed class DoorProxySource
        {
            private DoorProxySource(
                MeshRenderer baseRenderer,
                MeshFilter baseFilter,
                Material[] materials,
                string meshPath,
                string meshGuid,
                long meshLocalId,
                string[] materialPaths,
                string[] materialHashes)
            {
                BaseRenderer = baseRenderer;
                BaseFilter = baseFilter;
                Materials = materials;
                MeshPath = meshPath;
                MeshGuid = meshGuid;
                MeshLocalId = meshLocalId;
                MaterialPaths = materialPaths;
                MaterialHashes = materialHashes;
            }

            public MeshRenderer BaseRenderer { get; }
            public MeshFilter BaseFilter { get; }
            public Material[] Materials { get; }
            public string MeshPath { get; }
            public string MeshGuid { get; }
            public long MeshLocalId { get; }
            public string[] MaterialPaths { get; }
            public string[] MaterialHashes { get; }

            public static DoorProxySource Capture(AngleWorkspaceObjects objects)
            {
                MeshRenderer baseRenderer = objects.DoorLeaf.GetComponent<MeshRenderer>();
                MeshFilter baseFilter = objects.DoorLeaf.GetComponent<MeshFilter>();
                if (baseRenderer == null || baseRenderer.enabled || baseFilter == null ||
                    baseFilter.sharedMesh == null)
                {
                    throw new InvalidOperationException("Door proxy source full-mesh contract changed.");
                }
                AssertRendererMaterialPropertyBlocksAreEmpty(baseRenderer);
                Mesh mesh = baseFilter.sharedMesh;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        mesh,
                        out string meshGuid,
                        out long meshLocalId) ||
                    string.IsNullOrWhiteSpace(meshGuid) || meshLocalId == 0L)
                {
                    throw new InvalidOperationException("Door proxy source mesh is not a persistent asset.");
                }
                string meshPath = AssetDatabase.GetAssetPath(mesh);
                if (string.IsNullOrWhiteSpace(meshPath) ||
                    AssetDatabase.LoadMainAssetAtPath(meshPath) == null)
                {
                    throw new InvalidOperationException("Door proxy source mesh has no dependency path.");
                }
                Material[] materials = baseRenderer.sharedMaterials != null
                    ? (Material[])baseRenderer.sharedMaterials.Clone()
                    : Array.Empty<Material>();
                if (materials.Length == 0)
                    throw new InvalidOperationException("Door proxy source has no material slots.");
                var paths = new string[materials.Length];
                var hashes = new string[materials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null)
                        throw new InvalidOperationException("Door proxy source material is null at slot " + i + ".");
                    paths[i] = AssetDatabase.GetAssetPath(materials[i]);
                    if (string.IsNullOrWhiteSpace(paths[i]) ||
                        AssetDatabase.LoadMainAssetAtPath(paths[i]) == null)
                    {
                        throw new InvalidOperationException("Door proxy material has no persistent path at slot " + i + ".");
                    }
                    hashes[i] = GetDependencyHash(paths[i]);
                }
                return new DoorProxySource(
                    baseRenderer,
                    baseFilter,
                    materials,
                    meshPath,
                    meshGuid,
                    meshLocalId,
                    paths,
                    hashes);
            }

            public DungeonPortalBakedBasisDoorAngleCapture.DoorProxyProvenance BuildProvenance()
            {
                StaticEditorFlags flags =
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic;
                return new DungeonPortalBakedBasisDoorAngleCapture.DoorProxyProvenance
                {
                    proxyPolicy =
                        "Transient fixed-angle copy of disabled Door_01 full mesh; ShadowsOnly; " +
                        "ContributeGI|OccluderStatic; ScaleInLightmap=0; split presentation renderers " +
                        "disabled only during Lightmapping.Bake and exactly restored before evidence capture/save.",
                    sourceMeshAssetPath = MeshPath,
                    sourceMeshGuid = MeshGuid,
                    sourceMeshLocalId = MeshLocalId,
                    sourceMeshDependencyHash = GetDependencyHash(MeshPath),
                    sourceMaterialAssetPaths = (string[])MaterialPaths.Clone(),
                    sourceMaterialDependencyHashes = (string[])MaterialHashes.Clone(),
                    shadowCastingMode = ShadowCastingMode.ShadowsOnly,
                    staticEditorFlags = (int)flags,
                    scaleInLightmap = 0f,
                    materialPropertyBlockVerifiedEmpty = true,
                    transientAndAbsentFromCapturedInventories = true
                };
            }
        }

        private sealed class DoorProxySession
        {
            private readonly AngleWorkspaceObjects objects;
            private readonly DoorAngleCaptureContract contract;
            private readonly DoorProxySource source;
            private readonly RendererPresentationState positiveState;
            private readonly RendererPresentationState negativeState;
            private readonly RendererPresentationState edgeState;
            private GameObject proxyObject;
            private MeshRenderer proxyRenderer;
            private bool restored;

            private DoorProxySession(
                AngleWorkspaceObjects objects,
                DoorAngleCaptureContract contract,
                DoorProxySource source,
                RendererPresentationState positiveState,
                RendererPresentationState negativeState,
                RendererPresentationState edgeState)
            {
                this.objects = objects;
                this.contract = contract;
                this.source = source;
                this.positiveState = positiveState;
                this.negativeState = negativeState;
                this.edgeState = edgeState;
            }

            public static DoorProxySession Begin(
                AngleWorkspaceObjects objects,
                DoorAngleCaptureContract contract)
            {
                if (objects == null || !contract.UseTransientDoorProxy)
                    throw new InvalidOperationException("Stored angle states require the transient door proxy.");
                AssertNoAngleProxy(objects.RealDoor);
                DoorProxySource source = DoorProxySource.Capture(objects);
                MeshRenderer positive = RequireUniqueNamedMeshRenderer(
                    objects.RealDoor.transform,
                    DoorPositiveRendererName);
                MeshRenderer negative = RequireUniqueNamedMeshRenderer(
                    objects.RealDoor.transform,
                    DoorNegativeRendererName);
                MeshRenderer edge = RequireUniqueNamedMeshRenderer(
                    objects.RealDoor.transform,
                    DoorEdgeRendererName);
                var session = new DoorProxySession(
                    objects,
                    contract,
                    source,
                    RendererPresentationState.Capture(positive),
                    RendererPresentationState.Capture(negative),
                    RendererPresentationState.Capture(edge));
                try
                {
                    session.CreateProxyAndDisablePresentation(positive, negative, edge);
                    session.AssertBakeState();
                    return session;
                }
                catch
                {
                    session.RestorePresentationAndDestroyProxy();
                    throw;
                }
            }

            private void CreateProxyAndDisablePresentation(
                MeshRenderer positive,
                MeshRenderer negative,
                MeshRenderer edge)
            {
                proxyObject = new GameObject(AngleCaptureProxyName + contract.StateId);
                proxyObject.layer = source.BaseRenderer.gameObject.layer;
                proxyObject.transform.SetParent(objects.DoorLeaf, false);
                proxyObject.transform.localPosition = Vector3.zero;
                proxyObject.transform.localRotation = Quaternion.identity;
                proxyObject.transform.localScale = Vector3.one;
                MeshFilter filter = proxyObject.AddComponent<MeshFilter>();
                filter.sharedMesh = source.BaseFilter.sharedMesh;
                proxyRenderer = proxyObject.AddComponent<MeshRenderer>();
                EditorUtility.CopySerialized(source.BaseRenderer, proxyRenderer);
                proxyRenderer.enabled = true;
                proxyRenderer.forceRenderingOff = false;
                proxyRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                proxyRenderer.receiveShadows = false;
                proxyRenderer.lightProbeUsage = LightProbeUsage.Off;
                proxyRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                proxyRenderer.receiveGI = ReceiveGI.Lightmaps;
                proxyRenderer.renderingLayerMask = RequiredDungeonRenderingLayerMask;
                SetScaleInLightmapExactly(proxyRenderer, 0f);
                GameObjectUtility.SetStaticEditorFlags(
                    proxyObject,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic);
                AssertRendererMaterialPropertyBlocksAreEmpty(proxyRenderer);

                positive.enabled = false;
                negative.enabled = false;
                edge.enabled = false;
                EditorUtility.SetDirty(positive);
                EditorUtility.SetDirty(negative);
                EditorUtility.SetDirty(edge);
            }

            public void AssertBakeState()
            {
                MeshRenderer positive = RequireUniqueNamedMeshRenderer(
                    objects.RealDoor.transform,
                    DoorPositiveRendererName);
                MeshRenderer negative = RequireUniqueNamedMeshRenderer(
                    objects.RealDoor.transform,
                    DoorNegativeRendererName);
                MeshRenderer edge = RequireUniqueNamedMeshRenderer(
                    objects.RealDoor.transform,
                    DoorEdgeRendererName);
                StaticEditorFlags flags =
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic;
                if (proxyObject == null || proxyRenderer == null || !proxyRenderer.enabled ||
                    proxyRenderer.forceRenderingOff ||
                    proxyRenderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly ||
                    proxyRenderer.receiveShadows ||
                    proxyRenderer.lightProbeUsage != LightProbeUsage.Off ||
                    proxyRenderer.reflectionProbeUsage != ReflectionProbeUsage.Off ||
                    proxyRenderer.renderingLayerMask != RequiredDungeonRenderingLayerMask ||
                    proxyObject.transform.parent != objects.DoorLeaf ||
                    proxyObject.transform.localPosition != Vector3.zero ||
                    proxyObject.transform.localRotation != Quaternion.identity ||
                    proxyObject.transform.localScale != Vector3.one ||
                    proxyObject.GetComponent<MeshFilter>() == null ||
                    proxyObject.GetComponent<MeshFilter>().sharedMesh != source.BaseFilter.sharedMesh ||
                    !MaterialArraysEqual(proxyRenderer.sharedMaterials, source.Materials) ||
                    GameObjectUtility.GetStaticEditorFlags(proxyObject) != flags ||
                    !Approximately(GetScaleInLightmap(proxyRenderer), 0f, 0.000001f) ||
                    source.BaseRenderer.enabled || positive.enabled || negative.enabled || edge.enabled)
                {
                    throw new InvalidOperationException("Transient ShadowsOnly GI door-proxy bake state is invalid.");
                }
                AssertRendererMaterialPropertyBlocksAreEmpty(proxyRenderer);
            }

            public void RestorePresentationAndDestroyProxy()
            {
                if (restored)
                    return;
                MeshRenderer positive = RequireUniqueNamedMeshRenderer(
                    objects.RealDoor.transform,
                    DoorPositiveRendererName);
                MeshRenderer negative = RequireUniqueNamedMeshRenderer(
                    objects.RealDoor.transform,
                    DoorNegativeRendererName);
                MeshRenderer edge = RequireUniqueNamedMeshRenderer(
                    objects.RealDoor.transform,
                    DoorEdgeRendererName);
                positiveState.Restore(positive);
                negativeState.Restore(negative);
                edgeState.Restore(edge);
                if (proxyRenderer != null)
                {
                    proxyRenderer.enabled = false;
                    proxyRenderer.forceRenderingOff = true;
                    proxyRenderer.shadowCastingMode = ShadowCastingMode.Off;
                }
                if (proxyObject != null)
                    UnityEngine.Object.DestroyImmediate(proxyObject);
                proxyObject = null;
                proxyRenderer = null;
                positiveState.AssertExact(positive);
                negativeState.AssertExact(negative);
                edgeState.AssertExact(edge);
                AssertAngleDoorPresentation(
                    objects.RealDoor,
                    objects.DoorLeaf,
                    objects.Marker,
                    contract.OpenFraction);
                AssertNoAngleProxy(objects.RealDoor);
                restored = true;
            }

            private static void SetScaleInLightmapExactly(Renderer renderer, float value)
            {
                var serialized = new SerializedObject(renderer);
                SerializedProperty property = serialized.FindProperty("m_ScaleInLightmap");
                if (property == null)
                    throw new InvalidOperationException("Proxy MeshRenderer has no Scale In Lightmap field.");
                property.floatValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (!Approximately(GetScaleInLightmap(renderer), value, 0.000001f))
                    throw new InvalidOperationException("Proxy Scale In Lightmap could not be set exactly.");
            }

            private static float GetScaleInLightmap(Renderer renderer)
            {
                var serialized = new SerializedObject(renderer);
                SerializedProperty property = serialized.FindProperty("m_ScaleInLightmap");
                if (property == null)
                    throw new InvalidOperationException("MeshRenderer has no Scale In Lightmap field.");
                return property.floatValue;
            }

            private static bool MaterialArraysEqual(Material[] left, Material[] right)
            {
                left = left ?? Array.Empty<Material>();
                right = right ?? Array.Empty<Material>();
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

        private readonly struct RendererPresentationState
        {
            private RendererPresentationState(
                bool enabled,
                bool forceRenderingOff,
                ShadowCastingMode shadowCastingMode,
                bool receiveShadows,
                uint renderingLayerMask,
                LightProbeUsage lightProbeUsage,
                ReflectionProbeUsage reflectionProbeUsage,
                StaticEditorFlags staticFlags,
                Mesh mesh,
                Material[] materials)
            {
                Enabled = enabled;
                ForceRenderingOff = forceRenderingOff;
                ShadowCastingMode = shadowCastingMode;
                ReceiveShadows = receiveShadows;
                RenderingLayerMask = renderingLayerMask;
                LightProbeUsage = lightProbeUsage;
                ReflectionProbeUsage = reflectionProbeUsage;
                StaticFlags = staticFlags;
                Mesh = mesh;
                Materials = materials;
            }

            private bool Enabled { get; }
            private bool ForceRenderingOff { get; }
            private ShadowCastingMode ShadowCastingMode { get; }
            private bool ReceiveShadows { get; }
            private uint RenderingLayerMask { get; }
            private LightProbeUsage LightProbeUsage { get; }
            private ReflectionProbeUsage ReflectionProbeUsage { get; }
            private StaticEditorFlags StaticFlags { get; }
            private Mesh Mesh { get; }
            private Material[] Materials { get; }

            public static RendererPresentationState Capture(MeshRenderer renderer)
            {
                if (renderer == null)
                    throw new ArgumentNullException(nameof(renderer));
                AssertRendererMaterialPropertyBlocksAreEmpty(renderer);
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    throw new InvalidOperationException("Split door renderer has no persistent mesh.");
                return new RendererPresentationState(
                    renderer.enabled,
                    renderer.forceRenderingOff,
                    renderer.shadowCastingMode,
                    renderer.receiveShadows,
                    renderer.renderingLayerMask,
                    renderer.lightProbeUsage,
                    renderer.reflectionProbeUsage,
                    GameObjectUtility.GetStaticEditorFlags(renderer.gameObject),
                    filter.sharedMesh,
                    renderer.sharedMaterials != null
                        ? (Material[])renderer.sharedMaterials.Clone()
                        : Array.Empty<Material>());
            }

            public void Restore(MeshRenderer renderer)
            {
                renderer.enabled = Enabled;
                renderer.forceRenderingOff = ForceRenderingOff;
                renderer.shadowCastingMode = ShadowCastingMode;
                renderer.receiveShadows = ReceiveShadows;
                renderer.renderingLayerMask = RenderingLayerMask;
                renderer.lightProbeUsage = LightProbeUsage;
                renderer.reflectionProbeUsage = ReflectionProbeUsage;
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticFlags);
                EditorUtility.SetDirty(renderer);
            }

            public void AssertExact(MeshRenderer renderer)
            {
                MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                Material[] current = renderer != null && renderer.sharedMaterials != null
                    ? renderer.sharedMaterials
                    : Array.Empty<Material>();
                bool materialsEqual = current.Length == Materials.Length;
                for (int i = 0; materialsEqual && i < current.Length; i++)
                    materialsEqual = current[i] == Materials[i];
                if (renderer == null || filter == null || filter.sharedMesh != Mesh ||
                    renderer.enabled != Enabled || renderer.forceRenderingOff != ForceRenderingOff ||
                    renderer.shadowCastingMode != ShadowCastingMode ||
                    renderer.receiveShadows != ReceiveShadows ||
                    renderer.renderingLayerMask != RenderingLayerMask ||
                    renderer.lightProbeUsage != LightProbeUsage ||
                    renderer.reflectionProbeUsage != ReflectionProbeUsage ||
                    GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) != StaticFlags ||
                    !materialsEqual)
                {
                    throw new InvalidOperationException("Split door renderer did not restore exactly.");
                }
                AssertRendererMaterialPropertyBlocksAreEmpty(renderer);
            }
        }
    }
}
