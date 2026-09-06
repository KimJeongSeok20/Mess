using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DungeonPortalTransportPoC.Editor
{
    /// <summary>
    /// An edit-mode-only, fail-closed authoring utility for capturing the receiver-side
    /// three-state doorway response. It intentionally creates only isolated PoC scenes,
    /// lighting settings and evidence assets; it never writes endpoint profiles, tile
    /// sets, dungeon flows, map lists or production prefabs.
    /// </summary>
    public static partial class DungeonPortalReceiverBounceBaker
    {
        private const string RootFolder = "Assets/Experiments/DungeonPortalTransportPoC";
        private const string GeneratedRoot = RootFolder + "/Generated/Bounce";
        private const string LightingSettingsFolder = GeneratedRoot + "/LightingSettings";
        private const string WorkspacesFolder = GeneratedRoot + "/Workspaces";

        // The spelling below is intentional: it is the production asset's established name.
        private const string OfficialLightingSettingsPath = "Assets/OfficalLightmap.lighting";
        private const string StartRoomPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/StartRoom_R000.prefab";
        private const string AdminRoomPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/AdminstrativeSegregation_R000.prefab";
        private const string DoorPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/SomePicees/Door_SM_A_Door_Placement.prefab";
        private const string DunGenDoorSourcePath = "Assets/DunGen/Code/Door.cs";
        private const string DoorLeafSourcePath =
            "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Scripts/Door.cs";
        private const string RotationBakeToolSourcePath =
            "Assets/Editor/DungeonTileRotationBakeTool.cs";
        private const string PreservedAdminLampPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Lighting_Prefabs/Lamp_05.prefab";
        private const string PreservedV2StartCeilingLampPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Lighting_Prefabs/Ceiling_Lights_DualSided.prefab";

        private const string StableDoorwayId = "Doorways[5]/Door_SM_A[0]/DoorWayPoint[0]";
        private const string DoorLeafPath = "Door_01";
        private const string DoorInstanceName = "ReceiverBounce_RealDoor_FullyOpen";
        private const string InjectorName = "ReceiverBounce_BakedSpot__PoC";
        // Keep the transient manifest name short enough for legacy Windows/.NET path
        // handling when the receiver id itself is long (AdminstrativeSegregation_R000).
        private const string StateArtifactOwnershipManifestPrefix = "__Owner_";
        private const string LegacyStateArtifactOwnershipManifestPrefix = "__ReceiverBounceStateOwnership_";
        private const int RequiredDungeonRenderingLayerMask = 2;
        private const int ExpectedCullingMask = 66177;
        private const float CanonicalSpotAngle = 131.81032f;
        private const int FixedCameraWidth = 512;
        private const int FixedCameraHeight = 512;
        private const float FixedCameraFieldOfView = 90f;
        private const string ToolVersion = "DungeonPortalReceiverBounceBaker/3";

        // This convention is deliberately recorded in provenance because it is an
        // incoming/receiver-side inference, not an endpoint profile mutation.
        private const string ReceiverIncomingPlacementInterpretation =
            "Receiver-side incoming mirror inference: doorway-local (0,1,+0.5), Euler(0,180,0) " +
            "aims through the doorway into the receiver. It is intentionally distinct from " +
            "the outgoing direct-basis convention and is not written to a production profile.";
        private const string ReflectionProbePolicy =
            "All receiver ReflectionProbes are disabled before every bake and fixed HDR capture; " +
            "the movable real-door LightProbeGroup remains enabled and is not regenerated.";

        private static readonly ReceiverSpec StartSpec = new ReceiverSpec(
            "StartRoom_R000",
            StartRoomPrefabPath,
            21.602848f,
            4,
            76);
        private static readonly ReceiverSpec AdminSpec = new ReceiverSpec(
            "AdminstrativeSegregation_R000",
            AdminRoomPrefabPath,
            57.865467f,
            // The connected/open-passage workspace packs a fresh atlas layout that is
            // not represented by the standalone BakeData atlas count. Baseline records
            // the authoritative used-atlas count; later states must match its full layout.
            0,
            0);
        private static readonly BakeStateSpec[] StateSpecs =
        {
            new BakeStateSpec(DungeonPortalReceiverResponseCapture.BaselineStateName, false, 0f),
            new BakeStateSpec(DungeonPortalReceiverResponseCapture.DirectOnlyStateName, true, 0f),
            new BakeStateSpec(DungeonPortalReceiverResponseCapture.FullStateName, true, 1f)
        };
        private static readonly Vector3[] ProbeLocalPositions = BuildProbeLocalPositions();
        private static readonly EmissionMaterialVariant[] P0EmissionVariants =
        {
            new EmissionMaterialVariant(
                "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials/Lamps_01.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_01_P100.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_01_P0_Black.mat"),
            new EmissionMaterialVariant(
                "Assets/Prefabs/map_piece/NewPrison/Power_Material/Lamps_02.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_02_P100.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_02_P0_Black.mat"),
            new EmissionMaterialVariant(
                "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials/Lamps_05.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P100.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P0_Black.mat"),
            new EmissionMaterialVariant(
                "Assets/Prefabs/map_piece/NewPrison/Power_Material/Lamps_05.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P100.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P0_Black.mat")
        };

        private static readonly string[] KnownMaterialParityProperties =
        {
            "_ZWrite",
            "_ZTest",
            "_Cull",
            "_Surface",
            "_Blend",
            "_SrcBlend",
            "_DstBlend",
            "_AlphaClip",
            "_AlphaToMask"
        };

        private static bool captureInProgress;

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Capture Receiver Bounce/StartRoom_R000")]
        public static void CaptureStartFromMenu()
        {
            LogResult(CaptureStart());
        }

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Capture Receiver Bounce/AdminstrativeSegregation_R000")]
        public static void CaptureAdminFromMenu()
        {
            LogResult(CaptureAdmin());
        }

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Capture Receiver Bounce/All Receivers")]
        public static void CaptureAllFromMenu()
        {
            LogResult(CaptureAll());
        }

        /// <summary>Unity -executeMethod entry point for the Start receiver only.</summary>
        public static void CaptureStartCli()
        {
            ThrowIfFailed(CaptureStart());
        }

        /// <summary>Unity -executeMethod entry point for the Admin receiver only.</summary>
        public static void CaptureAdminCli()
        {
            ThrowIfFailed(CaptureAdmin());
        }

        public static string CaptureStart()
        {
            return CaptureReceiver(StartSpec);
        }

        public static string CaptureAdmin()
        {
            return CaptureReceiver(AdminSpec);
        }

        public static string CaptureAll()
        {
            string start = CaptureReceiver(StartSpec);
            if (!IsPass(start))
                return start;

            string admin = CaptureReceiver(AdminSpec);
            if (!IsPass(admin))
                return admin;

            return "PASS DungeonPortalReceiverBounceBaker captured StartRoom_R000 and " +
                   "AdminstrativeSegregation_R000. Runtime fitting/profile mutation remains disabled.";
        }

        /// <summary>
        /// Editor-side persisted-evidence integrity seam for later fitters. Unlike the
        /// runtime structural validator, this re-hashes copied textures/state payloads
        /// and compares the saved workspace checkpoint. This method owns a clean-session
        /// snapshot/restore transaction because validation opens the workspace Single.
        /// </summary>
        public static bool TryValidatePersistedCaptureIntegrity(
            DungeonPortalReceiverResponseCapture capture,
            out string error)
        {
            EditorSessionSnapshot snapshot = null;
            ProductionInputGuard[] inputGuards = null;
            bool valid = false;
            string failure = string.Empty;
            try
            {
                if (!TryValidateEditorState(out string preflightFailure))
                    throw new InvalidOperationException(preflightFailure);
                snapshot = EditorSessionSnapshot.Capture();
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;

                if (capture == null)
                    throw new InvalidOperationException("Receiver-response capture is null.");
                string capturePath = AssetDatabase.GetAssetPath(capture);
                if (!IsGeneratedPath(capturePath) || EditorUtility.IsDirty(capture))
                {
                    throw new InvalidOperationException(
                        "Capture evidence must be a clean asset inside the PoC Generated/Bounce root.");
                }
                if (!capture.TryValidate(out string structuralFailure))
                    throw new InvalidOperationException("Capture structural validation failed: " + structuralFailure);
                if (!TryGetReceiverSpec(capture.ReceiverRoomId, out ReceiverSpec spec))
                    throw new InvalidOperationException("Capture receiver id is not canonical: '" + capture.ReceiverRoomId + "'.");

                DungeonPortalReceiverResponseCapture.CaptureProvenance provenance = capture.Provenance;
                inputGuards = ProductionInputGuard.CaptureFor(spec);
                ProductionInputGuard.AssertFingerprintsMatch(provenance.productionInputs, inputGuards);
                LightingSettings clone = AssetDatabase.LoadAssetAtPath<LightingSettings>(
                    provenance.lightingSettingsClonePath);
                if (clone == null || EditorUtility.IsDirty(clone))
                    throw new InvalidOperationException("PoC LightingSettings clone is missing or dirty.");
                AssertLightingSettingsCloneHash(clone, provenance.lightingSettingsCloneDependencyHash);

                int capturedRendererCount = capture.Baseline.renderers != null
                    ? capture.Baseline.renderers.Length
                    : 0;
                int capturedLightmapCount = capture.Baseline.lightmaps != null
                    ? capture.Baseline.lightmaps.Length
                    : 0;
                if (!IsUsableCheckpointState(
                        capture.Baseline,
                        StateSpecs[0],
                        spec,
                        capturedLightmapCount,
                        capturedRendererCount) ||
                    !IsUsableCheckpointState(
                        capture.DirectOnly,
                        StateSpecs[1],
                        spec,
                        capturedLightmapCount,
                        capturedRendererCount) ||
                    !IsUsableCheckpointState(
                        capture.Full,
                        StateSpecs[2],
                        spec,
                        capturedLightmapCount,
                        capturedRendererCount))
                {
                    throw new InvalidOperationException(
                        "Capture state hash, copied texture dependency, full renderer inventory, or fixed HDR integrity validation failed.");
                }
                ValidateCanonicalWorkspaceCheckpoint(spec, clone, provenance);
                ProductionInputGuard.AssertUnchanged(inputGuards);
                valid = true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
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

                    if (inputGuards != null)
                        ProductionInputGuard.AssertUnchanged(inputGuards);
                }
                catch (Exception restoreFailure)
                {
                    valid = false;
                    failure = "Persisted capture validation could not restore the editor session or " +
                              "prove production inputs unchanged: " + restoreFailure.Message +
                              (string.IsNullOrEmpty(failure) ? string.Empty : " Previous failure: " + failure);
                }
            }

            error = valid ? string.Empty : failure;
            return valid;
        }

        private static string CaptureReceiver(ReceiverSpec spec)
        {
            if (!TryValidateEditorState(out string preflightFailure))
                return "FAIL: " + preflightFailure;

            if (captureInProgress)
                return "FAIL: another receiver bounce capture is already in progress.";

            captureInProgress = true;
            EditorSessionSnapshot snapshot = null;
            ProductionInputGuard[] inputGuards = null;
            FirstRunArtifactTransaction firstRunTransaction = null;
            StateArtifactTransaction activeStateArtifact = null;
            string result = null;
            try
            {
                snapshot = EditorSessionSnapshot.Capture();
                // Never permit an automatic bake while a workspace is opened/restored.
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;

                inputGuards = ProductionInputGuard.CaptureFor(spec);
                EnsureAssetFolder(GeneratedRoot);
                EnsureAssetFolder(GetReceiverFolder(spec));
                AssertNoOrphanedStateArtifacts(spec);

                string capturePath = GetCaptureAssetPath(spec);
                DungeonPortalReceiverResponseCapture capture =
                    AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverResponseCapture>(capturePath);
                LightingSettings clonedLightingSettings;
                DungeonPortalReceiverResponseCapture.CaptureProvenance provenance;

                if (capture != null)
                {
                    if (EditorUtility.IsDirty(capture))
                    {
                        throw new InvalidOperationException(
                            $"Existing PoC checkpoint is dirty: '{capturePath}'. Save or discard that " +
                            "generated checkpoint before resuming; it will not be overwritten.");
                    }

                    clonedLightingSettings = LoadAndValidateResumeLightingClone(spec, capture, inputGuards);
                    provenance = capture.Provenance;
                    ValidateExistingCheckpoint(capture, spec, provenance, inputGuards, clonedLightingSettings);
                }
                else
                {
                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GetWorkspacePath(spec)) != null)
                    {
                        throw new InvalidOperationException(
                            "Found an orphaned generated workspace without its checkpoint asset: '" +
                            GetWorkspacePath(spec) + "'. It is left untouched; remove or archive it manually " +
                            "before starting a new capture.");
                    }

                    firstRunTransaction = FirstRunArtifactTransaction.Begin(spec);
                    clonedLightingSettings = CreateOrRefreshPoCLightingSettingsClone(spec);
                    // A workspace is a complete, independently inspectable PoC artifact. Build and
                    // validate it before creating the resumable checkpoint, so a failed first build
                    // cannot strand a checkpoint that refers to a nonexistent workspace.
                    EnsureWorkspace(spec, clonedLightingSettings);
                    WorkspaceCheckpointFingerprint checkpoint =
                        RestoreAndCaptureCanonicalWorkspaceCheckpoint(spec, clonedLightingSettings);
                    provenance = BuildProvenance(spec, clonedLightingSettings, inputGuards, checkpoint);
                    capture = ScriptableObject.CreateInstance<DungeonPortalReceiverResponseCapture>();
                    capture.name = spec.RoomId + "_ReceiverResponseCapture";
                    AssetDatabase.CreateAsset(capture, capturePath);
                    ConfigureCheckpoint(capture, spec, provenance, default, default, default);
                    EditorUtility.SetDirty(capture);
                    AssetDatabase.SaveAssetIfDirty(capture);
                    if (EditorUtility.IsDirty(capture) ||
                        !string.Equals(capture.ReceiverRoomId, spec.RoomId, StringComparison.Ordinal) ||
                        !string.Equals(capture.StableDoorwayId, StableDoorwayId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "The initial receiver-response checkpoint did not persist its canonical identity.");
                    }
                    firstRunTransaction.Commit();
                }

                EnsureWorkspace(spec, clonedLightingSettings);
                ValidateCanonicalWorkspaceCheckpoint(spec, clonedLightingSettings, provenance);

                DungeonPortalReceiverResponseCapture.CaptureState baseline = capture.Baseline;
                DungeonPortalReceiverResponseCapture.CaptureState directOnly = capture.DirectOnly;
                DungeonPortalReceiverResponseCapture.CaptureState full = capture.Full;

                for (int i = 0; i < StateSpecs.Length; i++)
                {
                    BakeStateSpec stateSpec = StateSpecs[i];
                    DungeonPortalReceiverResponseCapture.CaptureState current = GetState(
                        stateSpec.Name,
                        baseline,
                        directOnly,
                        full);
                    int expectedRendererCount = ResolveExpectedRendererCountForState(
                        spec,
                        stateSpec,
                        baseline,
                        current);
                    int expectedLightmapCount = ResolveExpectedLightmapCountForState(
                        spec,
                        stateSpec,
                        baseline,
                        current);

                    bool isBaselineState = string.Equals(
                        stateSpec.Name,
                        DungeonPortalReceiverResponseCapture.BaselineStateName,
                        StringComparison.Ordinal);
                    bool isLayoutCompatible = isBaselineState ||
                        (baseline.captured &&
                         DungeonPortalReceiverResponseCapture.TryValidateCompatibleStateLayouts(
                             baseline,
                             current,
                             out _));
                    if (IsUsableCheckpointState(
                            current,
                            stateSpec,
                            spec,
                            expectedLightmapCount,
                            expectedRendererCount) &&
                        isLayoutCompatible)
                        continue;

                    ValidateCanonicalWorkspaceCheckpoint(spec, clonedLightingSettings, provenance);
                    current = BakeAndCaptureState(
                        spec,
                        clonedLightingSettings,
                        stateSpec,
                        provenance,
                        expectedLightmapCount,
                        expectedRendererCount,
                        out activeStateArtifact);
                    if (!isBaselineState &&
                        !DungeonPortalReceiverResponseCapture.TryValidateCompatibleStateLayouts(
                            baseline,
                            current,
                            out string layoutFailure))
                    {
                        throw new InvalidOperationException(
                            "Newly captured state '" + stateSpec.Name +
                            "' is not layout-compatible with Baseline: " + layoutFailure);
                    }
                    SetState(ref baseline, ref directOnly, ref full, current);
                    ProductionInputGuard.AssertUnchanged(inputGuards);
                    // Texture/EXR imports performed by the state capture can invalidate
                    // an earlier ScriptableObject wrapper. Never retain that Unity object
                    // reference across an AssetDatabase import boundary.
                    capture = ReloadCheckpointAsset(capturePath, spec);
                    ConfigureCheckpoint(capture, spec, provenance, baseline, directOnly, full);
                    EditorUtility.SetDirty(capture);
                    AssetDatabase.SaveAssetIfDirty(capture);
                    activeStateArtifact.Commit();
                    activeStateArtifact = null;
                }

                ProductionInputGuard.AssertUnchanged(inputGuards);
                AssertLightingSettingsCloneHash(clonedLightingSettings, provenance.lightingSettingsCloneDependencyHash);
                ValidateCanonicalWorkspaceCheckpoint(spec, clonedLightingSettings, provenance);
                capture = ReloadCheckpointAsset(capturePath, spec);
                ConfigureCheckpoint(capture, spec, provenance, baseline, directOnly, full);
                if (!capture.TryValidate(out string validationFailure))
                {
                    throw new InvalidOperationException(
                        "Capture evidence failed strict validation: " + validationFailure +
                        " Existing generated checkpoints were preserved for inspection.");
                }

                EditorUtility.SetDirty(capture);
                AssetDatabase.SaveAssetIfDirty(capture);
                if (!TryValidatePersistedCaptureIntegrity(capture, out string persistedValidationFailure))
                {
                    throw new InvalidOperationException(
                        "Capture evidence failed editor-side persisted integrity validation: " +
                        persistedValidationFailure + " Existing generated checkpoints were preserved for inspection.");
                }
                result = "PASS DungeonPortalReceiverBounceBaker\n" +
                         "receiver=" + spec.RoomId + "\n" +
                         "checkpoint=" + GetCaptureAssetPath(spec) + "\n" +
                         "workspace=" + GetWorkspacePath(spec) + "\n" +
                         "states=Baseline(disabled),DirectOnly(bounce=0),Full(bounce=1)\n" +
                         "receiverPower=P0\n" +
                         "injector=" + ReceiverIncomingPlacementInterpretation + "\n" +
                         "runtimeProfileMutation=none (intentional; HDR/SH/lightmap evidence awaits a separate fit gate)";
            }
            catch (Exception exception)
            {
                string stateRollbackFailure = activeStateArtifact != null
                    ? activeStateArtifact.TryRollbackUncommitted()
                    : string.Empty;
                string rollbackFailure = firstRunTransaction != null
                    ? firstRunTransaction.TryRollbackUncommitted()
                    : string.Empty;
                result = "FAIL: receiver bounce capture for '" + spec.RoomId + "' threw " + exception +
                         (string.IsNullOrEmpty(stateRollbackFailure)
                             ? string.Empty
                             : "\nPartial-state PoC artifact rollback also failed; its ownership manifest blocks resume: " +
                               stateRollbackFailure) +
                         (string.IsNullOrEmpty(rollbackFailure)
                             ? string.Empty
                             : "\nFirst-run PoC artifact rollback also failed: " + rollbackFailure);
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

                    if (inputGuards != null)
                        ProductionInputGuard.AssertUnchanged(inputGuards);
                }
                catch (Exception restoreFailure)
                {
                    result = "FAIL: workspace restoration or production-input verification failed: " +
                             restoreFailure + "\nPrevious result: " + result;
                }
                finally
                {
                    captureInProgress = false;
                }
            }

            return result ?? "FAIL: receiver bounce capture ended without a result.";
        }

        private static bool TryValidateEditorState(out string failure)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
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
                failure = "another lightmapping bake is already running; no scene or asset was touched.";
                return false;
            }

            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                failure = "a Prefab Stage is open; close it before this isolated Scene-mode capture.";
                return false;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                if (string.IsNullOrWhiteSpace(scene.path))
                {
                    failure = "a loaded scene is untitled: '" + scene.name + "'. Save it before capture.";
                    return false;
                }

                if (scene.isDirty)
                {
                    failure = "a loaded scene is dirty: '" + scene.name + "' (" + scene.path + "). " +
                              "Save or discard it before capture.";
                    return false;
                }
            }

            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
            {
                failure = "the editor GPU does not support the required HDR ARGBHalf/RGBAHalf capture path.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static LightingSettings LoadAndValidateResumeLightingClone(
            ReceiverSpec spec,
            DungeonPortalReceiverResponseCapture capture,
            ProductionInputGuard[] inputGuards)
        {
            DungeonPortalReceiverResponseCapture.CaptureProvenance provenance = capture.Provenance;
            string expectedPath = GetLightingSettingsClonePath(spec);
            if (!string.Equals(provenance.lightingSettingsClonePath, expectedPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Existing checkpoint uses a different lighting-settings clone path. It will not be " +
                    "rewritten by this tool.");
            }

            LightingSettings clone = AssetDatabase.LoadAssetAtPath<LightingSettings>(expectedPath);
            if (clone == null)
                throw new InvalidOperationException("Existing PoC lighting-settings clone is missing: '" + expectedPath + "'.");
            if (EditorUtility.IsDirty(clone))
            {
                throw new InvalidOperationException(
                    "Existing PoC lighting-settings clone is dirty. Save or discard it before resuming: '" +
                    expectedPath + "'.");
            }

            string cloneHash = GetDependencyHash(expectedPath);
            if (!string.Equals(cloneHash, provenance.lightingSettingsCloneDependencyHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Existing PoC lighting-settings clone hash changed. The checkpoint is preserved and " +
                    "will not be mixed with a different lighting configuration.");
            }

            ProductionInputGuard.AssertUnchanged(inputGuards);
            return clone;
        }

        private static LightingSettings CreateOrRefreshPoCLightingSettingsClone(ReceiverSpec spec)
        {
            LightingSettings source = AssetDatabase.LoadAssetAtPath<LightingSettings>(OfficialLightingSettingsPath);
            if (source == null)
                throw new InvalidOperationException("Required production lighting settings are missing: '" + OfficialLightingSettingsPath + "'.");
            if (EditorUtility.IsDirty(source))
            {
                throw new InvalidOperationException(
                    "Production lighting settings are dirty: '" + OfficialLightingSettingsPath + "'. " +
                    "The capture refuses to snapshot a mutable input.");
            }

            EnsureAssetFolder(LightingSettingsFolder);
            string clonePath = GetLightingSettingsClonePath(spec);
            LightingSettings clone = AssetDatabase.LoadAssetAtPath<LightingSettings>(clonePath);
            if (clone != null)
            {
                throw new InvalidOperationException(
                    "Found an orphaned PoC lighting-settings clone without a checkpoint. It is left " +
                    "untouched to preserve failure atomicity: '" + clonePath + "'.");
            }

            if (!AssetDatabase.CopyAsset(OfficialLightingSettingsPath, clonePath))
            {
                throw new InvalidOperationException(
                    "Unable to create the PoC-owned LightingSettings clone at '" + clonePath + "'.");
            }

            AssetDatabase.ImportAsset(clonePath, ImportAssetOptions.ForceSynchronousImport);
            clone = AssetDatabase.LoadAssetAtPath<LightingSettings>(clonePath);
            if (clone == null)
                throw new InvalidOperationException("The new PoC LightingSettings clone could not be loaded: '" + clonePath + "'.");

            return clone;
        }

        private static DungeonPortalReceiverResponseCapture.CaptureProvenance BuildProvenance(
            ReceiverSpec spec,
            LightingSettings clonedLightingSettings,
            ProductionInputGuard[] inputGuards,
            WorkspaceCheckpointFingerprint workspaceCheckpoint)
        {
            string clonePath = GetLightingSettingsClonePath(spec);
            if (clonedLightingSettings == null ||
                !string.Equals(AssetDatabase.GetAssetPath(clonedLightingSettings), clonePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The selected lighting settings are not the expected PoC clone.");
            }

            return new DungeonPortalReceiverResponseCapture.CaptureProvenance
            {
                toolVersion = ToolVersion,
                unityVersion = Application.unityVersion,
                stableDoorwayPath = StableDoorwayId,
                receiverPowerState = "P0",
                reflectionProbePolicy = ReflectionProbePolicy,
                injectorPlacementInterpretation = ReceiverIncomingPlacementInterpretation,
                workspaceScenePath = GetWorkspacePath(spec),
                canonicalWorkspaceDependencyHash = workspaceCheckpoint.DependencyHash,
                canonicalWorkspaceSetupSignature = workspaceCheckpoint.SetupSignature,
                canonicalFullRendererParitySignature = workspaceCheckpoint.FullRendererSignature,
                canonicalFullRendererCount = workspaceCheckpoint.FullRendererCount,
                canonicalFullRendererComponentCount = workspaceCheckpoint.FullRendererComponentCount,
                canonicalMaterialPropertyBlocksVerifiedEmpty = workspaceCheckpoint.MaterialPropertyBlocksVerifiedEmpty,
                lightingSettingsClonePath = clonePath,
                lightingSettingsCloneDependencyHash = GetDependencyHash(clonePath),
                capturedUtcIso8601 = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                productionInputs = ProductionInputGuard.ToFingerprints(inputGuards)
            };
        }

        private static void ValidateExistingCheckpoint(
            DungeonPortalReceiverResponseCapture capture,
            ReceiverSpec spec,
            DungeonPortalReceiverResponseCapture.CaptureProvenance provenance,
            ProductionInputGuard[] inputGuards,
            LightingSettings lightingSettingsClone)
        {
            if (capture.SchemaVersion != DungeonPortalReceiverResponseCapture.CurrentSchemaVersion ||
                !string.Equals(capture.ReceiverRoomId, spec.RoomId, StringComparison.Ordinal) ||
                !string.Equals(capture.StableDoorwayId, StableDoorwayId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Existing generated checkpoint has a different schema, receiver, or doorway identity. " +
                    "It will not be overwritten.");
            }

            if (!string.Equals(provenance.receiverPowerState, "P0", StringComparison.Ordinal) ||
                !string.Equals(provenance.toolVersion, ToolVersion, StringComparison.Ordinal) ||
                !string.Equals(provenance.unityVersion, Application.unityVersion, StringComparison.Ordinal) ||
                !string.Equals(provenance.stableDoorwayPath, StableDoorwayId, StringComparison.Ordinal) ||
                !string.Equals(provenance.workspaceScenePath, GetWorkspacePath(spec), StringComparison.Ordinal) ||
                !string.Equals(provenance.reflectionProbePolicy, ReflectionProbePolicy, StringComparison.Ordinal) ||
                !string.Equals(provenance.injectorPlacementInterpretation, ReceiverIncomingPlacementInterpretation, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(provenance.canonicalWorkspaceDependencyHash) ||
                string.IsNullOrWhiteSpace(provenance.canonicalWorkspaceSetupSignature) ||
                string.IsNullOrWhiteSpace(provenance.canonicalFullRendererParitySignature) ||
                provenance.canonicalFullRendererCount <= 0 ||
                provenance.canonicalFullRendererComponentCount != provenance.canonicalFullRendererCount ||
                !provenance.canonicalMaterialPropertyBlocksVerifiedEmpty ||
                !string.Equals(AssetDatabase.GetAssetPath(lightingSettingsClone), provenance.lightingSettingsClonePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Existing generated checkpoint provenance does not match the canonical receiver capture contract. " +
                    "It will not be overwritten.");
            }

            ProductionInputGuard.AssertFingerprintsMatch(provenance.productionInputs, inputGuards);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GetWorkspacePath(spec)) == null)
            {
                throw new InvalidOperationException(
                    "The generated workspace referenced by the existing checkpoint is missing: '" +
                    GetWorkspacePath(spec) + "'.");
            }
        }

        private static void ConfigureCheckpoint(
            DungeonPortalReceiverResponseCapture checkpoint,
            ReceiverSpec spec,
            DungeonPortalReceiverResponseCapture.CaptureProvenance provenance,
            DungeonPortalReceiverResponseCapture.CaptureState baseline,
            DungeonPortalReceiverResponseCapture.CaptureState directOnly,
            DungeonPortalReceiverResponseCapture.CaptureState full)
        {
            checkpoint.ConfigureAuthoring(
                spec.RoomId,
                StableDoorwayId,
                provenance,
                baseline,
                directOnly,
                full);
        }

        private static DungeonPortalReceiverResponseCapture ReloadCheckpointAsset(
            string capturePath,
            ReceiverSpec spec)
        {
            DungeonPortalReceiverResponseCapture checkpoint =
                AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverResponseCapture>(capturePath);
            if (checkpoint == null || EditorUtility.IsDirty(checkpoint) ||
                checkpoint.SchemaVersion != DungeonPortalReceiverResponseCapture.CurrentSchemaVersion ||
                !string.Equals(checkpoint.ReceiverRoomId, spec.RoomId, StringComparison.Ordinal) ||
                !string.Equals(checkpoint.StableDoorwayId, StableDoorwayId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The persisted receiver-response checkpoint could not be reloaded cleanly after an " +
                    "AssetDatabase import boundary: '" + capturePath + "'.");
            }

            return checkpoint;
        }

        private static void EnsureWorkspace(ReceiverSpec spec, LightingSettings clonedLightingSettings)
        {
            string workspacePath = GetWorkspacePath(spec);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(workspacePath) != null)
                return;

            EnsureAssetFolder(WorkspacesFolder);
            Scene workspace = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssertSoleWorkspaceScene(workspace, workspacePath, "workspace construction");
            EditorSceneManager.SetActiveScene(workspace);
            Lightmapping.lightingSettings = clonedLightingSettings;
            ConfigureFlatBlackEnvironment();
            ClearWorkspaceBakeData(workspace, workspacePath);

            GameObject roomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.RoomPrefabPath);
            GameObject doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath);
            if (roomPrefab == null || doorPrefab == null)
            {
                throw new InvalidOperationException(
                    "Required production prefabs are missing. room=" + (roomPrefab != null) +
                    " door=" + (doorPrefab != null));
            }

            GameObject room = PrefabUtility.InstantiatePrefab(roomPrefab, workspace) as GameObject;
            if (room == null)
                throw new InvalidOperationException("Unity could not instantiate the receiver production prefab.");

            room.name = GetRoomInstanceName(spec);
            room.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            room.transform.localScale = Vector3.one;
            Doorway doorway = ResolveExactStableDoorway(room, spec.RoomId);
            ConfigureDoorwayVisualsForRealDoor(doorway.transform);
            ApplyReceiverP0BakeState(room, spec);

            GameObject realDoor = InstantiateDoorUsingDunGenPlacement(doorPrefab, workspace, doorway);
            Transform doorLeaf = ConfigureDoorFullyOpen(realDoor, doorway);
            MarkReceiverContributeGiWithBakeExclusions(room, realDoor.transform);
            AssertDoorRemainsMovableAndProbeBacked(realDoor, doorLeaf);
            ApplyDungeonRenderingLayerPolicy(room);
            ApplyDungeonRenderingLayerPolicy(realDoor);
            int actualCullingMask = CalculateActualGameObjectLayerUnion(room, realDoor.transform, doorLeaf);
            Renderer[] receiverRenderers = GetReceiverRenderers(room, realDoor.transform);
            Renderer[] doorRenderers = GetVisibleRenderers(doorLeaf.gameObject);
            float range = ComputeRange(
                doorway.transform.TransformPoint(new Vector3(0f, 1f, 0.5f)),
                receiverRenderers,
                doorRenderers);
            if (actualCullingMask != ExpectedCullingMask ||
                !Approximately(range, spec.ExpectedInjectorRange, 0.0005f))
            {
                throw new InvalidOperationException(
                    "Initial receiver+door light contract drifted. culling=" + actualCullingMask +
                    " range=" + F(range));
            }
            CreateCanonicalInjector(doorway.transform, actualCullingMask, range);
            AssertWorkspaceTopology(workspace, spec, room, doorway.transform, realDoor, doorLeaf);

            EditorSceneManager.MarkSceneDirty(workspace);
            if (!EditorSceneManager.SaveScene(workspace, workspacePath, false))
                throw new InvalidOperationException("Unable to save isolated workspace '" + workspacePath + "'.");
        }

        /// <summary>
        /// Normalizes the persisted workspace to a saved Baseline pre-bake state after
        /// construction and after every attempted state capture. That makes the
        /// dependency hash useful on resume instead of allowing post-bake scene data to
        /// become an ambiguous hidden input.
        /// </summary>
        private static WorkspaceCheckpointFingerprint RestoreAndCaptureCanonicalWorkspaceCheckpoint(
            ReceiverSpec spec,
            LightingSettings clonedLightingSettings)
        {
            if (Lightmapping.isRunning)
                throw new InvalidOperationException("Cannot restore a canonical workspace while lightmapping is running.");

            string workspacePath = GetWorkspacePath(spec);
            Scene workspace = EditorSceneManager.OpenScene(workspacePath, OpenSceneMode.Single);
            AssertSoleWorkspaceScene(workspace, workspacePath, "canonical pre-bake workspace restore");
            EditorSceneManager.SetActiveScene(workspace);
            Lightmapping.lightingSettings = clonedLightingSettings;
            ConfigureFlatBlackEnvironment();

            WorkspaceObjects objects = ResolveWorkspaceObjects(workspace, spec);
            ApplyReceiverP0BakeState(objects.Room, spec, objects.RealDoor.transform);
            DisableReceiverReflectionProbes(objects.Room, objects.RealDoor.transform);
            ApplyDungeonRenderingLayerPolicy(objects.Room);
            ApplyDungeonRenderingLayerPolicy(objects.RealDoor);
            AssertDoorRemainsMovableAndProbeBacked(objects.RealDoor, objects.DoorLeaf);

            Renderer[] receiverRenderers = GetReceiverRenderers(objects.Room, objects.RealDoor.transform);
            Renderer[] doorRenderers = GetVisibleRenderers(objects.DoorLeaf.gameObject);
            int cullingMask = CalculateActualGameObjectLayerUnion(receiverRenderers, doorRenderers);
            if (cullingMask != ExpectedCullingMask)
                throw new InvalidOperationException("Canonical pre-bake workspace has a non-canonical culling union.");
            float range = ComputeRange(objects.Injector.transform.position, receiverRenderers, doorRenderers);
            if (!Approximately(range, spec.ExpectedInjectorRange, 0.0005f))
            {
                throw new InvalidOperationException(
                    "Canonical pre-bake workspace injector range drifted. receiver=" + spec.RoomId +
                    " expected=" + F(spec.ExpectedInjectorRange) + " actual=" + F(range));
            }

            ConfigureCanonicalInjector(objects.Injector, StateSpecs[0], cullingMask, range);
            ClearWorkspaceBakeData(workspace, workspacePath);
            Lightmapping.lightingSettings = clonedLightingSettings;
            ConfigureCanonicalInjector(objects.Injector, StateSpecs[0], cullingMask, range);
            EditorSceneManager.MarkSceneDirty(workspace);
            if (!EditorSceneManager.SaveScene(workspace))
                throw new InvalidOperationException("Unable to save canonical pre-bake workspace: '" + workspacePath + "'.");

            AssertLightingSettingsCloneHash(
                clonedLightingSettings,
                GetDependencyHash(GetLightingSettingsClonePath(spec)));
            return CaptureCurrentCanonicalWorkspaceCheckpoint(spec, clonedLightingSettings, true);
        }

        /// <summary>
        /// This executes before every resume and every new bake, including when no
        /// Baseline evidence exists. It intentionally performs no normalization before
        /// comparing the saved scene/dependency/full-renderer signatures.
        /// </summary>
        private static void ValidateCanonicalWorkspaceCheckpoint(
            ReceiverSpec spec,
            LightingSettings clonedLightingSettings,
            DungeonPortalReceiverResponseCapture.CaptureProvenance provenance)
        {
            string workspacePath = GetWorkspacePath(spec);
            Scene workspace = EditorSceneManager.OpenScene(workspacePath, OpenSceneMode.Single);
            AssertSoleWorkspaceScene(workspace, workspacePath, "canonical workspace checkpoint validation");
            if (workspace.isDirty)
            {
                throw new InvalidOperationException(
                    "The saved PoC workspace is dirty and cannot be compared to its canonical checkpoint: '" +
                    workspacePath + "'.");
            }

            AssertLightingSettingsCloneHash(clonedLightingSettings, provenance.lightingSettingsCloneDependencyHash);
            // This is editor-global state, not a workspace scene mutation. The persisted
            // workspace hash/full inventory are compared before any receiver/P0 repair.
            if (Lightmapping.lightingSettings != clonedLightingSettings)
                Lightmapping.lightingSettings = clonedLightingSettings;
            WorkspaceCheckpointFingerprint current = CaptureCurrentCanonicalWorkspaceCheckpoint(
                spec,
                clonedLightingSettings,
                false);
            if (!string.Equals(current.DependencyHash, provenance.canonicalWorkspaceDependencyHash, StringComparison.Ordinal) ||
                !string.Equals(current.SetupSignature, provenance.canonicalWorkspaceSetupSignature, StringComparison.Ordinal) ||
                !string.Equals(current.FullRendererSignature, provenance.canonicalFullRendererParitySignature, StringComparison.Ordinal) ||
                current.FullRendererCount != provenance.canonicalFullRendererCount ||
                current.FullRendererComponentCount != provenance.canonicalFullRendererComponentCount ||
                current.MaterialPropertyBlocksVerifiedEmpty != provenance.canonicalMaterialPropertyBlocksVerifiedEmpty)
            {
                throw new InvalidOperationException(
                    "The saved PoC workspace no longer matches its pre-bake canonical dependency/setup/full-renderer " +
                    "checkpoint. It is preserved and will not be resumed over a modified workspace.");
            }
        }

        private static WorkspaceCheckpointFingerprint CaptureCurrentCanonicalWorkspaceCheckpoint(
            ReceiverSpec spec,
            LightingSettings clonedLightingSettings,
            bool expectSavedCleanWorkspace)
        {
            string workspacePath = GetWorkspacePath(spec);
            Scene workspace = SceneManager.GetSceneByPath(workspacePath);
            AssertSoleWorkspaceScene(workspace, workspacePath, "canonical workspace fingerprint");
            if (expectSavedCleanWorkspace && workspace.isDirty)
                throw new InvalidOperationException("Canonical workspace was expected to be saved clean before fingerprinting.");
            if (Lightmapping.lightingSettings != clonedLightingSettings)
                throw new InvalidOperationException("Canonical workspace is not using its PoC-owned LightingSettings clone.");
            if (Lightmapping.lightingDataAsset != null ||
                (LightmapSettings.lightmaps != null && LightmapSettings.lightmaps.Length != 0))
            {
                throw new InvalidOperationException("Canonical workspace unexpectedly still carries baked lighting data.");
            }

            WorkspaceObjects objects = ResolveWorkspaceObjects(workspace, spec);
            AssertDoorRemainsMovableAndProbeBacked(objects.RealDoor, objects.DoorLeaf);
            AssertCanonicalPreBakeInjector(objects, spec);
            FullRendererInventory fullInventory = CaptureFullRendererInventory(
                objects.Room,
                objects.RealDoor,
                objects.Doorway);
            string setupSignature = ComputeCanonicalWorkspaceSetupSignature(spec, objects, fullInventory);
            return new WorkspaceCheckpointFingerprint(
                GetDependencyHash(workspacePath),
                setupSignature,
                fullInventory.Signature,
                fullInventory.Count,
                fullInventory.Count,
                true);
        }

        private static void AssertCanonicalPreBakeInjector(WorkspaceObjects objects, ReceiverSpec spec)
        {
            Renderer[] receiverRenderers = GetReceiverRenderers(objects.Room, objects.RealDoor.transform);
            Renderer[] doorRenderers = GetVisibleRenderers(objects.DoorLeaf.gameObject);
            int cullingMask = CalculateActualGameObjectLayerUnion(receiverRenderers, doorRenderers);
            float range = ComputeRange(objects.Injector.transform.position, receiverRenderers, doorRenderers);
            DungeonPortalReceiverResponseCapture.InjectorProvenance injector = BuildInjectorProvenance(objects.Injector);
            if (cullingMask != ExpectedCullingMask ||
                !Approximately(range, spec.ExpectedInjectorRange, 0.0005f) ||
                injector.enabled || injector.type != LightType.Spot ||
                injector.lightmapBakeType != LightmapBakeType.Baked ||
                !Approximately(injector.color, Color.white) ||
                !Approximately(injector.intensity, 1f, 0.00001f) ||
                !Approximately(injector.bounceIntensity, 0f, 0.00001f) ||
                !Approximately(injector.localPosition, new Vector3(0f, 1f, 0.5f), 0.00001f) ||
                !Approximately(NormalizeEulerAngles(injector.localEulerAngles), new Vector3(0f, 180f, 0f), 0.00001f) ||
                !Approximately(injector.range, spec.ExpectedInjectorRange, 0.0005f) ||
                !Approximately(injector.spotAngle, CanonicalSpotAngle, 0.0001f) ||
                !Approximately(injector.innerSpotAngle, CanonicalSpotAngle, 0.0001f) ||
                injector.shadows != LightShadows.Soft ||
                !Approximately(injector.shadowStrength, 1f, 0.00001f) ||
                injector.cullingMask != ExpectedCullingMask ||
                injector.renderingLayerMask != RequiredDungeonRenderingLayerMask ||
                injector.shadowRenderingLayerMask != RequiredDungeonRenderingLayerMask)
            {
                throw new InvalidOperationException("Canonical pre-bake injector contract drifted for " + spec.RoomId + ".");
            }
        }

        private static string ComputeCanonicalWorkspaceSetupSignature(
            ReceiverSpec spec,
            WorkspaceObjects objects,
            FullRendererInventory fullInventory)
        {
            var builder = new StringBuilder(1024);
            AppendString(builder, spec.RoomId);
            AppendString(builder, spec.RoomPrefabPath);
            AppendString(builder, DoorPrefabPath);
            AppendString(builder, StableDoorwayId);
            AppendString(builder, GetRoomInstanceName(spec));
            AppendString(builder, DoorInstanceName);
            AppendString(builder, InjectorName);
            AppendInjector(builder, BuildInjectorProvenance(objects.Injector));
            AppendString(builder, ComputeDoorLightProbeSignature(objects.RealDoor));
            AppendString(builder, fullInventory.Signature);
            builder.Append(fullInventory.Count).Append('|');
            return ComputeSha256(builder.ToString());
        }

        private static void AssertLightingSettingsCloneHash(
            LightingSettings clonedLightingSettings,
            string expectedDependencyHash)
        {
            if (clonedLightingSettings == null)
                throw new ArgumentNullException(nameof(clonedLightingSettings));
            string clonePath = AssetDatabase.GetAssetPath(clonedLightingSettings);
            if (EditorUtility.IsDirty(clonedLightingSettings) ||
                string.IsNullOrWhiteSpace(expectedDependencyHash) ||
                string.IsNullOrWhiteSpace(clonePath) ||
                !string.Equals(GetDependencyHash(clonePath), expectedDependencyHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The PoC-owned LightingSettings clone dependency hash changed; capture is fail-closed. path='" +
                    clonePath + "'.");
            }
        }

        private static DungeonPortalReceiverResponseCapture.CaptureState BakeAndCaptureState(
            ReceiverSpec spec,
            LightingSettings clonedLightingSettings,
            BakeStateSpec stateSpec,
            DungeonPortalReceiverResponseCapture.CaptureProvenance provenance,
            int expectedLightmapCount,
            int expectedRendererCount,
            out StateArtifactTransaction stateArtifact)
        {
            stateArtifact = null;
            DungeonPortalReceiverResponseCapture.CaptureState capturedState = default;
            Exception failure = null;
            try
            {
                if (Lightmapping.isRunning)
                    throw new InvalidOperationException("A lightmapping bake became active before the receiver state began.");

                AssertLightingSettingsCloneHash(clonedLightingSettings, provenance.lightingSettingsCloneDependencyHash);
                string lightingHashBeforeBake = provenance.lightingSettingsCloneDependencyHash;
                string workspacePath = GetWorkspacePath(spec);
                Scene workspace = EditorSceneManager.OpenScene(workspacePath, OpenSceneMode.Single);
                AssertSoleWorkspaceScene(workspace, workspacePath, "state '" + stateSpec.Name + "'");
                EditorSceneManager.SetActiveScene(workspace);
                if (Lightmapping.lightingSettings != clonedLightingSettings)
                    Lightmapping.lightingSettings = clonedLightingSettings;

                ConfigureFlatBlackEnvironment();
                WorkspaceObjects objects = ResolveWorkspaceObjects(workspace, spec);
                ApplyReceiverP0BakeState(objects.Room, spec, objects.RealDoor.transform);
                DisableReceiverReflectionProbes(objects.Room, objects.RealDoor.transform);
                ApplyDungeonRenderingLayerPolicy(objects.Room);
                ApplyDungeonRenderingLayerPolicy(objects.RealDoor);
                AssertDoorRemainsMovableAndProbeBacked(objects.RealDoor, objects.DoorLeaf);

                Renderer[] receiverRenderers = GetReceiverRenderers(objects.Room, objects.RealDoor.transform);
                Renderer[] doorRenderers = GetVisibleRenderers(objects.DoorLeaf.gameObject);
                int cullingMask = CalculateActualGameObjectLayerUnion(receiverRenderers, doorRenderers);
                if (cullingMask != ExpectedCullingMask)
                {
                    throw new InvalidOperationException(
                        "The actual receiver+door GameObject layer union changed. expected=" +
                        ExpectedCullingMask + " actual=" + cullingMask);
                }

                float range = ComputeRange(objects.Injector.transform.position, receiverRenderers, doorRenderers);
                if (!Approximately(range, spec.ExpectedInjectorRange, 0.0005f))
                {
                    throw new InvalidOperationException(
                        "Canonical injector range no longer matches the measured receiver+door bounds. receiver=" +
                        spec.RoomId + " expected=" + F(spec.ExpectedInjectorRange) + " actual=" + F(range));
                }

                ConfigureCanonicalInjector(objects.Injector, stateSpec, cullingMask, range);
                ClearWorkspaceBakeData(workspace, workspacePath);
                // Clear can reset scene lighting data; restore the deliberately cloned settings and
                // explicit state after the sole-scene assertion/clear boundary.
                Lightmapping.lightingSettings = clonedLightingSettings;
                ConfigureCanonicalInjector(objects.Injector, stateSpec, cullingMask, range);
                AssertLightingSettingsCloneHash(clonedLightingSettings, lightingHashBeforeBake);
                EditorSceneManager.MarkSceneDirty(workspace);
                if (!EditorSceneManager.SaveScene(workspace))
                    throw new InvalidOperationException("Unable to save pre-bake workspace state: '" + workspacePath + "'.");

                DateTime startedUtc = DateTime.UtcNow;
                bool baked = Lightmapping.Bake();
                double elapsedSeconds = (DateTime.UtcNow - startedUtc).TotalSeconds;
                if (!baked || Lightmapping.isRunning)
                {
                    throw new InvalidOperationException(
                        "Synchronous Lightmapping.Bake did not complete successfully for " + spec.RoomId +
                        " " + stateSpec.Name + ". baked=" + baked + " running=" + Lightmapping.isRunning +
                        " elapsed=" + elapsedSeconds.ToString("F3", CultureInfo.InvariantCulture));
                }

                AssertLightingSettingsCloneHash(clonedLightingSettings, lightingHashBeforeBake);
                string lightingHashAfterBake = GetDependencyHash(GetLightingSettingsClonePath(spec));
                if (!string.Equals(lightingHashAfterBake, lightingHashBeforeBake, StringComparison.Ordinal))
                    throw new InvalidOperationException("The PoC LightingSettings clone changed during bake.");
                if (!EditorSceneManager.SaveScene(workspace))
                    throw new InvalidOperationException("Unable to save post-bake workspace state: '" + workspacePath + "'.");

                // Re-resolve after the bake/save boundary so no stale component or lightmap pointers
                // are used for the evidence record.
                objects = ResolveWorkspaceObjects(workspace, spec);
                receiverRenderers = GetReceiverRenderers(objects.Room, objects.RealDoor.transform);
                doorRenderers = GetVisibleRenderers(objects.DoorLeaf.gameObject);
                cullingMask = CalculateActualGameObjectLayerUnion(receiverRenderers, doorRenderers);
                if (cullingMask != ExpectedCullingMask)
                    throw new InvalidOperationException("Receiver+door culling union changed during bake.");

                RendererCapture[] rendererCaptures = CaptureRenderers(
                    objects.Room,
                    objects.RealDoor.transform,
                    objects.Doorway,
                    spec);
                FullRendererInventory fullRendererInventory = CaptureFullRendererInventory(
                    objects.Room,
                    objects.RealDoor,
                    objects.Doorway);
                stateArtifact = StateArtifactTransaction.Begin(spec, stateSpec.Name);
                string stateFolder = stateArtifact.StateFolderPath;
                DungeonPortalReceiverResponseCapture.CaptureLightmap[] lightmaps = CaptureLightmaps(
                    rendererCaptures,
                    stateFolder,
                    spec,
                    expectedLightmapCount);
                DungeonPortalReceiverResponseCapture.ProbeSample[] probes = CaptureProbeGrid(
                    objects.Doorway,
                    objects.Room,
                    objects.RealDoor,
                    out string probeLocalPositionSignature);
                DungeonPortalReceiverResponseCapture.FixedCameraCapture[] cameras = CaptureFixedCameras(
                    objects.Doorway,
                    cullingMask,
                    stateFolder);

                capturedState = new DungeonPortalReceiverResponseCapture.CaptureState
                {
                    captured = true,
                    stateName = stateSpec.Name,
                    capturedUtcIso8601 = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    stateFolderPath = stateFolder,
                    elapsedSeconds = elapsedSeconds,
                    lightmapsMode = LightmapSettings.lightmapsMode,
                    injector = BuildInjectorProvenance(objects.Injector),
                    lightmaps = ExtractLightmaps(lightmaps),
                    renderers = ExtractRenderers(rendererCaptures),
                    rendererLayoutSignature = ComputeRendererLayoutSignature(rendererCaptures),
                    fullRendererParitySignature = fullRendererInventory.Signature,
                    fullRendererCount = fullRendererInventory.Count,
                    fullRendererComponentCount = fullRendererInventory.Count,
                    materialPropertyBlocksVerifiedEmpty = true,
                    fullRendererInventory = fullRendererInventory.Entries,
                    lightingSettingsCloneDependencyHashBeforeBake = lightingHashBeforeBake,
                    lightingSettingsCloneDependencyHashAfterBake = lightingHashAfterBake,
                    probes = probes,
                    probeLocalPositionSignature = probeLocalPositionSignature,
                    doorLightProbeCount = 8,
                    doorLightProbeLocalPositionSignature = ComputeDoorLightProbeSignature(objects.RealDoor),
                    fixedCameraCaptures = cameras
                };
                capturedState.stateHash = ComputeStateHash(capturedState);

                int capturedLightmapCount = expectedLightmapCount > 0
                    ? expectedLightmapCount
                    : (capturedState.lightmaps != null ? capturedState.lightmaps.Length : 0);
                if (!IsUsableCheckpointState(
                        capturedState,
                        stateSpec,
                        spec,
                        capturedLightmapCount,
                        expectedRendererCount))
                {
                    throw new InvalidOperationException(
                        "Newly captured " + stateSpec.Name + " state failed its self-check before checkpointing.");
                }
            }
            catch (Exception exception)
            {
                failure = exception;
                if (stateArtifact != null)
                {
                    string cleanupFailure = stateArtifact.TryRollbackUncommitted();
                    stateArtifact = null;
                    if (!string.IsNullOrEmpty(cleanupFailure))
                    {
                        failure = new InvalidOperationException(
                            "State capture failed and its ownership-token cleanup also failed: " + cleanupFailure,
                            exception);
                    }
                }
            }
            finally
            {
                try
                {
                    RestoreAndCaptureCanonicalWorkspaceCheckpoint(spec, clonedLightingSettings);
                    ValidateCanonicalWorkspaceCheckpoint(spec, clonedLightingSettings, provenance);
                }
                catch (Exception restoreFailure)
                {
                    if (stateArtifact != null)
                    {
                        string cleanupFailure = stateArtifact.TryRollbackUncommitted();
                        stateArtifact = null;
                        if (!string.IsNullOrEmpty(cleanupFailure))
                        {
                            restoreFailure = new InvalidOperationException(
                                "Canonical workspace restoration and state-artifact cleanup both failed: " +
                                cleanupFailure,
                                restoreFailure);
                        }
                    }
                    failure = failure == null
                        ? restoreFailure
                        : new AggregateException("State capture and canonical workspace restoration failed.", failure, restoreFailure);
                }
            }

            if (failure != null)
                throw new InvalidOperationException("Receiver state '" + stateSpec.Name + "' failed.", failure);
            return capturedState;
        }

        private static void ConfigureFlatBlackEnvironment()
        {
            Material builtInSkybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            if (builtInSkybox == null)
                throw new InvalidOperationException("Unity's built-in Default-Skybox.mat could not be resolved.");

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientSkyColor = Color.black;
            RenderSettings.ambientEquatorColor = Color.black;
            RenderSettings.ambientGroundColor = Color.black;
            RenderSettings.ambientIntensity = 0.9f;
            RenderSettings.fog = false;
            RenderSettings.skybox = builtInSkybox;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 128;
            RenderSettings.reflectionBounces = 1;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.customReflection = null;
            RenderSettings.sun = null;

            if (RenderSettings.ambientMode != AmbientMode.Flat || RenderSettings.ambientSkyColor != Color.black ||
                RenderSettings.ambientEquatorColor != Color.black || RenderSettings.ambientGroundColor != Color.black ||
                !Approximately(RenderSettings.ambientIntensity, 0.9f, 0.00001f) || RenderSettings.fog ||
                RenderSettings.skybox != builtInSkybox ||
                RenderSettings.defaultReflectionMode != DefaultReflectionMode.Skybox ||
                RenderSettings.defaultReflectionResolution != 128 || RenderSettings.reflectionBounces != 1 ||
                !Approximately(RenderSettings.reflectionIntensity, 1f, 0.00001f) ||
                RenderSettings.sun != null)
            {
                throw new InvalidOperationException("The isolated P0 flat-black RenderSettings contract could not be applied.");
            }
        }

        private static void ClearWorkspaceBakeData(Scene workspace, string workspacePath)
        {
            AssertSoleWorkspaceScene(workspace, workspacePath, "Lightmapping.Clear");
            if (Lightmapping.isRunning)
                throw new InvalidOperationException("Refusing to clear an active lightmapping bake.");

            Lightmapping.lightingDataAsset = null;
            LightmapSettings.lightmaps = Array.Empty<LightmapData>();
            Lightmapping.Clear();
            if (Lightmapping.isRunning)
                throw new InvalidOperationException("Lightmapping.Clear unexpectedly started an asynchronous bake.");
        }

        private static WorkspaceObjects ResolveWorkspaceObjects(Scene workspace, ReceiverSpec spec)
        {
            AssertSoleWorkspaceScene(workspace, GetWorkspacePath(spec), "workspace topology validation");
            GameObject[] roots = workspace.GetRootGameObjects();
            if (roots.Length != 1 || roots[0] == null || roots[0].name != GetRoomInstanceName(spec))
            {
                throw new InvalidOperationException(
                    "Workspace must contain exactly one production receiver root named '" +
                    GetRoomInstanceName(spec) + "'.");
            }

            GameObject room = roots[0];
            string instancePrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(room);
            if (!string.Equals(instancePrefabPath, spec.RoomPrefabPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Workspace receiver root no longer resolves to the required production prefab. expected='" +
                    spec.RoomPrefabPath + "' actual='" + instancePrefabPath + "'.");
            }

            Doorway doorwayComponent = ResolveExactStableDoorway(room, spec.RoomId);
            Transform doorway = doorwayComponent.transform;
            GameObject realDoor = FindUniqueChildObject(room.transform, DoorInstanceName);
            string doorPrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(realDoor);
            if (!string.Equals(doorPrefabPath, DoorPrefabPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Workspace real door no longer resolves to the required production prefab. expected='" +
                    DoorPrefabPath + "' actual='" + doorPrefabPath + "'.");
            }

            Transform leaf = realDoor.transform.Find(DoorLeafPath);
            if (leaf == null)
                throw new InvalidOperationException("The real door has no '" + DoorLeafPath + "' leaf.");

            Transform injectorTransform = doorway.Find(InjectorName);
            Light injector = injectorTransform != null ? injectorTransform.GetComponent<Light>() : null;
            if (injector == null || injectorTransform.GetComponents<Light>().Length != 1)
            {
                throw new InvalidOperationException(
                    "Workspace doorway must contain exactly one canonical PoC injector named '" + InjectorName + "'.");
            }

            return new WorkspaceObjects(room, doorway, realDoor, leaf, injector);
        }

        private static void AssertWorkspaceTopology(
            Scene workspace,
            ReceiverSpec spec,
            GameObject room,
            Transform doorway,
            GameObject realDoor,
            Transform doorLeaf)
        {
            WorkspaceObjects objects = ResolveWorkspaceObjects(workspace, spec);
            if (objects.Room != room || objects.Doorway != doorway || objects.RealDoor != realDoor ||
                objects.DoorLeaf != doorLeaf)
            {
                throw new InvalidOperationException("Workspace topology did not survive its construction round-trip.");
            }
        }

        private static Doorway ResolveExactStableDoorway(GameObject room, string roomId)
        {
            if (room == null)
                throw new ArgumentNullException(nameof(room));

            Transform doorways = RequireIndexedChild(room.transform, 5, "Doorways", roomId + "/Doorways[5]");
            Transform doorModel = RequireIndexedChild(doorways, 0, "Door_SM_A", roomId + "/Door_SM_A[0]");
            Transform waypoint = RequireIndexedChild(doorModel, 0, "DoorWayPoint", StableDoorwayId);
            Doorway doorway = waypoint.GetComponent<Doorway>();
            if (doorway == null)
            {
                throw new InvalidOperationException(
                    "Exact doorway '" + StableDoorwayId + "' has no DunGen.Doorway component in '" + roomId + "'.");
            }

            return doorway;
        }

        private static Transform RequireIndexedChild(Transform parent, int index, string expectedName, string label)
        {
            if (parent == null || index < 0 || parent.childCount <= index)
                throw new InvalidOperationException("Stable hierarchy segment is missing: '" + label + "'.");

            Transform child = parent.GetChild(index);
            if (child == null || !string.Equals(child.name, expectedName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Stable hierarchy segment drifted at '" + label + "'. expected='" + expectedName +
                    "' actual='" + (child != null ? child.name : "null") + "'.");
            }

            return child;
        }

        private static void ConfigureDoorwayVisualsForRealDoor(Transform doorway)
        {
            Transform doorwayAnchor = doorway != null ? doorway.parent : null;
            Transform blocker = doorwayAnchor != null ? doorwayAnchor.Find("Blocker_SM_A") : null;
            Transform openPassage = doorwayAnchor != null ? doorwayAnchor.Find("No_Door_Placement") : null;
            if (blocker == null || openPassage == null)
            {
                throw new InvalidOperationException(
                    "The exact doorway does not expose Blocker_SM_A and No_Door_Placement visual controls.");
            }

            blocker.gameObject.SetActive(false);
            openPassage.gameObject.SetActive(true);
            EditorUtility.SetDirty(blocker.gameObject);
            EditorUtility.SetDirty(openPassage.gameObject);
        }

        private static GameObject InstantiateDoorUsingDunGenPlacement(
            GameObject doorPrefab,
            Scene workspace,
            Doorway placementOwner)
        {
            if (!ContainsViableDoorPrefab(placementOwner, doorPrefab))
            {
                throw new InvalidOperationException(
                    "The exact receiver doorway does not list the required real door prefab as a viable connector.");
            }

            GameObject door = PrefabUtility.InstantiatePrefab(doorPrefab, workspace) as GameObject;
            if (door == null)
                throw new InvalidOperationException("Unity could not instantiate the required real door prefab.");

            // Keep the same DunGen placement offsets/rotation branch as the production PoC scene builder.
            door.name = DoorInstanceName;
            door.transform.SetParent(placementOwner.transform, false);
            door.transform.localPosition = placementOwner.DoorPrefabPositionOffset;
            if (placementOwner.AvoidRotatingDoorPrefab)
                door.transform.rotation = Quaternion.Euler(placementOwner.DoorPrefabRotationOffset);
            else
                door.transform.localRotation = Quaternion.Euler(placementOwner.DoorPrefabRotationOffset);
            door.transform.localScale = Vector3.one;
            door.SetActive(true);
            return door;
        }

        private static bool ContainsViableDoorPrefab(Doorway doorway, GameObject doorPrefab)
        {
            if (doorway == null || doorPrefab == null || doorway.ConnectorPrefabWeights == null)
                return false;

            for (int i = 0; i < doorway.ConnectorPrefabWeights.Count; i++)
            {
                GameObjectWeight candidate = doorway.ConnectorPrefabWeights[i];
                if (candidate != null && candidate.GameObject == doorPrefab && candidate.Weight > 0f)
                    return true;
            }

            return false;
        }

        private static Transform ConfigureDoorFullyOpen(GameObject realDoor, Doorway placementOwner)
        {
            Transform leaf = realDoor != null ? realDoor.transform.Find(DoorLeafPath) : null;
            if (leaf == null)
                throw new InvalidOperationException("The real door prefab has no '" + DoorLeafPath + "' transform.");

            DunGen.Door[] doors = realDoor.GetComponentsInChildren<DunGen.Door>(true);
            if (doors.Length != 1)
                throw new InvalidOperationException("Expected exactly one DunGen.Door on the real door prefab.");

            Tile tile = placementOwner.GetComponentInParent<Tile>();
            if (tile == null)
                throw new InvalidOperationException("The exact doorway has no parent DunGen.Tile.");

            doors[0].DoorwayA = placementOwner;
            doors[0].TileA = tile;
            doors[0].IsOpen = true;

            // The source leaf is closed at identity. Capture the opening policy explicitly as
            // closed * AngleAxis(90, up), rather than relying on any edit-mode animation hook.
            Quaternion closed = leaf.localRotation;
            leaf.localRotation = closed * Quaternion.AngleAxis(90f, Vector3.up);
            Quaternion expectedOpen = Quaternion.Euler(0f, 90f, 0f);
            if (Quaternion.Angle(leaf.localRotation, expectedOpen) > 0.01f)
            {
                throw new InvalidOperationException(
                    "The real door leaf no longer has the canonical closed*90-degree open pose.");
            }

            EditorUtility.SetDirty(doors[0]);
            EditorUtility.SetDirty(leaf);
            return leaf;
        }

        /// <summary>
        /// Mirrors DungeonTileRotationBakeTool.ApplyPowerBakeState(P0) without calling
        /// DungeonTileLightmapSwitcher. This works entirely on the isolated instance,
        /// preserves Ignore* escape hatches, and avoids applying a pre-existing runtime
        /// lightmap asset to the capture workspace.
        /// </summary>
        private static void ApplyReceiverP0BakeState(
            GameObject receiver,
            ReceiverSpec spec,
            Transform excludedDoorSubtree = null)
        {
            if (receiver == null)
                throw new ArgumentNullException(nameof(receiver));

            Dictionary<Material, Material> p0Materials = BuildP0EmissionLookup();
            int enabledIgnoredLights = 0;
            Light[] lights = receiver.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null || IsInSubtree(light.transform, excludedDoorSubtree))
                    continue;

                if (HasEnabledMarker(light, "IgnoreLightControl"))
                {
                    if (light.enabled)
                    {
                        if (!light.gameObject.activeInHierarchy)
                        {
                            throw new InvalidOperationException(
                                "An IgnoreLightControl exception is enabled but inactive in hierarchy: '" +
                                light.name + "'.");
                        }
                        if (string.Equals(spec.RoomId, AdminSpec.RoomId, StringComparison.Ordinal))
                            AssertPreservedAdminLamp(light);
                        else if (string.Equals(
                                     spec.RoomPrefabPath,
                                     EndpointNativeV2StartSpec.RoomPrefabPath,
                                     StringComparison.Ordinal))
                            AssertPreservedV2StartCeilingLamp(light);
                        enabledIgnoredLights++;
                    }
                    continue;
                }

                if (light.enabled)
                {
                    light.enabled = false;
                    EditorUtility.SetDirty(light);
                }
            }

            int expectedEnabledIgnoredLights = string.Equals(
                spec.RoomPrefabPath,
                EndpointNativeV2StartSpec.RoomPrefabPath,
                StringComparison.Ordinal)
                ? 4
                : string.Equals(spec.RoomId, StartSpec.RoomId, StringComparison.Ordinal)
                    ? 0
                    : 2;
            if (enabledIgnoredLights != expectedEnabledIgnoredLights)
            {
                throw new InvalidOperationException(
                    "P0 IgnoreLightControl contract drifted for " + spec.RoomId + ". expected enabled exceptions=" +
                    expectedEnabledIgnoredLights + " actual=" + enabledIgnoredLights + ".");
            }

            Renderer[] renderers = receiver.GetComponentsInChildren<Renderer>(true);
            int registeredMaterialCount = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || IsInSubtree(renderer.transform, excludedDoorSubtree) ||
                    HasEnabledMarker(renderer, "IgnoreEmissionControl"))
                    continue;

                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null || !TryResolveRegisteredP0Material(material, p0Materials, out Material p0Material))
                        continue;
                    registeredMaterialCount++;
                    if (p0Material == null)
                    {
                        throw new InvalidOperationException(
                            "Registered P0 emission material is unavailable for '" + material.name + "'.");
                    }

                    if (materials[materialIndex] == p0Material)
                        continue;
                    materials[materialIndex] = p0Material;
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                    EditorUtility.SetDirty(renderer);
                }
            }

            int normalizedMaterialCount = AssertKnownEmissionSlotsNormalizedToP0(
                receiver,
                spec,
                excludedDoorSubtree,
                p0Materials);
            if (registeredMaterialCount == 0 || normalizedMaterialCount == 0)
            {
                throw new InvalidOperationException(
                    "P0 state found none of the four registered lamp material families for '" +
                    spec.RoomId + "'.");
            }

            DisableReceiverReflectionProbes(receiver, excludedDoorSubtree);
        }

        private static void AssertPreservedAdminLamp(Light light)
        {
            if (light == null)
                throw new ArgumentNullException(nameof(light));
            if (!HasEnabledMarker(light, "IgnoreEmissionControl"))
            {
                throw new InvalidOperationException(
                    "An enabled Admin IgnoreLightControl exception does not also preserve its emission: '" +
                    light.name + "'.");
            }

            // This light is nested two prefab levels deep (Admin room -> Lamp_05).
            // GetCorrespondingObjectFromSource stops at the outer Admin prefab when the
            // room itself is instantiated into the workspace, so validate the original
            // nested source rather than misclassifying a legitimate preserved lamp.
            UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(light.gameObject);
            string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
            if (!string.Equals(sourcePath, PreservedAdminLampPrefabPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Admin P0 may preserve only the two nested Lamp_05 Spotlights. actual source='" +
                    sourcePath + "' light='" + light.name + "'.");
            }
        }

        private static void AssertPreservedV2StartCeilingLamp(Light light)
        {
            if (light == null)
                throw new ArgumentNullException(nameof(light));
            if (!HasEnabledMarker(light, "IgnoreEmissionControl"))
            {
                throw new InvalidOperationException(
                    "An enabled V2 StartRoom IgnoreLightControl exception does not also preserve its emission: '" +
                    light.name + "'.");
            }

            UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(light.gameObject);
            string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
            if (!string.Equals(sourcePath, PreservedV2StartCeilingLampPrefabPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "V2 StartRoom P0 may preserve only the four nested dual-sided ceiling Spotlights. " +
                    "actual source='" + sourcePath + "' light='" + light.name + "'.");
            }
        }

        private static Dictionary<Material, Material> BuildP0EmissionLookup()
        {
            var lookup = new Dictionary<Material, Material>();
            for (int i = 0; i < P0EmissionVariants.Length; i++)
            {
                EmissionMaterialVariant variant = P0EmissionVariants[i];
                Material source = AssetDatabase.LoadAssetAtPath<Material>(variant.SourcePath);
                Material p100 = AssetDatabase.LoadAssetAtPath<Material>(variant.P100Path);
                Material p0 = AssetDatabase.LoadAssetAtPath<Material>(variant.P0Path);
                if (source == null || p100 == null || p0 == null)
                {
                    throw new InvalidOperationException(
                        "Missing a registered P0 emission material triplet. source='" + variant.SourcePath +
                        "' p100='" + variant.P100Path + "' p0='" + variant.P0Path + "'.");
                }

                lookup[source] = p0;
                lookup[p100] = p0;
                lookup[p0] = p0;
            }

            return lookup;
        }

        private static bool TryResolveRegisteredP0Material(
            Material material,
            Dictionary<Material, Material> p0Materials,
            out Material p0Material)
        {
            p0Material = null;
            if (material == null || p0Materials == null)
                return false;
            if (p0Materials.TryGetValue(material, out p0Material))
                return p0Material != null;

            // Prefab instances normally retain the direct asset reference. Resolve by
            // canonical asset path as a defensive equivalence check so a duplicated
            // in-memory reference to a registered source/P100/P0 slot cannot bypass
            // P0 normalization verification.
            string materialPath = AssetDatabase.GetAssetPath(material);
            for (int i = 0; i < P0EmissionVariants.Length; i++)
            {
                EmissionMaterialVariant variant = P0EmissionVariants[i];
                if (!string.Equals(materialPath, variant.SourcePath, StringComparison.Ordinal) &&
                    !string.Equals(materialPath, variant.P100Path, StringComparison.Ordinal) &&
                    !string.Equals(materialPath, variant.P0Path, StringComparison.Ordinal))
                {
                    continue;
                }

                p0Material = AssetDatabase.LoadAssetAtPath<Material>(variant.P0Path);
                return p0Material != null;
            }

            return false;
        }

        private static int AssertKnownEmissionSlotsNormalizedToP0(
            GameObject receiver,
            ReceiverSpec spec,
            Transform excludedDoorSubtree,
            Dictionary<Material, Material> p0Materials)
        {
            if (receiver == null || p0Materials == null)
                throw new ArgumentNullException(receiver == null ? nameof(receiver) : nameof(p0Materials));

            int normalizedCount = 0;
            Renderer[] renderers = receiver.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || IsInSubtree(renderer.transform, excludedDoorSubtree) ||
                    HasEnabledMarker(renderer, "IgnoreEmissionControl"))
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null || !TryResolveRegisteredP0Material(material, p0Materials, out Material expectedP0))
                        continue;

                    normalizedCount++;
                    if (material != expectedP0)
                    {
                        throw new InvalidOperationException(
                            "Known emissive slot was not normalized to its P0 material. receiver='" +
                            spec.RoomId + "' renderer='" + renderer.name + "' materialIndex=" + materialIndex +
                            " actual='" + material.name + "' expected='" + expectedP0.name + "'.");
                    }
                }
            }

            return normalizedCount;
        }

        private static bool HasEnabledMarker(Component component, string markerTypeName)
        {
            if (component == null || string.IsNullOrWhiteSpace(markerTypeName))
                return false;

            MonoBehaviour[] markers = component.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < markers.Length; i++)
            {
                MonoBehaviour marker = markers[i];
                if (marker != null && marker.enabled &&
                    string.Equals(marker.GetType().Name, markerTypeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void DisableReceiverReflectionProbes(GameObject receiver, Transform excludedDoorSubtree = null)
        {
            ReflectionProbe[] probes = receiver.GetComponentsInChildren<ReflectionProbe>(true);
            for (int i = 0; i < probes.Length; i++)
            {
                ReflectionProbe probe = probes[i];
                if (probe != null && !IsInSubtree(probe.transform, excludedDoorSubtree) && probe.enabled)
                {
                    probe.enabled = false;
                    EditorUtility.SetDirty(probe);
                }
            }
        }

        private static void MarkReceiverContributeGiWithBakeExclusions(
            GameObject receiver,
            Transform excludedDoorSubtree)
        {
            if (receiver == null)
                throw new ArgumentNullException(nameof(receiver));

            var stack = new Stack<GiFlagWorkItem>();
            stack.Push(new GiFlagWorkItem(receiver.transform, false));
            while (stack.Count > 0)
            {
                GiFlagWorkItem item = stack.Pop();
                Transform current = item.Transform;
                if (current == null ||
                    (excludedDoorSubtree != null &&
                     (current == excludedDoorSubtree || current.IsChildOf(excludedDoorSubtree))))
                {
                    continue;
                }

                bool excluded = item.InsideExcludedSubtree || IsWallDecalTransformRoot(current) ||
                                IsReceptionWindowVisualTransform(current);
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(current.gameObject);
                StaticEditorFlags updated = excluded
                    ? flags & ~StaticEditorFlags.ContributeGI
                    : flags | StaticEditorFlags.ContributeGI;
                if (updated != flags)
                    GameObjectUtility.SetStaticEditorFlags(current.gameObject, updated);

                for (int childIndex = 0; childIndex < current.childCount; childIndex++)
                    stack.Push(new GiFlagWorkItem(current.GetChild(childIndex), excluded));
            }
        }

        private static bool IsWallDecalTransformRoot(Transform transform)
        {
            if (transform == null)
                return false;

            Renderer[] renderers = transform.GetComponents<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (IsWallDecalRenderer(renderers[i]))
                    return true;
            }

            return false;
        }

        private static bool IsWallDecalRenderer(Renderer renderer)
        {
            if (!IsDecalRenderer(renderer))
                return false;

            string name = renderer.name ?? string.Empty;
            string objectName = renderer.gameObject != null ? renderer.gameObject.name ?? string.Empty : string.Empty;
            if (name.IndexOf("Decal_Corridor_C", StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("Decal_Corridor_C", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            Vector3 size = renderer.bounds.size;
            bool floorLike = size.y <= 0.08f && size.x >= 0.1f && size.z >= 0.1f;
            return !floorLike;
        }

        private static bool IsDecalRenderer(Renderer renderer)
        {
            if (renderer == null)
                return false;

            if (ContainsDecalToken(renderer.name) || ContainsDecalToken(renderer.gameObject.name))
                return true;

            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                    continue;
                string path = AssetDatabase.GetAssetPath(material);
                string shaderName = material.shader != null ? material.shader.name : string.Empty;
                if (ContainsDecalToken(material.name) || ContainsDecalToken(path) ||
                    path.IndexOf("/DECAL_MATERIAL/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    shaderName.IndexOf("Decals", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsDecalToken(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsReceptionWindowVisualTransform(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (string.Equals(current.name, "ReceptionWindowVisual", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void AssertDoorRemainsMovableAndProbeBacked(GameObject realDoor, Transform doorLeaf)
        {
            if (realDoor == null || doorLeaf == null)
                throw new ArgumentNullException(realDoor == null ? nameof(realDoor) : nameof(doorLeaf));

            Transform[] transforms = realDoor.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (GameObjectUtility.GetStaticEditorFlags(transforms[i].gameObject) != (StaticEditorFlags)0)
                {
                    throw new InvalidOperationException(
                        "The real movable door subtree must preserve staticFlags=0: '" +
                        transforms[i].name + "'.");
                }
            }

            if (Quaternion.Angle(doorLeaf.localRotation, Quaternion.Euler(0f, 90f, 0f)) > 0.01f)
            {
                throw new InvalidOperationException(
                    "The real door leaf is not held at the canonical fully open closed*90-degree pose.");
            }

            LightProbeGroup[] groups = realDoor.GetComponentsInChildren<LightProbeGroup>(true);
            if (groups.Length != 1 || groups[0] == null || groups[0].probePositions == null ||
                groups[0].probePositions.Length != 8 || !groups[0].enabled)
            {
                throw new InvalidOperationException(
                    "The real door must preserve its enabled eight-point LightProbeGroup.");
            }
        }

        private static void ApplyDungeonRenderingLayerPolicy(GameObject root)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            int dungeonLayerIndex = RenderingLayerMask.NameToRenderingLayer("Dungeon");
            if (dungeonLayerIndex < 0)
                throw new InvalidOperationException("The required production rendering layer 'Dungeon' is undefined.");
            uint targetMask = 1u << dungeonLayerIndex;
            if (targetMask != RequiredDungeonRenderingLayerMask)
            {
                throw new InvalidOperationException(
                    "The required production rendering layer 'Dungeon' no longer resolves to mask 2. actual=" +
                    targetMask);
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer.renderingLayerMask == targetMask)
                    continue;
                renderer.renderingLayerMask = targetMask;
                EditorUtility.SetDirty(renderer);
            }

            Light[] lights = root.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null)
                    continue;
                light.renderingLayerMask = unchecked((int)targetMask);
                UniversalAdditionalLightData additional = light.GetUniversalAdditionalLightData();
                if (additional == null)
                    throw new InvalidOperationException("URP additional light data is unavailable for '" + light.name + "'.");
                additional.renderingLayers = targetMask;
                additional.shadowRenderingLayers = targetMask;
                EditorUtility.SetDirty(light);
                EditorUtility.SetDirty(additional);
            }
        }

        private static void CreateCanonicalInjector(Transform doorway, int cullingMask, float range)
        {
            if (doorway == null)
                throw new ArgumentNullException(nameof(doorway));
            if (doorway.Find(InjectorName) != null)
                throw new InvalidOperationException("A canonical injector already exists below the doorway.");

            var injectorObject = new GameObject(InjectorName);
            injectorObject.transform.SetParent(doorway, false);
            Light injector = injectorObject.AddComponent<Light>();
            ConfigureCanonicalInjector(injector, StateSpecs[0], cullingMask, range);
            EditorUtility.SetDirty(injector);
        }

        private static void ConfigureCanonicalInjector(
            Light injector,
            BakeStateSpec state,
            int cullingMask,
            float range)
        {
            if (injector == null)
                throw new ArgumentNullException(nameof(injector));
            if (cullingMask != ExpectedCullingMask)
                throw new InvalidOperationException("The injector culling mask must equal the canonical receiver+door union.");

            injector.transform.localPosition = new Vector3(0f, 1f, 0.5f);
            injector.transform.localEulerAngles = new Vector3(0f, 180f, 0f);
            injector.type = LightType.Spot;
            injector.color = Color.white;
            injector.intensity = 1f;
            injector.range = range;
            injector.spotAngle = CanonicalSpotAngle;
            injector.innerSpotAngle = CanonicalSpotAngle;
            injector.shadows = LightShadows.Soft;
            injector.shadowStrength = 1f;
            injector.cookie = null;
            injector.cullingMask = cullingMask;
            injector.renderingLayerMask = RequiredDungeonRenderingLayerMask;
            injector.lightmapBakeType = LightmapBakeType.Baked;
            injector.bounceIntensity = state.BounceIntensity;
            injector.enabled = state.Enabled;
            UniversalAdditionalLightData additional = injector.GetUniversalAdditionalLightData();
            if (additional == null)
                throw new InvalidOperationException("URP additional light data is unavailable for the canonical injector.");
            additional.renderingLayers = RequiredDungeonRenderingLayerMask;
            additional.shadowRenderingLayers = RequiredDungeonRenderingLayerMask;
            EditorUtility.SetDirty(injector);
            EditorUtility.SetDirty(additional);
        }

        private static DungeonPortalReceiverResponseCapture.InjectorProvenance BuildInjectorProvenance(Light injector)
        {
            if (injector == null)
                throw new ArgumentNullException(nameof(injector));

            UniversalAdditionalLightData additional = injector.GetUniversalAdditionalLightData();
            if (additional == null)
                throw new InvalidOperationException("The canonical injector has no URP additional light data.");

            return new DungeonPortalReceiverResponseCapture.InjectorProvenance
            {
                enabled = injector.enabled,
                type = injector.type,
                lightmapBakeType = injector.lightmapBakeType,
                color = injector.color,
                intensity = injector.intensity,
                bounceIntensity = injector.bounceIntensity,
                localPosition = injector.transform.localPosition,
                localEulerAngles = NormalizeEulerAngles(injector.transform.localEulerAngles),
                placementInterpretation = ReceiverIncomingPlacementInterpretation,
                range = injector.range,
                spotAngle = injector.spotAngle,
                innerSpotAngle = injector.innerSpotAngle,
                shadows = injector.shadows,
                shadowStrength = injector.shadowStrength,
                cullingMask = injector.cullingMask,
                renderingLayerMask = injector.renderingLayerMask,
                shadowRenderingLayerMask = unchecked((int)additional.shadowRenderingLayers)
            };
        }

        private static Renderer[] GetReceiverRenderers(GameObject room, Transform realDoorTransform)
        {
            if (room == null || realDoorTransform == null)
                throw new ArgumentNullException(room == null ? nameof(room) : nameof(realDoorTransform));

            Renderer[] all = room.GetComponentsInChildren<Renderer>(true);
            var receiver = new List<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer renderer = all[i];
                if (renderer == null || IsInSubtree(renderer.transform, realDoorTransform))
                {
                    continue;
                }
                receiver.Add(renderer);
            }

            if (receiver.Count == 0)
                throw new InvalidOperationException("The receiver workspace contains no production renderers.");
            return receiver.ToArray();
        }

        private static Renderer[] GetVisibleRenderers(GameObject root)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
            var visible = new List<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer renderer = all[i];
                if (renderer != null && renderer.enabled && !renderer.forceRenderingOff &&
                    renderer.gameObject.activeInHierarchy)
                {
                    visible.Add(renderer);
                }
            }

            if (visible.Count == 0)
                throw new InvalidOperationException("'" + root.name + "' has no visible renderers.");
            return visible.ToArray();
        }

        private static int CalculateActualGameObjectLayerUnion(
            GameObject room,
            Transform realDoorTransform,
            Transform doorLeaf)
        {
            return CalculateActualGameObjectLayerUnion(
                GetReceiverRenderers(room, realDoorTransform),
                GetVisibleRenderers(doorLeaf.gameObject));
        }

        private static int CalculateActualGameObjectLayerUnion(
            Renderer[] receiverRenderers,
            Renderer[] doorRenderers)
        {
            int union = 0;
            AccumulateGameObjectLayers(receiverRenderers, ref union, "receiver");
            AccumulateGameObjectLayers(doorRenderers, ref union, "door leaf");
            if (union == 0)
                throw new InvalidOperationException("The actual receiver+door GameObject layer union is empty.");
            return union;
        }

        private static void AccumulateGameObjectLayers(Renderer[] renderers, ref int union, string label)
        {
            if (renderers == null || renderers.Length == 0)
                throw new InvalidOperationException("The " + label + " renderer set is empty.");

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer.gameObject.layer < 0 || renderer.gameObject.layer > 31)
                    throw new InvalidOperationException("The " + label + " renderer set contains an invalid GameObject layer.");
                if (renderer.renderingLayerMask != RequiredDungeonRenderingLayerMask)
                {
                    throw new InvalidOperationException(
                        "The " + label + " renderer does not carry the required Dungeon rendering mask 2: '" +
                        renderer.name + "'.");
                }
                union |= 1 << renderer.gameObject.layer;
            }
        }

        private static float ComputeRange(Vector3 lightPosition, Renderer[] receiverRenderers, Renderer[] doorRenderers)
        {
            float farthestSquared = 0f;
            AccumulateFarthestBoundsDistance(lightPosition, receiverRenderers, ref farthestSquared);
            AccumulateFarthestBoundsDistance(lightPosition, doorRenderers, ref farthestSquared);
            if (farthestSquared <= 0f)
                throw new InvalidOperationException("Unable to derive the canonical injector range from receiver+door bounds.");
            return Mathf.Sqrt(farthestSquared) + 0.01f;
        }

        private static void AccumulateFarthestBoundsDistance(Vector3 position, Renderer[] renderers, ref float farthestSquared)
        {
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                Bounds bounds = renderer.bounds;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    farthestSquared = Mathf.Max(farthestSquared, (point - position).sqrMagnitude);
                }
            }
        }

        private static RendererCapture[] CaptureRenderers(
            GameObject room,
            Transform realDoorTransform,
            Transform doorway,
            ReceiverSpec spec)
        {
            Renderer[] receiverRenderers = GetReceiverRenderers(room, realDoorTransform);
            var captures = new List<RendererCapture>();
            for (int i = 0; i < receiverRenderers.Length; i++)
            {
                Renderer renderer = receiverRenderers[i];
                Mesh mesh = GetRendererMesh(renderer);
                if (mesh == null || renderer.lightmapIndex < 0)
                    continue;

                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string meshGuid, out long meshLocalId) ||
                    string.IsNullOrWhiteSpace(meshGuid) || meshLocalId == 0L)
                {
                    throw new InvalidOperationException(
                        "Unable to fingerprint receiver mesh '" + mesh.name + "' for renderer '" + renderer.name + "'.");
                }

                captures.Add(new RendererCapture
                {
                    Data = new DungeonPortalReceiverResponseCapture.CaptureRenderer
                    {
                        relativePath = GetStableRelativePath(room.transform, renderer.transform),
                        componentOrdinal = GetRendererComponentOrdinal(renderer),
                        meshAssetGuid = meshGuid,
                        meshLocalId = meshLocalId,
                        meshUv2Hash = ComputeUv2Hash(mesh),
                        lightmapIndex = renderer.lightmapIndex,
                        lightmapScaleOffset = renderer.lightmapScaleOffset
                    },
                    typeName = renderer.GetType().FullName ?? renderer.GetType().Name,
                    parityMetadata = BuildRendererParityMetadata(renderer, doorway)
                });
            }

            captures.Sort(RendererCaptureComparer.Instance);
            var bucketCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < captures.Count; i++)
            {
                RendererCapture capture = captures[i];
                string bucketKey = capture.Data.relativePath;
                if (!bucketCounts.TryGetValue(bucketKey, out int bucketIndex))
                    bucketIndex = 0;
                capture.Data.rendererBucketIndex = bucketIndex;
                bucketCounts[bucketKey] = bucketIndex + 1;
                captures[i] = capture;
            }

            if (captures.Count <= 0 ||
                (spec.ExpectedCapturedRendererCount > 0 &&
                 captures.Count != spec.ExpectedCapturedRendererCount))
            {
                throw new InvalidOperationException(
                    "Receiver renderer capture layout drifted. receiver=" + spec.RoomId + " expected=" +
                    (spec.ExpectedCapturedRendererCount > 0
                        ? spec.ExpectedCapturedRendererCount.ToString(CultureInfo.InvariantCulture)
                        : "Baseline-authoritative") +
                    " actual=" + captures.Count + ".");
            }

            return captures.ToArray();
        }

        /// <summary>
        /// Captures a separate whole-workspace renderer inventory. The compact
        /// lightmap mapping deliberately remains connected-receiver-only (Start=76;
        /// Admin Baseline is authoritative and later states must match it exactly),
        /// while this inventory includes disabled/non-lightmapped renderers and the
        /// complete real-door subtree so no scene override can hide outside UV2 data.
        /// </summary>
        private static FullRendererInventory CaptureFullRendererInventory(
            GameObject room,
            GameObject realDoor,
            Transform doorway)
        {
            if (room == null || realDoor == null || doorway == null)
                throw new ArgumentNullException("Full renderer inventory inputs must be non-null.");

            var records = new List<FullRendererInventoryRecord>();
            AddFullRendererInventoryScope(
                records,
                "ReceiverRoom",
                room.transform,
                GetReceiverRenderers(room, realDoor.transform),
                doorway);
            AddFullRendererInventoryScope(
                records,
                "RealDoor",
                realDoor.transform,
                realDoor.GetComponentsInChildren<Renderer>(true),
                doorway);
            if (records.Count == 0)
                throw new InvalidOperationException("The full receiver+real-door renderer inventory is empty.");

            records.Sort(FullRendererInventoryRecordComparer.Instance);
            var buckets = new HashSet<string>(StringComparer.Ordinal);
            var entries = new DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[records.Count];
            for (int i = 0; i < records.Count; i++)
            {
                FullRendererInventoryRecord record = records[i];
                if (!buckets.Add(record.Entry.canonicalBucket))
                {
                    throw new InvalidOperationException(
                        "Full renderer inventory contains a duplicate canonical path/component bucket: '" +
                        record.Entry.canonicalBucket + "'.");
                }
                entries[i] = record.Entry;
            }

            return new FullRendererInventory(entries, ComputeFullRendererParitySignature(entries));
        }

        private static void AddFullRendererInventoryScope(
            List<FullRendererInventoryRecord> records,
            string scope,
            Transform scopeRoot,
            Renderer[] renderers,
            Transform doorway)
        {
            if (records == null || scopeRoot == null || renderers == null || doorway == null)
                throw new ArgumentNullException("Full renderer inventory scope inputs must be non-null.");

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    throw new InvalidOperationException("The full renderer inventory contains a null renderer component.");

                string relativePath = GetStableRelativePath(scopeRoot, renderer.transform);
                string rendererType = renderer.GetType().FullName ?? renderer.GetType().Name;
                int componentOrdinal = GetRendererComponentOrdinal(renderer);
                string bucket = scope + "|" + relativePath + "|" + rendererType + "|" + componentOrdinal;
                records.Add(new FullRendererInventoryRecord
                {
                    Entry = new DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry
                    {
                        scope = scope,
                        relativePath = relativePath,
                        rendererType = rendererType,
                        componentOrdinal = componentOrdinal,
                        canonicalBucket = bucket,
                        parityMetadataHash = BuildRendererParityMetadata(renderer, doorway)
                    }
                });
            }
        }

        private static string ComputeFullRendererParitySignature(
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] entries)
        {
            var builder = new StringBuilder(entries != null ? entries.Length * 192 : 16);
            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry entry = entries[i];
                    AppendString(builder, entry.scope);
                    AppendString(builder, entry.relativePath);
                    AppendString(builder, entry.rendererType);
                    builder.Append(entry.componentOrdinal).Append('|');
                    AppendString(builder, entry.canonicalBucket);
                    AppendString(builder, entry.parityMetadataHash);
                }
            }
            return ComputeSha256(builder.ToString());
        }

        private static Mesh GetRendererMesh(Renderer renderer)
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

        private static int GetRendererComponentOrdinal(Renderer renderer)
        {
            Renderer[] sameObject = renderer.GetComponents<Renderer>();
            for (int i = 0; i < sameObject.Length; i++)
            {
                if (sameObject[i] == renderer)
                    return i;
            }

            throw new InvalidOperationException("Unable to resolve a renderer component ordinal.");
        }

        private static string GetStableRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || (target != root && !target.IsChildOf(root)))
                throw new InvalidOperationException("Cannot make a stable relative path outside the receiver root.");
            if (target == root)
                return ".";

            var segments = new List<string>();
            for (Transform current = target; current != null && current != root; current = current.parent)
                segments.Add(current.name + "[" + current.GetSiblingIndex() + "]");
            if (segments.Count == 0)
                throw new InvalidOperationException("Stable relative path unexpectedly had no segments.");
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static string ComputeUv2Hash(Mesh mesh)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            Vector2[] uv2;
            try
            {
                uv2 = mesh.uv2;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Unable to read UV2 for mesh '" + mesh.name + "'.", exception);
            }

            if (uv2 != null && uv2.Length != 0 && uv2.Length != mesh.vertexCount)
            {
                throw new InvalidOperationException(
                    "Receiver mesh has a partial UV2 channel that cannot be fingerprinted safely: '" +
                    mesh.name + "'.");
            }

            var builder = new StringBuilder((uv2 != null ? uv2.Length : 0) * 24 + 256);
            if (uv2 == null || uv2.Length == 0)
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId) ||
                    string.IsNullOrWhiteSpace(guid) || localId == 0L)
                {
                    throw new InvalidOperationException(
                        "Receiver mesh without stored UV2 is not a persistent asset: '" + mesh.name + "'.");
                }

                string assetPath = AssetDatabase.GetAssetPath(mesh);
                if (string.IsNullOrWhiteSpace(assetPath))
                    throw new InvalidOperationException("Receiver mesh has no asset path: '" + mesh.name + "'.");
                ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                AppendString(builder, "NO_STORED_UV2");
                AppendString(builder, guid);
                builder.Append(localId).Append('|');
                AppendString(builder, assetPath);
                AppendString(builder, GetDependencyHash(assetPath));
                AppendString(builder, importer != null ? "ModelImporter" : "NO_MODEL_IMPORTER");
                builder.Append(importer != null && importer.generateSecondaryUV ? '1' : '0').Append('|');
                builder.Append(mesh.vertexCount).Append('|');
                builder.Append(mesh.subMeshCount).Append('|');
                builder.Append((int)mesh.indexFormat).Append('|');
                return ComputeSha256(builder.ToString());
            }

            AppendString(builder, "STORED_UV2");
            builder.Append(mesh.vertexCount).Append('|');
            for (int i = 0; i < uv2.Length; i++)
            {
                AppendFloat(builder, uv2[i].x);
                AppendFloat(builder, uv2[i].y);
            }
            return ComputeSha256(builder.ToString());
        }

        /// <summary>
        /// Cross-pass renderer parity payload. It intentionally remains hash-only in the
        /// capture asset, but covers all visual/material state that could invalidate a
        /// later pixel-domain fit while leaving the compact renderer mapping schema stable.
        /// </summary>
        private static string BuildRendererParityMetadata(Renderer renderer, Transform doorway)
        {
            if (renderer == null || doorway == null)
                throw new ArgumentNullException(renderer == null ? nameof(renderer) : nameof(doorway));

            AssertRendererMaterialPropertyBlocksAreEmpty(renderer);
            var builder = new StringBuilder(512);
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
            builder.Append(renderer.lightmapIndex).Append('|');
            AppendVector4(builder, renderer.lightmapScaleOffset);
            AppendRendererGiAndScaleInLightmapParity(builder, renderer);
            AppendMeshParity(builder, GetRendererMesh(renderer));
            if (renderer is MeshRenderer meshRenderer)
                AppendMeshParity(builder, meshRenderer.additionalVertexStreams);
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

        private static void AssertRendererMaterialPropertyBlocksAreEmpty(Renderer renderer)
        {
            if (renderer == null)
                throw new ArgumentNullException(nameof(renderer));

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            if (!block.isEmpty)
            {
                throw new InvalidOperationException(
                    "Renderer MaterialPropertyBlock evidence is non-empty and cannot be deterministically serialized. " +
                    "Capture is fail-closed. renderer='" + renderer.name + "' scope='global'.");
            }

            int materialCount = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0;
            for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
            {
                block.Clear();
                renderer.GetPropertyBlock(block, materialIndex);
                if (!block.isEmpty)
                {
                    throw new InvalidOperationException(
                        "Renderer MaterialPropertyBlock evidence is non-empty and cannot be deterministically serialized. " +
                        "Capture is fail-closed. renderer='" + renderer.name + "' materialIndex=" + materialIndex + ".");
                }
            }
        }

        private static void AppendObjectReferenceParity(StringBuilder builder, UnityEngine.Object value)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (value == null)
            {
                AppendString(builder, "<null>");
                return;
            }

            GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(value);
            AppendString(builder, globalId.ToString());
            AppendString(builder, AssetDatabase.GetAssetPath(value));
        }

        private static void AppendMeshParity(StringBuilder builder, Mesh mesh)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (mesh == null)
            {
                AppendString(builder, "<no-mesh>");
                return;
            }

            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId) ||
                string.IsNullOrWhiteSpace(guid) || localId == 0L)
            {
                throw new InvalidOperationException(
                    "Full renderer inventory cannot fingerprint a non-persistent mesh. mesh='" + mesh.name + "'.");
            }
            AppendString(builder, guid);
            builder.Append(localId).Append('|');
            AppendString(builder, AssetDatabase.GetAssetPath(mesh));
            AppendString(builder, GetDependencyHash(AssetDatabase.GetAssetPath(mesh)));
            builder.Append(mesh.vertexCount).Append('|');
        }

        /// <summary>
        /// Unity 6000 exposes Receive GI and Scale In Lightmap on concrete renderer
        /// inspectors rather than the Renderer base API. Read the serialized fields so
        /// captured compatible renderers are fingerprinted without silently dropping a
        /// GI-affecting distinction.
        /// </summary>
        private static void AppendRendererGiAndScaleInLightmapParity(StringBuilder builder, Renderer renderer)
        {
            if (builder == null || renderer == null)
                throw new ArgumentNullException(builder == null ? nameof(builder) : nameof(renderer));

            var serialized = new SerializedObject(renderer);
            SerializedProperty receiveGi = serialized.FindProperty("m_ReceiveGI");
            SerializedProperty scaleInLightmap = serialized.FindProperty("m_ScaleInLightmap");
            if (receiveGi == null || scaleInLightmap == null)
            {
                // A few non-lightmapped Renderer subclasses legitimately do not expose
                // these MeshRenderer inspector fields. Preserve that distinction in the
                // parity hash rather than pretending they have the MeshRenderer default.
                AppendString(builder, "<ReceiveGI-or-ScaleInLightmap-not-applicable>");
                return;
            }

            builder.Append(receiveGi.intValue).Append('|');
            AppendFloat(builder, scaleInLightmap.floatValue);
        }

        private static void AppendMaterialParity(StringBuilder builder, Material material)
        {
            if (material == null)
            {
                AppendString(builder, "<null>");
                return;
            }

            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string materialGuid, out long materialLocalId) ||
                string.IsNullOrWhiteSpace(materialGuid))
            {
                throw new InvalidOperationException(
                    "Receiver material cannot be fingerprinted as a persistent asset: '" + material.name + "'.");
            }
            AppendString(builder, materialGuid);
            builder.Append(materialLocalId).Append('|');
            string materialPath = AssetDatabase.GetAssetPath(material);
            AppendString(builder, materialPath);
            AppendString(builder, GetDependencyHash(materialPath));
            builder.Append(material.renderQueue).Append('|');

            Shader shader = material.shader;
            if (shader == null)
            {
                AppendString(builder, "<null shader>");
            }
            else
            {
                string shaderGuid = string.Empty;
                long shaderLocalId = 0L;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(shader, out shaderGuid, out shaderLocalId);
                AppendString(builder, shaderGuid);
                builder.Append(shaderLocalId).Append('|');
                AppendString(builder, shader.name);
                AppendString(builder, AssetDatabase.GetAssetPath(shader));
            }

            string[] keywords = material.shaderKeywords ?? Array.Empty<string>();
            string[] sortedKeywords = (string[])keywords.Clone();
            Array.Sort(sortedKeywords, StringComparer.Ordinal);
            builder.Append(sortedKeywords.Length).Append('|');
            for (int i = 0; i < sortedKeywords.Length; i++)
                AppendString(builder, sortedKeywords[i]);

            for (int i = 0; i < KnownMaterialParityProperties.Length; i++)
            {
                string propertyName = KnownMaterialParityProperties[i];
                bool hasProperty = material.HasProperty(propertyName);
                AppendString(builder, propertyName);
                builder.Append(hasProperty ? '1' : '0').Append('|');
                if (hasProperty)
                    AppendFloat(builder, material.GetFloat(propertyName));
            }
        }

        private static DungeonPortalReceiverResponseCapture.CaptureLightmap[] CaptureLightmaps(
            RendererCapture[] renderers,
            string stateFolder,
            ReceiverSpec spec,
            int expectedLightmapCount)
        {
            if (LightmapSettings.lightmapsMode != LightmapsMode.CombinedDirectional)
            {
                throw new InvalidOperationException(
                    "The receiver capture requires exact CombinedDirectional lightmaps. actual=" +
                    LightmapSettings.lightmapsMode + ".");
            }

            LightmapData[] sourceLightmaps = LightmapSettings.lightmaps;
            if (sourceLightmaps == null || sourceLightmaps.Length == 0)
                throw new InvalidOperationException("The completed bake produced no lightmaps.");

            var usedIndices = new List<int>();
            for (int i = 0; i < renderers.Length; i++)
            {
                int index = renderers[i].Data.lightmapIndex;
                if (index < 0 || index >= sourceLightmaps.Length)
                    throw new InvalidOperationException("Captured renderer has an invalid post-bake lightmap index: " + index + ".");
                if (!usedIndices.Contains(index))
                    usedIndices.Add(index);
            }
            usedIndices.Sort();
            if (expectedLightmapCount > 0 && usedIndices.Count != expectedLightmapCount)
            {
                throw new InvalidOperationException(
                    "Receiver lightmap layout drifted. receiver=" + spec.RoomId + " expected=" +
                    expectedLightmapCount + " actual=" + usedIndices.Count + ".");
            }

            EnsureAssetFolder(stateFolder);
            string lightmapFolder = stateFolder + "/Lightmaps";
            EnsureAssetFolder(lightmapFolder);
            var captured = new DungeonPortalReceiverResponseCapture.CaptureLightmap[usedIndices.Count];
            for (int i = 0; i < usedIndices.Count; i++)
            {
                int sourceIndex = usedIndices[i];
                LightmapData source = sourceLightmaps[sourceIndex];
                if (source == null || source.lightmapColor == null || source.lightmapDir == null)
                {
                    throw new InvalidOperationException(
                        "CombinedDirectional lightmap " + sourceIndex + " has no stable color+direction texture.");
                }

                Texture2D color = CopyTextureAsset(source.lightmapColor, lightmapFolder, "LM" + sourceIndex + "_Color");
                Texture2D direction = CopyTextureAsset(source.lightmapDir, lightmapFolder, "LM" + sourceIndex + "_Direction");
                Texture2D shadowMask = source.shadowMask != null
                    ? CopyTextureAsset(source.shadowMask, lightmapFolder, "LM" + sourceIndex + "_ShadowMask")
                    : null;
                captured[i] = BuildLightmapRecord(sourceIndex, color, direction, shadowMask);
            }

            return captured;
        }

        private static DungeonPortalReceiverResponseCapture.CaptureLightmap BuildLightmapRecord(
            int sourceIndex,
            Texture2D color,
            Texture2D direction,
            Texture2D shadowMask)
        {
            if (color == null || direction == null)
                throw new InvalidOperationException("A copied CombinedDirectional lightmap texture is null.");

            return new DungeonPortalReceiverResponseCapture.CaptureLightmap
            {
                sourceLightmapIndex = sourceIndex,
                colorTexture = color,
                directionTexture = direction,
                shadowMaskTexture = shadowMask,
                hasShadowMask = shadowMask != null,
                colorAssetPath = AssetDatabase.GetAssetPath(color),
                directionAssetPath = AssetDatabase.GetAssetPath(direction),
                shadowMaskAssetPath = shadowMask != null ? AssetDatabase.GetAssetPath(shadowMask) : string.Empty,
                colorDependencyHash = GetDependencyHash(AssetDatabase.GetAssetPath(color)),
                directionDependencyHash = GetDependencyHash(AssetDatabase.GetAssetPath(direction)),
                shadowMaskDependencyHash = shadowMask != null
                    ? GetDependencyHash(AssetDatabase.GetAssetPath(shadowMask))
                    : string.Empty,
                colorWidth = color.width,
                colorHeight = color.height,
                colorFormat = color.format.ToString(),
                directionWidth = direction.width,
                directionHeight = direction.height,
                directionFormat = direction.format.ToString(),
                shadowMaskWidth = shadowMask != null ? shadowMask.width : 0,
                shadowMaskHeight = shadowMask != null ? shadowMask.height : 0,
                shadowMaskFormat = shadowMask != null ? shadowMask.format.ToString() : string.Empty
            };
        }

        private static Texture2D CopyTextureAsset(Texture2D source, string folder, string fileStem)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(extension))
            {
                throw new InvalidOperationException(
                    "Cannot persist a non-asset lightmap texture as stable evidence: '" + source.name + "'.");
            }

            string destinationPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + fileStem + extension);
            if (!IsGeneratedPath(destinationPath))
                throw new InvalidOperationException("Refusing to copy a lightmap outside the PoC Generated/Bounce root.");
            if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
            {
                throw new InvalidOperationException(
                    "Unable to copy baked texture evidence from '" + sourcePath + "' to '" + destinationPath + "'.");
            }

            AssetDatabase.ImportAsset(destinationPath, ImportAssetOptions.ForceSynchronousImport);
            Texture2D copied = AssetDatabase.LoadAssetAtPath<Texture2D>(destinationPath);
            if (copied == null)
                throw new InvalidOperationException("Copied lightmap evidence could not be reloaded: '" + destinationPath + "'.");
            return copied;
        }

        private static DungeonPortalReceiverResponseCapture.ProbeSample[] CaptureProbeGrid(
            Transform doorway,
            GameObject receiver,
            GameObject realDoor,
            out string localPositionSignature)
        {
            if (doorway == null || receiver == null || realDoor == null)
                throw new ArgumentNullException("Doorway/receiver/real-door probe capture inputs must be non-null.");
            if (LightmapSettings.lightProbes == null)
                throw new InvalidOperationException("The completed bake has no LightProbes asset for doorway response sampling.");

            var worldPositions = new Vector3[ProbeLocalPositions.Length];
            for (int i = 0; i < ProbeLocalPositions.Length; i++)
                worldPositions[i] = doorway.TransformPoint(ProbeLocalPositions[i]);

            Physics.SyncTransforms();
            for (int i = 0; i < worldPositions.Length; i++)
            {
                Collider[] overlaps = Physics.OverlapSphere(
                    worldPositions[i],
                    0.05f,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore);
                for (int overlapIndex = 0; overlapIndex < overlaps.Length; overlapIndex++)
                {
                    Collider overlap = overlaps[overlapIndex];
                    if (overlap == null)
                        continue;
                    Transform transform = overlap.transform;
                    if (transform == receiver.transform || transform.IsChildOf(receiver.transform) ||
                        transform == realDoor.transform || transform.IsChildOf(realDoor.transform))
                    {
                        throw new InvalidOperationException(
                            "Deterministic doorway probe stencil overlaps receiver/door geometry at index " + i +
                            " local=" + ProbeLocalPositions[i] + " collider='" + overlap.name + "'. " +
                            "The tool will not nudge sample points.");
                    }
                }
            }

            var harmonics = new SphericalHarmonicsL2[worldPositions.Length];
            var occlusions = new Vector4[worldPositions.Length];
            LightProbes.CalculateInterpolatedLightAndOcclusionProbes(worldPositions, harmonics, occlusions);
            var samples = new DungeonPortalReceiverResponseCapture.ProbeSample[worldPositions.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = new DungeonPortalReceiverResponseCapture.ProbeSample
                {
                    probeIndex = i,
                    localPosition = ProbeLocalPositions[i],
                    worldPosition = worldPositions[i],
                    coefficient0 = GetShCoefficient(harmonics[i], 0),
                    coefficient1 = GetShCoefficient(harmonics[i], 1),
                    coefficient2 = GetShCoefficient(harmonics[i], 2),
                    coefficient3 = GetShCoefficient(harmonics[i], 3),
                    coefficient4 = GetShCoefficient(harmonics[i], 4),
                    coefficient5 = GetShCoefficient(harmonics[i], 5),
                    coefficient6 = GetShCoefficient(harmonics[i], 6),
                    coefficient7 = GetShCoefficient(harmonics[i], 7),
                    coefficient8 = GetShCoefficient(harmonics[i], 8),
                    occlusion = occlusions[i]
                };
                if (!IsFinite(samples[i].occlusion) || !IsFinite(samples[i].coefficient0) ||
                    !IsFinite(samples[i].coefficient1) || !IsFinite(samples[i].coefficient2) ||
                    !IsFinite(samples[i].coefficient3) || !IsFinite(samples[i].coefficient4) ||
                    !IsFinite(samples[i].coefficient5) || !IsFinite(samples[i].coefficient6) ||
                    !IsFinite(samples[i].coefficient7) || !IsFinite(samples[i].coefficient8))
                {
                    throw new InvalidOperationException("Doorway probe capture produced non-finite SH or occlusion data.");
                }
            }

            localPositionSignature = ComputeProbeLocalPositionSignature(ProbeLocalPositions);
            return samples;
        }

        private static Vector3 GetShCoefficient(SphericalHarmonicsL2 harmonics, int coefficient)
        {
            return new Vector3(
                harmonics[0, coefficient],
                harmonics[1, coefficient],
                harmonics[2, coefficient]);
        }

        private static string ComputeDoorLightProbeSignature(GameObject realDoor)
        {
            LightProbeGroup[] groups = realDoor.GetComponentsInChildren<LightProbeGroup>(true);
            if (groups.Length != 1 || groups[0] == null || groups[0].probePositions == null ||
                groups[0].probePositions.Length != 8)
            {
                throw new InvalidOperationException("Cannot fingerprint the required eight-point real-door LightProbeGroup.");
            }

            Vector3[] doorLocal = new Vector3[groups[0].probePositions.Length];
            for (int i = 0; i < doorLocal.Length; i++)
            {
                Vector3 world = groups[0].transform.TransformPoint(groups[0].probePositions[i]);
                doorLocal[i] = realDoor.transform.InverseTransformPoint(world);
            }
            return ComputeProbeLocalPositionSignature(doorLocal);
        }

        private static DungeonPortalReceiverResponseCapture.FixedCameraCapture[] CaptureFixedCameras(
            Transform doorway,
            int cullingMask,
            string stateFolder)
        {
            if (doorway == null)
                throw new ArgumentNullException(nameof(doorway));
            if (cullingMask != ExpectedCullingMask)
                throw new InvalidOperationException("Fixed HDR cameras require the canonical receiver+door culling union.");

            // Keep physical evidence paths below legacy Windows MAX_PATH even when the
            // receiver id is long. Camera semantics remain in cameraId/cameraPath.
            string cameraFolder = stateFolder + "/HDR";
            EnsureAssetFolder(cameraFolder);
            FixedCameraSpec[] specs = BuildFixedCameraSpecs();
            var captures = new DungeonPortalReceiverResponseCapture.FixedCameraCapture[specs.Length];
            for (int i = 0; i < specs.Length; i++)
                captures[i] = CaptureFixedCamera(doorway, cullingMask, cameraFolder, specs[i]);
            return captures;
        }

        private static FixedCameraSpec[] BuildFixedCameraSpecs()
        {
            return new[]
            {
                new FixedCameraSpec(
                    "C0_InteriorForward",
                    new Vector3(0f, 1.67f, -3f),
                    new Vector3(0f, 0f, 0f),
                    false),
                new FixedCameraSpec(
                    "C1_ExteriorReverse",
                    new Vector3(0f, 1.67f, 3f),
                    new Vector3(0f, 180f, 0f),
                    false),
                new FixedCameraSpec(
                    "C2_InteriorLeft",
                    new Vector3(-1.25f, 1.4f, -2.5f),
                    Vector3.zero,
                    true),
                new FixedCameraSpec(
                    "C3_InteriorRight",
                    new Vector3(1.25f, 1.4f, -2.5f),
                    Vector3.zero,
                    true)
            };
        }

        private static DungeonPortalReceiverResponseCapture.FixedCameraCapture CaptureFixedCamera(
            Transform doorway,
            int cullingMask,
            string cameraFolder,
            FixedCameraSpec spec)
        {
            GameObject cameraObject = null;
            RenderTexture renderTarget = null;
            Texture2D linearPixels = null;
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                cameraObject = new GameObject("__ReceiverBounceFixedHDR_" + spec.Id);
                cameraObject.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(cameraObject, doorway.gameObject.scene);
                cameraObject.transform.position = doorway.TransformPoint(spec.LocalPosition);
                if (spec.LookAtDoorwayCenter)
                {
                    Vector3 target = doorway.TransformPoint(new Vector3(0f, 1f, 0f));
                    Vector3 direction = target - cameraObject.transform.position;
                    if (direction.sqrMagnitude <= 0.000001f)
                        throw new InvalidOperationException("Fixed HDR camera has a degenerate look-at direction.");
                    cameraObject.transform.rotation = Quaternion.LookRotation(direction.normalized, doorway.up);
                }
                else
                {
                    cameraObject.transform.rotation = doorway.rotation * Quaternion.Euler(spec.LocalEulerAngles);
                }

                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 100f;
                camera.fieldOfView = FixedCameraFieldOfView;
                camera.orthographic = false;
                camera.aspect = FixedCameraWidth / (float)FixedCameraHeight;
                camera.ResetProjectionMatrix();
                camera.cullingMask = cullingMask;
                camera.renderingPath = RenderingPath.Forward;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                camera.allowDynamicResolution = false;
                camera.useOcclusionCulling = false;

                UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
                if (cameraData == null)
                    throw new InvalidOperationException("URP additional camera data is unavailable for fixed HDR capture.");
                cameraData.renderPostProcessing = false;
                cameraData.antialiasing = AntialiasingMode.None;
                cameraData.dithering = false;
                cameraData.volumeLayerMask = 0;
                if (cameraObject.GetComponent<Volume>() != null)
                    throw new InvalidOperationException("Fixed HDR camera must not carry a Volume/post-processing component.");

                renderTarget = RenderTexture.GetTemporary(
                    FixedCameraWidth,
                    FixedCameraHeight,
                    24,
                    RenderTextureFormat.ARGBHalf,
                    RenderTextureReadWrite.Linear);
                renderTarget.name = "__ReceiverBounceARGBHalf";
                renderTarget.antiAliasing = 1;
                camera.targetTexture = renderTarget;
                // Do not inherit an editor/GameView aspect: the numeric HDR target is
                // contractually 512x512. Set it again after target assignment and
                // reset the projection immediately before rendering.
                camera.aspect = FixedCameraWidth / (float)FixedCameraHeight;
                camera.ResetProjectionMatrix();
                AssertFixedCameraProjectionContract(camera);
                camera.Render();

                RenderTexture.active = renderTarget;
                linearPixels = new Texture2D(
                    FixedCameraWidth,
                    FixedCameraHeight,
                    TextureFormat.RGBAHalf,
                    false,
                    true);
                linearPixels.ReadPixels(
                    new Rect(0f, 0f, FixedCameraWidth, FixedCameraHeight),
                    0,
                    0,
                    false);
                linearPixels.Apply(false, false);
                // The render target is ARGBHalf and the readable texture is RGBAHalf.
                // Preserve half precision: the fixed-camera contract requires the
                // persisted numeric target to remain RGBAHalf rather than be promoted.
                byte[] exr = ImageConversion.EncodeToEXR(linearPixels, Texture2D.EXRFlags.None);
                if (exr == null || exr.Length == 0)
                    throw new InvalidOperationException("Fixed HDR camera did not produce EXR payload bytes.");

                string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    cameraFolder + "/" + spec.Id + ".exr");
                if (!TryGetCanonicalGeneratedAssetPath(
                        assetPath,
                        out string canonicalAssetPath,
                        out string physicalAssetPath,
                        out string pathFailure) ||
                    !string.Equals(canonicalAssetPath, assetPath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Fixed HDR output path escaped the generated PoC root: " + pathFailure);
                }
                File.WriteAllBytes(physicalAssetPath, exr);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                ConfigureLinearHdrImporter(assetPath);
                Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (imported == null || imported.width != FixedCameraWidth || imported.height != FixedCameraHeight)
                {
                    throw new InvalidOperationException(
                        "Fixed HDR EXR evidence did not import at the required 512x512 dimensions: '" +
                        assetPath + "'.");
                }

                GetCurrentRenderPipelineFingerprint(out string pipelinePath, out string pipelineHash);
                return new DungeonPortalReceiverResponseCapture.FixedCameraCapture
                {
                    cameraId = spec.Id,
                    cameraPath = "__TransientLinearHDRCamera__/" + spec.Id,
                    doorwayLocalPosition = spec.LocalPosition,
                    doorwayLocalEulerAngles = NormalizeEulerAngles(
                        (Quaternion.Inverse(doorway.rotation) * cameraObject.transform.rotation).eulerAngles),
                    fieldOfView = camera.fieldOfView,
                    nearClipPlane = camera.nearClipPlane,
                    farClipPlane = camera.farClipPlane,
                    orthographic = camera.orthographic,
                    orthographicSize = camera.orthographicSize,
                    aspect = camera.aspect,
                    clearFlags = camera.clearFlags,
                    backgroundColor = camera.backgroundColor,
                    cullingMask = camera.cullingMask,
                    renderingPath = camera.renderingPath,
                    postProcessingEnabled = cameraData.renderPostProcessing,
                    antialiasing = cameraData.antialiasing,
                    dithering = cameraData.dithering,
                    volumeLayerMask = cameraData.volumeLayerMask.value,
                    allowMsaa = camera.allowMSAA,
                    allowDynamicResolution = camera.allowDynamicResolution,
                    useOcclusionCulling = camera.useOcclusionCulling,
                    urpRendererIndex = GetUrpRendererIndex(cameraData),
                    renderPipelineAssetPath = pipelinePath,
                    renderPipelineDependencyHash = pipelineHash,
                    hdr = camera.allowHDR,
                    linearPreTonemap = true,
                    hdrColorTexture = imported,
                    hdrColorAssetPath = assetPath,
                    hdrColorDependencyHash = GetDependencyHash(assetPath),
                    width = imported.width,
                    height = imported.height,
                    textureFormat = imported.format.ToString()
                };
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (linearPixels != null)
                    UnityEngine.Object.DestroyImmediate(linearPixels);
                if (renderTarget != null)
                    RenderTexture.ReleaseTemporary(renderTarget);
                if (cameraObject != null)
                    UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static void ConfigureLinearHdrImporter(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("EXR evidence has no TextureImporter: '" + assetPath + "'.");
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static void AssertFixedCameraProjectionContract(Camera camera)
        {
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));
            float expectedAspect = FixedCameraWidth / (float)FixedCameraHeight;
            if (camera.orthographic ||
                !Approximately(camera.aspect, expectedAspect, 0.000001f) ||
                !Approximately(camera.fieldOfView, FixedCameraFieldOfView, 0.000001f) ||
                !Approximately(camera.nearClipPlane, 0.01f, 0.000001f) ||
                !Approximately(camera.farClipPlane, 100f, 0.000001f))
            {
                throw new InvalidOperationException("Fixed HDR camera aspect/projection inputs drifted before render.");
            }

            Matrix4x4 expectedProjection = Matrix4x4.Perspective(
                FixedCameraFieldOfView,
                expectedAspect,
                camera.nearClipPlane,
                camera.farClipPlane);
            Matrix4x4 actualProjection = camera.projectionMatrix;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (!Approximately(actualProjection[row, column], expectedProjection[row, column], 0.0001f))
                    {
                        throw new InvalidOperationException(
                            "Fixed HDR camera projection matrix is not the reset 512x512 perspective contract.");
                    }
                }
            }
        }

        private static int GetUrpRendererIndex(UniversalAdditionalCameraData data)
        {
            if (data == null)
                return -1;
            var serialized = new SerializedObject(data);
            SerializedProperty property = serialized.FindProperty("m_RendererIndex");
            return property != null ? property.intValue : -1;
        }

        private static void GetCurrentRenderPipelineFingerprint(out string path, out string hash)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
                pipeline = GraphicsSettings.defaultRenderPipeline;
            if (pipeline == null)
                throw new InvalidOperationException("Fixed HDR capture requires a persisted render pipeline asset.");

            path = AssetDatabase.GetAssetPath(pipeline);
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("The active render pipeline is not a persisted project asset.");
            hash = GetDependencyHash(path);
        }

        private static DungeonPortalReceiverResponseCapture.CaptureState GetState(
            string stateName,
            DungeonPortalReceiverResponseCapture.CaptureState baseline,
            DungeonPortalReceiverResponseCapture.CaptureState directOnly,
            DungeonPortalReceiverResponseCapture.CaptureState full)
        {
            if (string.Equals(stateName, DungeonPortalReceiverResponseCapture.BaselineStateName, StringComparison.Ordinal))
                return baseline;
            if (string.Equals(stateName, DungeonPortalReceiverResponseCapture.DirectOnlyStateName, StringComparison.Ordinal))
                return directOnly;
            if (string.Equals(stateName, DungeonPortalReceiverResponseCapture.FullStateName, StringComparison.Ordinal))
                return full;
            throw new ArgumentOutOfRangeException(nameof(stateName), stateName, "Unknown canonical bake state.");
        }

        private static void SetState(
            ref DungeonPortalReceiverResponseCapture.CaptureState baseline,
            ref DungeonPortalReceiverResponseCapture.CaptureState directOnly,
            ref DungeonPortalReceiverResponseCapture.CaptureState full,
            DungeonPortalReceiverResponseCapture.CaptureState state)
        {
            if (string.Equals(state.stateName, DungeonPortalReceiverResponseCapture.BaselineStateName, StringComparison.Ordinal))
            {
                baseline = state;
                return;
            }
            if (string.Equals(state.stateName, DungeonPortalReceiverResponseCapture.DirectOnlyStateName, StringComparison.Ordinal))
            {
                directOnly = state;
                return;
            }
            if (string.Equals(state.stateName, DungeonPortalReceiverResponseCapture.FullStateName, StringComparison.Ordinal))
            {
                full = state;
                return;
            }
            throw new InvalidOperationException("Cannot checkpoint an unknown capture state.");
        }

        private static int ResolveExpectedRendererCountForState(
            ReceiverSpec receiver,
            BakeStateSpec stateSpec,
            DungeonPortalReceiverResponseCapture.CaptureState baseline,
            DungeonPortalReceiverResponseCapture.CaptureState current)
        {
            if (receiver.ExpectedCapturedRendererCount > 0)
                return receiver.ExpectedCapturedRendererCount;

            if (string.Equals(
                    stateSpec.Name,
                    DungeonPortalReceiverResponseCapture.BaselineStateName,
                    StringComparison.Ordinal))
            {
                return current.renderers != null ? current.renderers.Length : 0;
            }

            int baselineCount = baseline.renderers != null ? baseline.renderers.Length : 0;
            if (!baseline.captured || baselineCount <= 0)
            {
                throw new InvalidOperationException(
                    "A baseline-authoritative receiver cannot capture or resume '" + stateSpec.Name +
                    "' before a usable Baseline renderer layout exists.");
            }

            return baselineCount;
        }

        private static int ResolveExpectedLightmapCountForState(
            ReceiverSpec receiver,
            BakeStateSpec stateSpec,
            DungeonPortalReceiverResponseCapture.CaptureState baseline,
            DungeonPortalReceiverResponseCapture.CaptureState current)
        {
            if (receiver.ExpectedCapturedLightmapCount > 0)
                return receiver.ExpectedCapturedLightmapCount;

            if (string.Equals(
                    stateSpec.Name,
                    DungeonPortalReceiverResponseCapture.BaselineStateName,
                    StringComparison.Ordinal))
            {
                return current.lightmaps != null ? current.lightmaps.Length : 0;
            }

            int baselineCount = baseline.lightmaps != null ? baseline.lightmaps.Length : 0;
            if (!baseline.captured || baselineCount <= 0)
            {
                throw new InvalidOperationException(
                    "A baseline-authoritative receiver cannot capture or resume '" + stateSpec.Name +
                    "' before a usable Baseline lightmap layout exists.");
            }

            return baselineCount;
        }

        private static bool IsUsableCheckpointState(
            DungeonPortalReceiverResponseCapture.CaptureState state,
            BakeStateSpec expected,
            ReceiverSpec receiver,
            int expectedLightmapCount,
            int expectedRendererCount)
        {
            int resolvedRendererCount = expectedRendererCount > 0
                ? expectedRendererCount
                : (state.renderers != null ? state.renderers.Length : 0);
            int resolvedLightmapCount = expectedLightmapCount > 0
                ? expectedLightmapCount
                : (state.lightmaps != null ? state.lightmaps.Length : 0);
            if (!state.captured || !string.Equals(state.stateName, expected.Name, StringComparison.Ordinal) ||
                state.lightmapsMode != LightmapsMode.CombinedDirectional ||
                string.IsNullOrWhiteSpace(state.stateHash) ||
                string.IsNullOrWhiteSpace(state.stateFolderPath) || !IsGeneratedPath(state.stateFolderPath) ||
                resolvedLightmapCount <= 0 ||
                state.lightmaps == null || state.lightmaps.Length != resolvedLightmapCount ||
                resolvedRendererCount <= 0 ||
                state.renderers == null || state.renderers.Length != resolvedRendererCount ||
                string.IsNullOrWhiteSpace(state.fullRendererParitySignature) ||
                state.fullRendererCount <= resolvedRendererCount ||
                state.fullRendererComponentCount != state.fullRendererCount ||
                !state.materialPropertyBlocksVerifiedEmpty ||
                state.fullRendererInventory == null || state.fullRendererInventory.Length != state.fullRendererCount ||
                string.IsNullOrWhiteSpace(state.lightingSettingsCloneDependencyHashBeforeBake) ||
                string.IsNullOrWhiteSpace(state.lightingSettingsCloneDependencyHashAfterBake) ||
                state.probes == null || state.probes.Length != ProbeLocalPositions.Length ||
                state.fixedCameraCaptures == null || state.fixedCameraCaptures.Length != 4 ||
                state.doorLightProbeCount != 8 ||
                string.IsNullOrWhiteSpace(state.probeLocalPositionSignature) ||
                string.IsNullOrWhiteSpace(state.rendererLayoutSignature) ||
                string.IsNullOrWhiteSpace(state.doorLightProbeLocalPositionSignature))
            {
                return false;
            }

            if (!string.Equals(
                    state.lightingSettingsCloneDependencyHashBeforeBake,
                    state.lightingSettingsCloneDependencyHashAfterBake,
                    StringComparison.Ordinal))
            {
                return false;
            }

            DungeonPortalReceiverResponseCapture.InjectorProvenance injector = state.injector;
            if (injector.enabled != expected.Enabled || injector.type != LightType.Spot ||
                injector.lightmapBakeType != LightmapBakeType.Baked || !Approximately(injector.color, Color.white) ||
                !Approximately(injector.intensity, 1f, 0.00001f) ||
                !Approximately(injector.bounceIntensity, expected.BounceIntensity, 0.00001f) ||
                !Approximately(injector.localPosition, new Vector3(0f, 1f, 0.5f), 0.00001f) ||
                !Approximately(NormalizeEulerAngles(injector.localEulerAngles), new Vector3(0f, 180f, 0f), 0.00001f) ||
                !string.Equals(injector.placementInterpretation, ReceiverIncomingPlacementInterpretation, StringComparison.Ordinal) ||
                !Approximately(injector.range, receiver.ExpectedInjectorRange, 0.0005f) ||
                !Approximately(injector.spotAngle, CanonicalSpotAngle, 0.0001f) ||
                !Approximately(injector.innerSpotAngle, CanonicalSpotAngle, 0.0001f) ||
                injector.shadows != LightShadows.Soft || !Approximately(injector.shadowStrength, 1f, 0.00001f) ||
                injector.cullingMask != ExpectedCullingMask ||
                injector.renderingLayerMask != RequiredDungeonRenderingLayerMask ||
                injector.shadowRenderingLayerMask != RequiredDungeonRenderingLayerMask)
            {
                return false;
            }

            for (int i = 0; i < state.lightmaps.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.CaptureLightmap lightmap = state.lightmaps[i];
                if (lightmap.sourceLightmapIndex < 0 || (i > 0 &&
                    lightmap.sourceLightmapIndex <= state.lightmaps[i - 1].sourceLightmapIndex) ||
                    !IsStableTexture(lightmap.colorTexture, lightmap.colorAssetPath, lightmap.colorDependencyHash) ||
                    !IsStableTexture(lightmap.directionTexture, lightmap.directionAssetPath, lightmap.directionDependencyHash) ||
                    lightmap.colorWidth <= 0 || lightmap.colorHeight <= 0 ||
                    lightmap.directionWidth <= 0 || lightmap.directionHeight <= 0 ||
                    string.IsNullOrWhiteSpace(lightmap.colorFormat) ||
                    string.IsNullOrWhiteSpace(lightmap.directionFormat) ||
                    lightmap.hasShadowMask != (lightmap.shadowMaskTexture != null) ||
                    (lightmap.hasShadowMask &&
                     (!IsStableTexture(
                         lightmap.shadowMaskTexture,
                         lightmap.shadowMaskAssetPath,
                         lightmap.shadowMaskDependencyHash) ||
                      lightmap.shadowMaskWidth <= 0 || lightmap.shadowMaskHeight <= 0 ||
                      string.IsNullOrWhiteSpace(lightmap.shadowMaskFormat))))
                {
                    return false;
                }
            }

            for (int i = 0; i < state.renderers.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.CaptureRenderer renderer = state.renderers[i];
                if (string.IsNullOrWhiteSpace(renderer.relativePath) || renderer.rendererBucketIndex < 0 ||
                    renderer.componentOrdinal < 0 || string.IsNullOrWhiteSpace(renderer.meshAssetGuid) ||
                    renderer.meshLocalId == 0L || string.IsNullOrWhiteSpace(renderer.meshUv2Hash) ||
                    renderer.lightmapIndex < 0 || !IsFinite(renderer.lightmapScaleOffset))
                {
                    return false;
                }
            }

            var fullRendererBuckets = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < state.fullRendererInventory.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry entry =
                    state.fullRendererInventory[i];
                if (string.IsNullOrWhiteSpace(entry.scope) ||
                    string.IsNullOrWhiteSpace(entry.relativePath) ||
                    string.IsNullOrWhiteSpace(entry.rendererType) ||
                    entry.componentOrdinal < 0 ||
                    string.IsNullOrWhiteSpace(entry.canonicalBucket) ||
                    string.IsNullOrWhiteSpace(entry.parityMetadataHash) ||
                    !fullRendererBuckets.Add(entry.canonicalBucket))
                {
                    return false;
                }
            }
            if (!string.Equals(
                    state.fullRendererParitySignature,
                    ComputeFullRendererParitySignature(state.fullRendererInventory),
                    StringComparison.Ordinal))
            {
                return false;
            }

            for (int i = 0; i < state.probes.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.ProbeSample probe = state.probes[i];
                if (probe.probeIndex != i ||
                    !Approximately(probe.localPosition, ProbeLocalPositions[i], 0.00001f) ||
                    !IsFinite(probe.worldPosition) || !IsFinite(probe.occlusion) ||
                    !IsFinite(probe.coefficient0) || !IsFinite(probe.coefficient1) ||
                    !IsFinite(probe.coefficient2) || !IsFinite(probe.coefficient3) ||
                    !IsFinite(probe.coefficient4) || !IsFinite(probe.coefficient5) ||
                    !IsFinite(probe.coefficient6) || !IsFinite(probe.coefficient7) ||
                    !IsFinite(probe.coefficient8))
                {
                    return false;
                }
            }

            // The full renderer signature contains editor-only parity fields that are
            // intentionally not duplicated into CaptureRenderer. Its integrity is
            // covered by stateHash; recomputing only the compact mapping here would
            // incorrectly reject every valid checkpoint.
            if (!string.Equals(
                    state.probeLocalPositionSignature,
                    ComputeProbeLocalPositionSignature(ProbeLocalPositions),
                    StringComparison.Ordinal))
            {
                return false;
            }

            for (int i = 0; i < state.fixedCameraCaptures.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.FixedCameraCapture camera = state.fixedCameraCaptures[i];
                if (!IsUsableFixedCamera(camera, i))
                    return false;
            }

            return string.Equals(state.stateHash, ComputeStateHash(state), StringComparison.Ordinal);
        }

        private static bool IsStableTexture(Texture2D texture, string assetPath, string dependencyHash)
        {
            return texture != null && IsGeneratedPath(assetPath) &&
                   AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) == texture &&
                   string.Equals(GetDependencyHash(assetPath), dependencyHash, StringComparison.Ordinal);
        }

        private static bool IsUsableFixedCamera(
            DungeonPortalReceiverResponseCapture.FixedCameraCapture camera,
            int index)
        {
            FixedCameraSpec[] specs = BuildFixedCameraSpecs();
            if (index < 0 || index >= specs.Length ||
                !string.Equals(camera.cameraId, specs[index].Id, StringComparison.Ordinal) ||
                !string.Equals(camera.cameraPath, "__TransientLinearHDRCamera__/" + specs[index].Id, StringComparison.Ordinal) ||
                !Approximately(camera.doorwayLocalPosition, specs[index].LocalPosition, 0.00001f) ||
                !Approximately(camera.fieldOfView, FixedCameraFieldOfView, 0.00001f) ||
                !Approximately(camera.nearClipPlane, 0.01f, 0.00001f) ||
                !Approximately(camera.farClipPlane, 100f, 0.00001f) ||
                camera.orthographic || !Approximately(
                    camera.aspect,
                    FixedCameraWidth / (float)FixedCameraHeight,
                    0.00001f) ||
                camera.clearFlags != CameraClearFlags.SolidColor || !Approximately(camera.backgroundColor, Color.black) ||
                camera.cullingMask != ExpectedCullingMask || camera.renderingPath != RenderingPath.Forward ||
                camera.postProcessingEnabled || camera.antialiasing != AntialiasingMode.None || camera.dithering ||
                camera.volumeLayerMask != 0 || camera.allowMsaa || camera.allowDynamicResolution ||
                camera.useOcclusionCulling || !camera.hdr || !camera.linearPreTonemap ||
                camera.width != FixedCameraWidth || camera.height != FixedCameraHeight ||
                !IsStableTexture(camera.hdrColorTexture, camera.hdrColorAssetPath, camera.hdrColorDependencyHash) ||
                !string.Equals(camera.textureFormat, TextureFormat.RGBAHalf.ToString(), StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(camera.renderPipelineAssetPath) ||
                string.IsNullOrWhiteSpace(camera.renderPipelineDependencyHash))
            {
                return false;
            }

            try
            {
                GetCurrentRenderPipelineFingerprint(out string currentPath, out string currentHash);
                return string.Equals(camera.renderPipelineAssetPath, currentPath, StringComparison.Ordinal) &&
                       string.Equals(camera.renderPipelineDependencyHash, currentHash, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static DungeonPortalReceiverResponseCapture.CaptureLightmap[] ExtractLightmaps(
            DungeonPortalReceiverResponseCapture.CaptureLightmap[] lightmaps)
        {
            return lightmaps != null ? (DungeonPortalReceiverResponseCapture.CaptureLightmap[])lightmaps.Clone() :
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureLightmap>();
        }

        private static DungeonPortalReceiverResponseCapture.CaptureRenderer[] ExtractRenderers(
            RendererCapture[] captures)
        {
            if (captures == null)
                return Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            var result = new DungeonPortalReceiverResponseCapture.CaptureRenderer[captures.Length];
            for (int i = 0; i < captures.Length; i++)
                result[i] = captures[i].Data;
            return result;
        }

        private static string ComputeRendererLayoutSignature(RendererCapture[] captures)
        {
            if (captures == null)
                return ComputeRendererLayoutSignature(Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>());

            // The serialized CaptureRenderer mapping stays compact. This signature also
            // includes every capture-time visual/material parity field so an otherwise
            // matching UV layout cannot mask a different receiver surface.
            var builder = new StringBuilder(captures.Length * 256);
            for (int i = 0; i < captures.Length; i++)
            {
                AppendRenderer(builder, captures[i].Data);
                AppendString(builder, captures[i].typeName);
                AppendString(builder, captures[i].parityMetadata);
            }
            return ComputeSha256(builder.ToString());
        }

        private static string ComputeRendererLayoutSignature(
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] renderers)
        {
            var builder = new StringBuilder();
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                    AppendRenderer(builder, renderers[i]);
            }
            return ComputeSha256(builder.ToString());
        }

        private static string ComputeProbeLocalPositionSignature(Vector3[] positions)
        {
            var builder = new StringBuilder();
            if (positions != null)
            {
                for (int i = 0; i < positions.Length; i++)
                    AppendVector3(builder, positions[i]);
            }
            return ComputeSha256(builder.ToString());
        }

        private static string ComputeStateHash(DungeonPortalReceiverResponseCapture.CaptureState state)
        {
            var builder = new StringBuilder(8192);
            AppendString(builder, state.stateName);
            AppendString(builder, state.capturedUtcIso8601);
            AppendString(builder, state.stateFolderPath);
            AppendDouble(builder, state.elapsedSeconds);
            builder.Append((int)state.lightmapsMode).Append('|');
            AppendInjector(builder, state.injector);
            AppendString(builder, state.probeLocalPositionSignature);
            AppendString(builder, state.rendererLayoutSignature);
            AppendString(builder, state.fullRendererParitySignature);
            builder.Append(state.fullRendererCount).Append('|');
            builder.Append(state.fullRendererComponentCount).Append('|');
            builder.Append(state.materialPropertyBlocksVerifiedEmpty ? '1' : '0').Append('|');
            AppendString(builder, state.lightingSettingsCloneDependencyHashBeforeBake);
            AppendString(builder, state.lightingSettingsCloneDependencyHashAfterBake);
            builder.Append(state.doorLightProbeCount).Append('|');
            AppendString(builder, state.doorLightProbeLocalPositionSignature);

            DungeonPortalReceiverResponseCapture.CaptureLightmap[] lightmaps = state.lightmaps ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            for (int i = 0; i < lightmaps.Length; i++)
                AppendLightmap(builder, lightmaps[i]);

            DungeonPortalReceiverResponseCapture.CaptureRenderer[] renderers = state.renderers ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            for (int i = 0; i < renderers.Length; i++)
                AppendRenderer(builder, renderers[i]);

            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] fullInventory =
                state.fullRendererInventory ?? Array.Empty<DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry>();
            for (int i = 0; i < fullInventory.Length; i++)
                AppendFullRendererInventoryEntry(builder, fullInventory[i]);

            DungeonPortalReceiverResponseCapture.ProbeSample[] probes = state.probes ??
                Array.Empty<DungeonPortalReceiverResponseCapture.ProbeSample>();
            for (int i = 0; i < probes.Length; i++)
                AppendProbe(builder, probes[i]);

            DungeonPortalReceiverResponseCapture.FixedCameraCapture[] cameras = state.fixedCameraCaptures ??
                Array.Empty<DungeonPortalReceiverResponseCapture.FixedCameraCapture>();
            for (int i = 0; i < cameras.Length; i++)
                AppendCamera(builder, cameras[i]);

            return ComputeSha256(builder.ToString());
        }

        private static Vector3[] BuildProbeLocalPositions()
        {
            float[] xs = { -0.4f, 0f, 0.4f };
            float[] ys = { 0.25f, 1f, 1.75f };
            float[] zs = { -0.25f, -1.5f, -3f };
            var positions = new List<Vector3>(27);
            for (int z = 0; z < zs.Length; z++)
            {
                for (int y = 0; y < ys.Length; y++)
                {
                    for (int x = 0; x < xs.Length; x++)
                        positions.Add(new Vector3(xs[x], ys[y], zs[z]));
                }
            }

            if (positions.Count != 27)
                throw new InvalidOperationException("The doorway-local probe stencil must contain exactly 27 samples.");
            return positions.ToArray();
        }

        private static void AppendInjector(
            StringBuilder builder,
            DungeonPortalReceiverResponseCapture.InjectorProvenance injector)
        {
            builder.Append(injector.enabled ? '1' : '0').Append('|');
            builder.Append((int)injector.type).Append('|');
            builder.Append((int)injector.lightmapBakeType).Append('|');
            AppendColor(builder, injector.color);
            AppendFloat(builder, injector.intensity);
            AppendFloat(builder, injector.bounceIntensity);
            AppendVector3(builder, injector.localPosition);
            AppendVector3(builder, NormalizeEulerAngles(injector.localEulerAngles));
            AppendString(builder, injector.placementInterpretation);
            AppendFloat(builder, injector.range);
            AppendFloat(builder, injector.spotAngle);
            AppendFloat(builder, injector.innerSpotAngle);
            builder.Append((int)injector.shadows).Append('|');
            AppendFloat(builder, injector.shadowStrength);
            builder.Append(injector.cullingMask).Append('|');
            builder.Append(injector.renderingLayerMask).Append('|');
            builder.Append(injector.shadowRenderingLayerMask).Append('|');
        }

        private static void AppendLightmap(
            StringBuilder builder,
            DungeonPortalReceiverResponseCapture.CaptureLightmap lightmap)
        {
            builder.Append(lightmap.sourceLightmapIndex).Append('|');
            AppendString(builder, lightmap.colorAssetPath);
            AppendString(builder, lightmap.colorDependencyHash);
            builder.Append(lightmap.colorWidth).Append('|').Append(lightmap.colorHeight).Append('|');
            AppendString(builder, lightmap.colorFormat);
            AppendString(builder, lightmap.directionAssetPath);
            AppendString(builder, lightmap.directionDependencyHash);
            builder.Append(lightmap.directionWidth).Append('|').Append(lightmap.directionHeight).Append('|');
            AppendString(builder, lightmap.directionFormat);
            builder.Append(lightmap.hasShadowMask ? '1' : '0').Append('|');
            AppendString(builder, lightmap.shadowMaskAssetPath);
            AppendString(builder, lightmap.shadowMaskDependencyHash);
            builder.Append(lightmap.shadowMaskWidth).Append('|').Append(lightmap.shadowMaskHeight).Append('|');
            AppendString(builder, lightmap.shadowMaskFormat);
        }

        private static void AppendRenderer(
            StringBuilder builder,
            DungeonPortalReceiverResponseCapture.CaptureRenderer renderer)
        {
            AppendString(builder, renderer.relativePath);
            builder.Append(renderer.rendererBucketIndex).Append('|');
            builder.Append(renderer.componentOrdinal).Append('|');
            AppendString(builder, renderer.meshAssetGuid);
            builder.Append(renderer.meshLocalId).Append('|');
            AppendString(builder, renderer.meshUv2Hash);
            builder.Append(renderer.lightmapIndex).Append('|');
            AppendVector4(builder, renderer.lightmapScaleOffset);
        }

        private static void AppendFullRendererInventoryEntry(
            StringBuilder builder,
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry entry)
        {
            AppendString(builder, entry.scope);
            AppendString(builder, entry.relativePath);
            AppendString(builder, entry.rendererType);
            builder.Append(entry.componentOrdinal).Append('|');
            AppendString(builder, entry.canonicalBucket);
            AppendString(builder, entry.parityMetadataHash);
        }

        private static void AppendProbe(
            StringBuilder builder,
            DungeonPortalReceiverResponseCapture.ProbeSample probe)
        {
            builder.Append(probe.probeIndex).Append('|');
            AppendVector3(builder, probe.localPosition);
            AppendVector3(builder, probe.worldPosition);
            AppendVector3(builder, probe.coefficient0);
            AppendVector3(builder, probe.coefficient1);
            AppendVector3(builder, probe.coefficient2);
            AppendVector3(builder, probe.coefficient3);
            AppendVector3(builder, probe.coefficient4);
            AppendVector3(builder, probe.coefficient5);
            AppendVector3(builder, probe.coefficient6);
            AppendVector3(builder, probe.coefficient7);
            AppendVector3(builder, probe.coefficient8);
            AppendVector4(builder, probe.occlusion);
        }

        private static void AppendCamera(
            StringBuilder builder,
            DungeonPortalReceiverResponseCapture.FixedCameraCapture camera)
        {
            AppendString(builder, camera.cameraId);
            AppendString(builder, camera.cameraPath);
            AppendVector3(builder, camera.doorwayLocalPosition);
            AppendVector3(builder, NormalizeEulerAngles(camera.doorwayLocalEulerAngles));
            AppendFloat(builder, camera.fieldOfView);
            AppendFloat(builder, camera.nearClipPlane);
            AppendFloat(builder, camera.farClipPlane);
            builder.Append(camera.orthographic ? '1' : '0').Append('|');
            AppendFloat(builder, camera.orthographicSize);
            AppendFloat(builder, camera.aspect);
            builder.Append((int)camera.clearFlags).Append('|');
            AppendColor(builder, camera.backgroundColor);
            builder.Append(camera.cullingMask).Append('|');
            builder.Append((int)camera.renderingPath).Append('|');
            builder.Append(camera.postProcessingEnabled ? '1' : '0').Append('|');
            builder.Append((int)camera.antialiasing).Append('|');
            builder.Append(camera.dithering ? '1' : '0').Append('|');
            builder.Append(camera.volumeLayerMask).Append('|');
            builder.Append(camera.allowMsaa ? '1' : '0').Append('|');
            builder.Append(camera.allowDynamicResolution ? '1' : '0').Append('|');
            builder.Append(camera.useOcclusionCulling ? '1' : '0').Append('|');
            builder.Append(camera.urpRendererIndex).Append('|');
            AppendString(builder, camera.renderPipelineAssetPath);
            AppendString(builder, camera.renderPipelineDependencyHash);
            builder.Append(camera.hdr ? '1' : '0').Append('|');
            builder.Append(camera.linearPreTonemap ? '1' : '0').Append('|');
            AppendString(builder, camera.hdrColorAssetPath);
            AppendString(builder, camera.hdrColorDependencyHash);
            builder.Append(camera.width).Append('|').Append(camera.height).Append('|');
            AppendString(builder, camera.textureFormat);
        }

        private static void AppendString(StringBuilder builder, string value)
        {
            string safe = value ?? string.Empty;
            builder.Append(safe.Length).Append(':').Append(safe).Append('|');
        }

        private static void AppendDouble(StringBuilder builder, double value)
        {
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        private static void AppendFloat(StringBuilder builder, float value)
        {
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        private static void AppendVector3(StringBuilder builder, Vector3 value)
        {
            AppendFloat(builder, value.x);
            AppendFloat(builder, value.y);
            AppendFloat(builder, value.z);
        }

        private static void AppendVector4(StringBuilder builder, Vector4 value)
        {
            AppendFloat(builder, value.x);
            AppendFloat(builder, value.y);
            AppendFloat(builder, value.z);
            AppendFloat(builder, value.w);
        }

        private static void AppendColor(StringBuilder builder, Color value)
        {
            AppendFloat(builder, value.r);
            AppendFloat(builder, value.g);
            AppendFloat(builder, value.b);
            AppendFloat(builder, value.a);
        }

        private static string ComputeSha256(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            using (SHA256 hasher = SHA256.Create())
            {
                byte[] hash = hasher.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static string GetStateRootFolder(ReceiverSpec spec, string stateName)
        {
            string safeStateName = string.Equals(stateName, DungeonPortalReceiverResponseCapture.BaselineStateName, StringComparison.Ordinal) ||
                                   string.Equals(stateName, DungeonPortalReceiverResponseCapture.DirectOnlyStateName, StringComparison.Ordinal) ||
                                   string.Equals(stateName, DungeonPortalReceiverResponseCapture.FullStateName, StringComparison.Ordinal)
                ? stateName
                : throw new InvalidOperationException("State artifact requested for a non-canonical state.");
            return GetReceiverFolder(spec) + "/States/" + safeStateName;
        }

        private static string GetReceiverFolder(ReceiverSpec spec)
        {
            return GeneratedRoot + "/" + spec.RoomId;
        }

        private static string GetCaptureAssetPath(ReceiverSpec spec)
        {
            return GetReceiverFolder(spec) + "/" + spec.RoomId + "_ReceiverResponseCapture.asset";
        }

        private static string GetWorkspacePath(ReceiverSpec spec)
        {
            return WorkspacesFolder + "/" + spec.RoomId + "_ReceiverBounceWorkspace.unity";
        }

        private static string GetLightingSettingsClonePath(ReceiverSpec spec)
        {
            return LightingSettingsFolder + "/" + spec.RoomId + "_ReceiverBounce.lighting";
        }

        private static string GetRoomInstanceName(ReceiverSpec spec)
        {
            return "ReceiverBounce_" + spec.RoomId + "_ProductionInstance";
        }

        private static bool IsGeneratedPath(string assetPath)
        {
            return TryGetCanonicalGeneratedAssetPath(assetPath, out _, out _, out _);
        }

        private static bool TryGetCanonicalGeneratedAssetPath(
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

            string normalizedInput = assetPath.Replace('\\', '/');
            string[] segments = normalizedInput.Split('/');
            if (segments.Length < 2 || !string.Equals(segments[0], "Assets", StringComparison.Ordinal))
            {
                error = "path is not an Assets-relative path";
                return false;
            }
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(segments[i]) ||
                    string.Equals(segments[i], ".", StringComparison.Ordinal) ||
                    string.Equals(segments[i], "..", StringComparison.Ordinal))
                {
                    error = "path has an empty or dot segment";
                    return false;
                }
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string assetsRoot = Path.GetFullPath(Application.dataPath);
            string generatedRoot = Path.GetFullPath(
                Path.Combine(projectRoot, GeneratedRoot.Replace('/', Path.DirectorySeparatorChar)));
            string candidate = Path.GetFullPath(
                Path.Combine(projectRoot, normalizedInput.Replace('/', Path.DirectorySeparatorChar)));
            string generatedRootWithSeparator = generatedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                                Path.DirectorySeparatorChar;
            if (!string.Equals(candidate, generatedRoot, StringComparison.OrdinalIgnoreCase) &&
                !candidate.StartsWith(generatedRootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                error = "path escapes the physical Generated/Bounce root";
                return false;
            }

            string assetsRootWithSeparator = assetsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                             Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(assetsRootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                error = "canonical path is not below the project Assets directory";
                return false;
            }

            string relativeToAssets = candidate.Substring(assetsRootWithSeparator.Length)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            canonicalAssetPath = "Assets/" + relativeToAssets;
            physicalPath = candidate;
            return true;
        }

        private static string GetDependencyHash(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                throw new InvalidOperationException("Cannot hash a missing asset: '" + assetPath + "'.");
            return AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
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
            string created = AssetDatabase.CreateFolder(parent, name);
            if (string.IsNullOrWhiteSpace(created) && !AssetDatabase.IsValidFolder(assetFolder))
                throw new InvalidOperationException("Unable to create PoC asset folder: '" + assetFolder + "'.");
        }

        private static void AssertNoOrphanedStateArtifacts(ReceiverSpec spec)
        {
            string statesRoot = GetReceiverFolder(spec) + "/States";
            if (!TryGetCanonicalGeneratedAssetPath(statesRoot, out _, out string physicalStatesRoot, out string error))
            {
                throw new InvalidOperationException("Cannot inspect state-artifact ownership root: " + error);
            }
            if (!Directory.Exists(physicalStatesRoot))
                return;

            var manifests = new List<string>();
            manifests.AddRange(Directory.GetFiles(
                physicalStatesRoot,
                StateArtifactOwnershipManifestPrefix + "*.txt",
                SearchOption.AllDirectories));
            // A previous tool build may have failed before deleting its longer manifest.
            // Continue to fail closed on those artifacts after shortening new filenames.
            manifests.AddRange(Directory.GetFiles(
                physicalStatesRoot,
                LegacyStateArtifactOwnershipManifestPrefix + "*.txt",
                SearchOption.AllDirectories));
            if (manifests.Count == 0)
                return;

            throw new InvalidOperationException(
                "Found an uncommitted receiver-state artifact ownership manifest. It is intentionally isolated " +
                "and cannot become resume input: '" + manifests[0] + "'. Resolve or archive it manually.");
        }

        private static void AssertSoleWorkspaceScene(Scene workspace, string workspacePath, string operation)
        {
            if (!workspace.IsValid() || !workspace.isLoaded)
                throw new InvalidOperationException("Workspace is not loaded for " + operation + ".");
            if (SceneManager.sceneCount != 1)
            {
                throw new InvalidOperationException(
                    "Refusing " + operation + " because the workspace is not the sole loaded scene. sceneCount=" +
                    SceneManager.sceneCount + ".");
            }
            Scene onlyScene = SceneManager.GetSceneAt(0);
            if (onlyScene != workspace)
                throw new InvalidOperationException("The sole loaded scene is not the expected workspace for " + operation + ".");
            if (!string.IsNullOrWhiteSpace(workspace.path) &&
                !string.Equals(workspace.path, workspacePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Workspace scene path drifted for " + operation + ". expected='" + workspacePath +
                    "' actual='" + workspace.path + "'.");
            }
        }

        private static GameObject FindUniqueChildObject(Transform root, string expectedName)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            GameObject result = null;
            int count = 0;
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && string.Equals(transforms[i].name, expectedName, StringComparison.Ordinal))
                {
                    result = transforms[i].gameObject;
                    count++;
                }
            }
            if (count != 1)
                throw new InvalidOperationException("Expected exactly one child named '" + expectedName + "', found " + count + ".");
            return result;
        }

        private static bool IsInSubtree(Transform candidate, Transform subtreeRoot)
        {
            return subtreeRoot != null && candidate != null &&
                   (candidate == subtreeRoot || candidate.IsChildOf(subtreeRoot));
        }

        private static Vector3 NormalizeEulerAngles(Vector3 angles)
        {
            return new Vector3(
                Mathf.Repeat(angles.x, 360f),
                Mathf.Repeat(angles.y, 360f),
                Mathf.Repeat(angles.z, 360f));
        }

        private static bool Approximately(float left, float right, float tolerance)
        {
            return Mathf.Abs(left - right) <= tolerance;
        }

        private static bool Approximately(Vector3 left, Vector3 right, float tolerance)
        {
            return (left - right).sqrMagnitude <= tolerance * tolerance;
        }

        private static bool Approximately(Color left, Color right)
        {
            return Approximately(left.r, right.r, 0.00001f) &&
                   Approximately(left.g, right.g, 0.00001f) &&
                   Approximately(left.b, right.b, 0.00001f) &&
                   Approximately(left.a, right.a, 0.00001f);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Vector4 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool IsPass(string result)
        {
            return !string.IsNullOrEmpty(result) && result.StartsWith("PASS", StringComparison.Ordinal);
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
            public ReceiverSpec(
                string roomId,
                string roomPrefabPath,
                float expectedInjectorRange,
                int expectedCapturedLightmapCount,
                int expectedCapturedRendererCount)
            {
                RoomId = roomId;
                RoomPrefabPath = roomPrefabPath;
                ExpectedInjectorRange = expectedInjectorRange;
                ExpectedCapturedLightmapCount = expectedCapturedLightmapCount;
                ExpectedCapturedRendererCount = expectedCapturedRendererCount;
            }

            public string RoomId { get; }
            public string RoomPrefabPath { get; }
            public float ExpectedInjectorRange { get; }
            public int ExpectedCapturedLightmapCount { get; }
            public int ExpectedCapturedRendererCount { get; }
        }

        private readonly struct BakeStateSpec
        {
            public BakeStateSpec(string name, bool enabled, float bounceIntensity)
            {
                Name = name;
                Enabled = enabled;
                BounceIntensity = bounceIntensity;
            }

            public string Name { get; }
            public bool Enabled { get; }
            public float BounceIntensity { get; }
        }

        private readonly struct EmissionMaterialVariant
        {
            public EmissionMaterialVariant(string sourcePath, string p100Path, string p0Path)
            {
                SourcePath = sourcePath;
                P100Path = p100Path;
                P0Path = p0Path;
            }

            public string SourcePath { get; }
            public string P100Path { get; }
            public string P0Path { get; }
        }

        private readonly struct GiFlagWorkItem
        {
            public GiFlagWorkItem(Transform transform, bool insideExcludedSubtree)
            {
                Transform = transform;
                InsideExcludedSubtree = insideExcludedSubtree;
            }

            public Transform Transform { get; }
            public bool InsideExcludedSubtree { get; }
        }

        private readonly struct FixedCameraSpec
        {
            public FixedCameraSpec(string id, Vector3 localPosition, Vector3 localEulerAngles, bool lookAtDoorwayCenter)
            {
                Id = id;
                LocalPosition = localPosition;
                LocalEulerAngles = localEulerAngles;
                LookAtDoorwayCenter = lookAtDoorwayCenter;
            }

            public string Id { get; }
            public Vector3 LocalPosition { get; }
            public Vector3 LocalEulerAngles { get; }
            public bool LookAtDoorwayCenter { get; }
        }

        private readonly struct WorkspaceCheckpointFingerprint
        {
            public WorkspaceCheckpointFingerprint(
                string dependencyHash,
                string setupSignature,
                string fullRendererSignature,
                int fullRendererCount,
                int fullRendererComponentCount,
                bool materialPropertyBlocksVerifiedEmpty)
            {
                DependencyHash = dependencyHash ?? string.Empty;
                SetupSignature = setupSignature ?? string.Empty;
                FullRendererSignature = fullRendererSignature ?? string.Empty;
                FullRendererCount = fullRendererCount;
                FullRendererComponentCount = fullRendererComponentCount;
                MaterialPropertyBlocksVerifiedEmpty = materialPropertyBlocksVerifiedEmpty;
            }

            public string DependencyHash { get; }
            public string SetupSignature { get; }
            public string FullRendererSignature { get; }
            public int FullRendererCount { get; }
            public int FullRendererComponentCount { get; }
            public bool MaterialPropertyBlocksVerifiedEmpty { get; }
        }

        private sealed class WorkspaceObjects
        {
            public WorkspaceObjects(GameObject room, Transform doorway, GameObject realDoor, Transform doorLeaf, Light injector)
            {
                Room = room;
                Doorway = doorway;
                RealDoor = realDoor;
                DoorLeaf = doorLeaf;
                Injector = injector;
            }

            public GameObject Room { get; }
            public Transform Doorway { get; }
            public GameObject RealDoor { get; }
            public Transform DoorLeaf { get; }
            public Light Injector { get; }
        }

        private struct RendererCapture
        {
            public DungeonPortalReceiverResponseCapture.CaptureRenderer Data;
            public string typeName;
            public string parityMetadata;
        }

        private readonly struct FullRendererInventory
        {
            public FullRendererInventory(
                DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] entries,
                string signature)
            {
                Entries = entries ?? Array.Empty<DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry>();
                Signature = signature ?? string.Empty;
            }

            public DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] Entries { get; }
            public string Signature { get; }
            public int Count => Entries.Length;
        }

        private struct FullRendererInventoryRecord
        {
            public DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry Entry;
        }

        private sealed class FullRendererInventoryRecordComparer : IComparer<FullRendererInventoryRecord>
        {
            public static readonly FullRendererInventoryRecordComparer Instance =
                new FullRendererInventoryRecordComparer();

            public int Compare(FullRendererInventoryRecord left, FullRendererInventoryRecord right)
            {
                int result = string.CompareOrdinal(left.Entry.scope, right.Entry.scope);
                if (result != 0) return result;
                result = string.CompareOrdinal(left.Entry.relativePath, right.Entry.relativePath);
                if (result != 0) return result;
                result = string.CompareOrdinal(left.Entry.rendererType, right.Entry.rendererType);
                if (result != 0) return result;
                return left.Entry.componentOrdinal.CompareTo(right.Entry.componentOrdinal);
            }
        }

        private sealed class RendererCaptureComparer : IComparer<RendererCapture>
        {
            public static readonly RendererCaptureComparer Instance = new RendererCaptureComparer();

            public int Compare(RendererCapture left, RendererCapture right)
            {
                int result = string.CompareOrdinal(left.Data.relativePath, right.Data.relativePath);
                if (result != 0) return result;
                result = string.CompareOrdinal(left.typeName, right.typeName);
                if (result != 0) return result;
                result = left.Data.componentOrdinal.CompareTo(right.Data.componentOrdinal);
                if (result != 0) return result;
                result = string.CompareOrdinal(left.Data.meshAssetGuid, right.Data.meshAssetGuid);
                if (result != 0) return result;
                return left.Data.meshLocalId.CompareTo(right.Data.meshLocalId);
            }
        }

        /// <summary>
        /// Tracks the one unique state-evidence folder created by this invocation. It
        /// never trusts a checkpoint/serialized path for deletion: both the generated
        /// random token and an on-disk ownership manifest must match before cleanup.
        /// A failed cleanup deliberately leaves the manifest in place so the next run
        /// stops rather than treating orphaned partial evidence as canonical input.
        /// </summary>
        private sealed class StateArtifactTransaction
        {
            private readonly string stateFolderPath;
            private readonly string manifestPath;
            private readonly string physicalStateFolderPath;
            private readonly string physicalManifestPath;
            private readonly string token;
            private readonly string manifestContents;
            private bool committed;
            private bool folderCreated;
            private bool manifestWritten;

            private StateArtifactTransaction(
                string stateFolderPath,
                string manifestPath,
                string physicalStateFolderPath,
                string physicalManifestPath,
                string token,
                string manifestContents)
            {
                this.stateFolderPath = stateFolderPath;
                this.manifestPath = manifestPath;
                this.physicalStateFolderPath = physicalStateFolderPath;
                this.physicalManifestPath = physicalManifestPath;
                this.token = token;
                this.manifestContents = manifestContents;
            }

            public string StateFolderPath => stateFolderPath;

            public static StateArtifactTransaction Begin(ReceiverSpec spec, string stateName)
            {
                string stateRoot = GetStateRootFolder(spec, stateName);
                EnsureAssetFolder(stateRoot);
                string token = Guid.NewGuid().ToString("N");
                string folderName = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) +
                                    "_" + token;
                string stateFolder = stateRoot + "/" + folderName;
                if (!TryGetCanonicalGeneratedAssetPath(
                        stateFolder,
                        out string canonicalStateFolder,
                        out string physicalStateFolder,
                        out string pathFailure) ||
                    !string.Equals(canonicalStateFolder, stateFolder, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "State artifact path did not canonicalize under Generated/Bounce: " + pathFailure);
                }
                if (AssetDatabase.IsValidFolder(canonicalStateFolder) || Directory.Exists(physicalStateFolder))
                    throw new InvalidOperationException("State artifact folder unexpectedly already exists: '" + stateFolder + "'.");

                string manifestPath = stateFolder + "/" + StateArtifactOwnershipManifestPrefix + token + ".txt";
                if (!TryGetCanonicalGeneratedAssetPath(
                        manifestPath,
                        out string canonicalManifestPath,
                        out string physicalManifestPath,
                        out pathFailure) ||
                    !string.Equals(canonicalManifestPath, manifestPath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "State artifact manifest path did not canonicalize under Generated/Bounce: " + pathFailure);
                }

                string manifestContents = "DungeonPortalReceiverBounceStateArtifact/v1\n" +
                                          "token=" + token + "\n" +
                                          "stateFolder=" + stateFolder + "\n";
                var transaction = new StateArtifactTransaction(
                    stateFolder,
                    manifestPath,
                    physicalStateFolder,
                    physicalManifestPath,
                    token,
                    manifestContents);
                try
                {
                    string created = AssetDatabase.CreateFolder(stateRoot, folderName);
                    if (string.IsNullOrWhiteSpace(created) || !AssetDatabase.IsValidFolder(canonicalStateFolder))
                        throw new InvalidOperationException("Unable to create state artifact folder: '" + stateFolder + "'.");
                    transaction.folderCreated = true;

                    File.WriteAllText(physicalManifestPath, manifestContents, new UTF8Encoding(false));
                    transaction.manifestWritten = true;
                    AssetDatabase.ImportAsset(canonicalManifestPath, ImportAssetOptions.ForceSynchronousImport);
                    if (!File.Exists(physicalManifestPath) ||
                        !string.Equals(File.ReadAllText(physicalManifestPath), manifestContents, StringComparison.Ordinal) ||
                        AssetDatabase.LoadAssetAtPath<TextAsset>(canonicalManifestPath) == null)
                    {
                        throw new InvalidOperationException(
                            "State artifact ownership manifest could not be persisted and verified: '" +
                            manifestPath + "'.");
                    }

                    return transaction;
                }
                catch
                {
                    transaction.TryRollbackUncommitted();
                    throw;
                }
            }

            public void Commit()
            {
                if (committed)
                    return;
                if (!VerifyOwnership(out string ownershipFailure))
                    throw new InvalidOperationException("Cannot commit state artifact: " + ownershipFailure);
                if (!AssetDatabase.DeleteAsset(manifestPath))
                {
                    throw new InvalidOperationException(
                        "Unable to remove committed state-artifact ownership manifest: '" + manifestPath + "'.");
                }
                committed = true;
            }

            public string TryRollbackUncommitted()
            {
                if (committed)
                    return string.Empty;

                var failures = new List<string>();
                if (!folderCreated)
                    return string.Empty;
                if (!manifestWritten)
                {
                    if (!TryGetCanonicalGeneratedAssetPath(
                            stateFolderPath,
                            out string canonicalStateFolder,
                            out string canonicalPhysicalStateFolder,
                            out string pathFailure) ||
                        !string.Equals(canonicalStateFolder, stateFolderPath, StringComparison.Ordinal) ||
                        !string.Equals(canonicalPhysicalStateFolder, physicalStateFolderPath, StringComparison.OrdinalIgnoreCase) ||
                        !stateFolderPath.EndsWith("_" + token, StringComparison.Ordinal))
                    {
                        return "state artifact direct-created folder path validation failed: " + pathFailure;
                    }
                    if (AssetDatabase.IsValidFolder(canonicalStateFolder) && !AssetDatabase.DeleteAsset(canonicalStateFolder))
                        return "unable to delete direct-created state artifact folder after manifest write failure: " + canonicalStateFolder;
                    return string.Empty;
                }
                if (!VerifyOwnership(out string ownershipFailure))
                {
                    failures.Add(ownershipFailure);
                    return string.Join(" | ", failures);
                }

                if (AssetDatabase.IsValidFolder(stateFolderPath) && !AssetDatabase.DeleteAsset(stateFolderPath))
                {
                    failures.Add("unable to delete exact owned state artifact folder: " + stateFolderPath);
                }
                return failures.Count == 0 ? string.Empty : string.Join(" | ", failures);
            }

            private bool VerifyOwnership(out string failure)
            {
                failure = string.Empty;
                string statePathFailure = string.Empty;
                string manifestPathFailure = string.Empty;
                string canonicalStateFolder = string.Empty;
                string canonicalPhysicalStateFolder = string.Empty;
                string canonicalManifestPath = string.Empty;
                string canonicalPhysicalManifestPath = string.Empty;
                bool statePathValid = TryGetCanonicalGeneratedAssetPath(
                    stateFolderPath,
                    out canonicalStateFolder,
                    out canonicalPhysicalStateFolder,
                    out statePathFailure);
                bool manifestPathValid = TryGetCanonicalGeneratedAssetPath(
                    manifestPath,
                    out canonicalManifestPath,
                    out canonicalPhysicalManifestPath,
                    out manifestPathFailure);
                if (!folderCreated || string.IsNullOrWhiteSpace(token) ||
                    !stateFolderPath.EndsWith("_" + token, StringComparison.Ordinal) ||
                    !statePathValid || !manifestPathValid ||
                    !string.Equals(canonicalStateFolder, stateFolderPath, StringComparison.Ordinal) ||
                    !string.Equals(canonicalManifestPath, manifestPath, StringComparison.Ordinal) ||
                    !string.Equals(canonicalPhysicalStateFolder, physicalStateFolderPath, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(canonicalPhysicalManifestPath, physicalManifestPath, StringComparison.OrdinalIgnoreCase))
                {
                    failure = "state artifact ownership path validation failed: " + statePathFailure + " " + manifestPathFailure;
                    return false;
                }

                if (!manifestWritten || !File.Exists(physicalManifestPath) ||
                    !string.Equals(File.ReadAllText(physicalManifestPath), manifestContents, StringComparison.Ordinal))
                {
                    failure = "state artifact ownership manifest is missing or no longer matches its invocation token: '" +
                              manifestPath + "'.";
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Cleans only artifacts this first invocation proved absent before it created
        /// them. Once a checkpoint is saved it is committed and never rolled back: a
        /// partially captured checkpoint is the explicit resume boundary.
        /// </summary>
        private sealed class FirstRunArtifactTransaction
        {
            private readonly string capturePath;
            private readonly string workspacePath;
            private readonly string clonePath;
            private bool committed;

            private FirstRunArtifactTransaction(
                string capturePath,
                string workspacePath,
                string clonePath)
            {
                this.capturePath = capturePath;
                this.workspacePath = workspacePath;
                this.clonePath = clonePath;
            }

            public static FirstRunArtifactTransaction Begin(ReceiverSpec spec)
            {
                string capture = GetCaptureAssetPath(spec);
                string workspace = GetWorkspacePath(spec);
                string clone = GetLightingSettingsClonePath(spec);
                if (!IsGeneratedPath(capture) || !IsGeneratedPath(workspace) || !IsGeneratedPath(clone) ||
                    AssetDatabase.LoadMainAssetAtPath(capture) != null ||
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(workspace) != null ||
                    AssetDatabase.LoadMainAssetAtPath(clone) != null)
                {
                    throw new InvalidOperationException(
                        "First-run transaction cannot establish exclusive ownership of its PoC artifacts.");
                }

                return new FirstRunArtifactTransaction(capture, workspace, clone);
            }

            public void Commit()
            {
                if (AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverResponseCapture>(capturePath) == null)
                    throw new InvalidOperationException("Cannot commit a first-run transaction without its checkpoint asset.");
                committed = true;
            }

            public string TryRollbackUncommitted()
            {
                if (committed)
                    return string.Empty;

                var failures = new List<string>();
                try
                {
                    Scene loadedWorkspace = SceneManager.GetSceneByPath(workspacePath);
                    if (loadedWorkspace.IsValid() && loadedWorkspace.isLoaded)
                        EditorSceneManager.CloseScene(loadedWorkspace, true);
                    else if (SceneManager.sceneCount == 1 && string.IsNullOrWhiteSpace(SceneManager.GetActiveScene().path))
                        EditorSceneManager.CloseScene(SceneManager.GetActiveScene(), true);
                }
                catch (Exception exception)
                {
                    failures.Add("close workspace: " + exception.Message);
                }

                TryDeleteOwnedGeneratedAsset(capturePath, failures);
                TryDeleteOwnedGeneratedAsset(workspacePath, failures);
                TryDeleteOwnedGeneratedAsset(clonePath, failures);
                return failures.Count == 0 ? string.Empty : string.Join(" | ", failures);
            }

            private static void TryDeleteOwnedGeneratedAsset(string path, List<string> failures)
            {
                if (!TryGetCanonicalGeneratedAssetPath(path, out string canonicalPath, out _, out string pathFailure) ||
                    !string.Equals(canonicalPath, path, StringComparison.Ordinal))
                {
                    failures.Add("refused non-canonical PoC deletion: " + path + " (" + pathFailure + ")");
                    return;
                }
                if (AssetDatabase.LoadMainAssetAtPath(canonicalPath) == null)
                    return;
                if (!AssetDatabase.DeleteAsset(canonicalPath))
                    failures.Add("unable to delete newly-created PoC artifact: " + canonicalPath);
            }
        }

        private sealed class ProductionInputGuard
        {
            private readonly string assetPath;
            private readonly string dependencyHash;

            private ProductionInputGuard(string assetPath, string dependencyHash)
            {
                this.assetPath = assetPath;
                this.dependencyHash = dependencyHash;
            }

            public static ProductionInputGuard[] CaptureFor(ReceiverSpec spec)
            {
                var paths = new List<string>
                {
                    spec.RoomPrefabPath,
                    DoorPrefabPath,
                    OfficialLightingSettingsPath,
                    DunGenDoorSourcePath,
                    DoorLeafSourcePath,
                    RotationBakeToolSourcePath,
                    PreservedAdminLampPrefabPath,
                    PreservedV2StartCeilingLampPrefabPath
                };
                for (int i = 0; i < P0EmissionVariants.Length; i++)
                {
                    paths.Add(P0EmissionVariants[i].SourcePath);
                    paths.Add(P0EmissionVariants[i].P100Path);
                    paths.Add(P0EmissionVariants[i].P0Path);
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                var guards = new List<ProductionInputGuard>();
                for (int i = 0; i < paths.Count; i++)
                {
                    string path = paths[i];
                    if (!seen.Add(path))
                        continue;
                    UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (asset == null)
                        throw new InvalidOperationException("Required production input is missing: '" + path + "'.");
                    if (EditorUtility.IsDirty(asset))
                    {
                        throw new InvalidOperationException(
                            "Required production input is dirty: '" + path + "'. Save or discard it before capture.");
                    }
                    guards.Add(new ProductionInputGuard(path, GetDependencyHash(path)));
                }

                if (guards.Count == 0)
                    throw new InvalidOperationException("No production inputs were captured for the receiver bake guard.");
                return guards.ToArray();
            }

            public void AssertUnchanged()
            {
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (asset == null || EditorUtility.IsDirty(asset))
                {
                    throw new InvalidOperationException(
                        "Production input became missing or dirty during capture: '" + assetPath + "'.");
                }
                string currentHash = GetDependencyHash(assetPath);
                if (!string.Equals(currentHash, dependencyHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Production input dependency hash changed during capture: '" + assetPath + "'.");
                }
            }

            public static void AssertUnchanged(ProductionInputGuard[] guards)
            {
                if (guards == null)
                    throw new ArgumentNullException(nameof(guards));
                for (int i = 0; i < guards.Length; i++)
                    guards[i].AssertUnchanged();
            }

            public static DungeonPortalReceiverResponseCapture.AssetFingerprint[] ToFingerprints(
                ProductionInputGuard[] guards)
            {
                AssertUnchanged(guards);
                var fingerprints = new DungeonPortalReceiverResponseCapture.AssetFingerprint[guards.Length];
                for (int i = 0; i < guards.Length; i++)
                {
                    fingerprints[i] = new DungeonPortalReceiverResponseCapture.AssetFingerprint
                    {
                        assetPath = guards[i].assetPath,
                        dependencyHash = guards[i].dependencyHash,
                        wasDirtyBeforeCapture = false,
                        wasDirtyAfterCapture = false
                    };
                }
                return fingerprints;
            }

            public static void AssertFingerprintsMatch(
                DungeonPortalReceiverResponseCapture.AssetFingerprint[] fingerprints,
                ProductionInputGuard[] guards)
            {
                if (fingerprints == null || guards == null || fingerprints.Length != guards.Length)
                {
                    throw new InvalidOperationException(
                        "Existing checkpoint production-input fingerprint count does not match the current contract.");
                }
                for (int i = 0; i < guards.Length; i++)
                {
                    if (fingerprints[i].wasDirtyBeforeCapture || fingerprints[i].wasDirtyAfterCapture ||
                        !string.Equals(fingerprints[i].assetPath, guards[i].assetPath, StringComparison.Ordinal) ||
                        !string.Equals(fingerprints[i].dependencyHash, guards[i].dependencyHash, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Existing checkpoint production-input fingerprint differs at index " + i + ".");
                    }
                }
                AssertUnchanged(guards);
            }
        }

        private sealed class EditorSessionSnapshot
        {
            private readonly SceneSetup[] sceneSetup;
            private readonly string activeScenePath;
            private readonly string[] selectionGlobalObjectIds;
            private readonly string activeSelectionGlobalObjectId;
            private readonly Lightmapping.BakeOnSceneLoadMode bakeOnSceneLoadMode;
            private readonly bool hadLightingSettings;
            private readonly LightingSettings lightingSettings;
            private readonly LightingDataAsset lightingDataAsset;
            private readonly LightmapData[] lightmaps;
            private readonly LightmapsMode lightmapsMode;

            private EditorSessionSnapshot(
                SceneSetup[] sceneSetup,
                string activeScenePath,
                string[] selectionGlobalObjectIds,
                string activeSelectionGlobalObjectId,
                Lightmapping.BakeOnSceneLoadMode bakeOnSceneLoadMode,
                bool hadLightingSettings,
                LightingSettings lightingSettings,
                LightingDataAsset lightingDataAsset,
                LightmapData[] lightmaps,
                LightmapsMode lightmapsMode)
            {
                this.sceneSetup = sceneSetup;
                this.activeScenePath = activeScenePath;
                this.selectionGlobalObjectIds = selectionGlobalObjectIds;
                this.activeSelectionGlobalObjectId = activeSelectionGlobalObjectId;
                this.bakeOnSceneLoadMode = bakeOnSceneLoadMode;
                this.hadLightingSettings = hadLightingSettings;
                this.lightingSettings = lightingSettings;
                this.lightingDataAsset = lightingDataAsset;
                this.lightmaps = lightmaps;
                this.lightmapsMode = lightmapsMode;
            }

            public static EditorSessionSnapshot Capture()
            {
                Scene active = SceneManager.GetActiveScene();
                UnityEngine.Object[] selected = Selection.objects ?? Array.Empty<UnityEngine.Object>();
                var selectedGlobalIds = new string[selected.Length];
                for (int i = 0; i < selected.Length; i++)
                    selectedGlobalIds[i] = CaptureSelectionGlobalObjectId(selected[i], false);
                bool hasLightingSettings = Lightmapping.TryGetLightingSettings(
                    out LightingSettings currentLightingSettings);
                return new EditorSessionSnapshot(
                    (SceneSetup[])EditorSceneManager.GetSceneManagerSetup().Clone(),
                    active.path,
                    selectedGlobalIds,
                    CaptureSelectionGlobalObjectId(Selection.activeObject, true),
                    Lightmapping.bakeOnSceneLoad,
                    hasLightingSettings,
                    currentLightingSettings,
                    Lightmapping.lightingDataAsset,
                    LightmapSettings.lightmaps != null ? (LightmapData[])LightmapSettings.lightmaps.Clone() : null,
                    LightmapSettings.lightmapsMode);
            }

            public void Restore()
            {
                Lightmapping.BakeOnSceneLoadMode desiredBakeOnSceneLoadMode = bakeOnSceneLoadMode;
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;
                try
                {
                    EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
                    bool currentlyHasLightingSettings = Lightmapping.TryGetLightingSettings(
                        out LightingSettings currentLightingSettings);
                    if (hadLightingSettings)
                    {
                        if (!currentlyHasLightingSettings || currentLightingSettings != lightingSettings)
                            Lightmapping.lightingSettings = lightingSettings;
                    }
                    else if (currentlyHasLightingSettings)
                    {
                        throw new InvalidOperationException(
                            "Original scene had no LightingSettings, but scene restoration introduced one.");
                    }
                    if (Lightmapping.lightingDataAsset != lightingDataAsset)
                        Lightmapping.lightingDataAsset = lightingDataAsset;
                    LightmapSettings.lightmapsMode = lightmapsMode;
                    LightmapSettings.lightmaps = lightmaps;

                    if (!string.IsNullOrWhiteSpace(activeScenePath))
                    {
                        Scene active = SceneManager.GetSceneByPath(activeScenePath);
                        if (!active.IsValid() || !active.isLoaded)
                        {
                            throw new InvalidOperationException(
                                "Unable to restore the original active scene: '" + activeScenePath + "'.");
                        }

                        Scene currentActive = SceneManager.GetActiveScene();
                        if (!string.Equals(currentActive.path, activeScenePath, StringComparison.Ordinal) &&
                            !EditorSceneManager.SetActiveScene(active))
                        {
                            throw new InvalidOperationException(
                                "Unable to restore the original active scene: '" + activeScenePath + "'.");
                        }

                        if (!string.Equals(SceneManager.GetActiveScene().path, activeScenePath, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                "The restored active scene does not match the original: '" + activeScenePath + "'.");
                        }
                    }

                    UnityEngine.Object[] restoredSelection = ResolveSelectionGlobalObjectIds(
                        selectionGlobalObjectIds,
                        false);
                    UnityEngine.Object restoredActive = ResolveSelectionGlobalObjectId(
                        activeSelectionGlobalObjectId,
                        true);
                    Selection.objects = restoredSelection;
                    Selection.activeObject = restoredActive;
                }
                finally
                {
                    Lightmapping.bakeOnSceneLoad = desiredBakeOnSceneLoadMode;
                }
            }

            public void AssertRestoredClean()
            {
                int expectedLoadedCount = 0;
                for (int i = 0; i < sceneSetup.Length; i++)
                {
                    SceneSetup expected = sceneSetup[i];
                    if (!expected.isLoaded)
                        continue;
                    expectedLoadedCount++;
                    Scene scene = SceneManager.GetSceneByPath(expected.path);
                    if (!scene.IsValid() || !scene.isLoaded || scene.isDirty)
                    {
                        throw new InvalidOperationException(
                            "Original SceneSetup was not restored cleanly: '" + expected.path + "'.");
                    }
                }
                if (SceneManager.sceneCount != expectedLoadedCount)
                {
                    throw new InvalidOperationException(
                        "Original SceneSetup loaded-scene count was not restored. expected=" +
                        expectedLoadedCount + " actual=" + SceneManager.sceneCount + ".");
                }
                if (!string.IsNullOrWhiteSpace(activeScenePath) &&
                    !string.Equals(SceneManager.GetActiveScene().path, activeScenePath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Original active scene was not restored.");
                }
                bool currentlyHasLightingSettings = Lightmapping.TryGetLightingSettings(
                    out LightingSettings currentLightingSettings);
                if (Lightmapping.bakeOnSceneLoad != bakeOnSceneLoadMode ||
                    currentlyHasLightingSettings != hadLightingSettings ||
                    (hadLightingSettings && currentLightingSettings != lightingSettings) ||
                    Lightmapping.lightingDataAsset != lightingDataAsset ||
                    LightmapSettings.lightmapsMode != lightmapsMode ||
                    !SameLightmapLayout(LightmapSettings.lightmaps, lightmaps))
                {
                    throw new InvalidOperationException("Original editor lighting state was not restored.");
                }
                if (!SelectionMatchesGlobalObjectIds())
                {
                    throw new InvalidOperationException(
                        "Original editor selection was not restored by exact GlobalObjectId sequence.");
                }
            }

            private bool SelectionMatchesGlobalObjectIds()
            {
                UnityEngine.Object[] current = Selection.objects ?? Array.Empty<UnityEngine.Object>();
                if (current.Length != selectionGlobalObjectIds.Length)
                    return false;
                for (int i = 0; i < current.Length; i++)
                {
                    if (!string.Equals(
                            CaptureSelectionGlobalObjectId(current[i], false),
                            selectionGlobalObjectIds[i],
                            StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
                return string.Equals(
                    CaptureSelectionGlobalObjectId(Selection.activeObject, true),
                    activeSelectionGlobalObjectId,
                    StringComparison.Ordinal);
            }

            private static string CaptureSelectionGlobalObjectId(
                UnityEngine.Object value,
                bool allowNull)
            {
                if (value == null)
                {
                    if (allowNull)
                        return string.Empty;
                    throw new InvalidOperationException(
                        "Editor selection contains a null entry and cannot be snapshotted exactly.");
                }

                GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(value);
                string serialized = globalId.ToString();
                if (string.IsNullOrWhiteSpace(serialized) ||
                    !GlobalObjectId.TryParse(serialized, out GlobalObjectId parsed) ||
                    GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) != value)
                {
                    throw new InvalidOperationException(
                        "Editor selection object '" + value.name +
                        "' has no stable, currently resolvable GlobalObjectId.");
                }
                return serialized;
            }

            private static UnityEngine.Object[] ResolveSelectionGlobalObjectIds(
                string[] values,
                bool allowEmpty)
            {
                string[] source = values ?? Array.Empty<string>();
                var result = new UnityEngine.Object[source.Length];
                for (int i = 0; i < source.Length; i++)
                    result[i] = ResolveSelectionGlobalObjectId(source[i], allowEmpty);
                return result;
            }

            private static UnityEngine.Object ResolveSelectionGlobalObjectId(
                string value,
                bool allowEmpty)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (allowEmpty)
                        return null;
                    throw new InvalidOperationException(
                        "Editor selection identity is empty and cannot be restored exactly.");
                }
                if (!GlobalObjectId.TryParse(value, out GlobalObjectId globalId))
                    throw new InvalidOperationException("Editor selection GlobalObjectId cannot be parsed: '" + value + "'.");
                UnityEngine.Object restored = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId);
                if (restored == null)
                {
                    throw new InvalidOperationException(
                        "Editor selection GlobalObjectId cannot be resolved after scene restoration: '" + value + "'.");
                }
                return restored;
            }

            private static bool SameLightmapLayout(LightmapData[] left, LightmapData[] right)
            {
                int leftLength = left != null ? left.Length : 0;
                int rightLength = right != null ? right.Length : 0;
                if (leftLength != rightLength)
                    return false;
                for (int i = 0; i < leftLength; i++)
                {
                    LightmapData leftData = left[i];
                    LightmapData rightData = right[i];
                    if (leftData == null || rightData == null)
                    {
                        if (leftData != rightData)
                            return false;
                        continue;
                    }
                    if (leftData.lightmapColor != rightData.lightmapColor ||
                        leftData.lightmapDir != rightData.lightmapDir ||
                        leftData.shadowMask != rightData.shadowMask)
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }
}
