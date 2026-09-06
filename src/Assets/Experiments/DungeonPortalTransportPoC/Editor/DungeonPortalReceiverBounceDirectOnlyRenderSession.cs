using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DungeonPortalTransportPoC
{
    /// <summary>
    /// A disposable, never-saved reconstruction of a schema-7 DirectOnly capture.
    /// It deliberately owns its camera, MRT targets, materials and command buffers;
    /// the opened canonical workspace and the persisted capture assets are read only.
    /// </summary>
    internal sealed class DungeonPortalReceiverBounceDirectOnlyRenderSession : IDisposable
    {
        internal const string StableDoorwayId = "Doorways[5]/Door_SM_A[0]/DoorWayPoint[0]";
        internal const string RealDoorName = "ReceiverBounce_RealDoor_FullyOpen";
        internal const string DoorLeafName = "Door_01";
        internal const string InjectorName = "ReceiverBounce_BakedSpot__PoC";
        internal const int UnsupportedObjectId = 0xFF00FF;

        private static readonly int CaptureGpuVpId = Shader.PropertyToID("_StageA_CaptureGpuVP");
        private static readonly int DoorwayWorldToLocalId = Shader.PropertyToID("_StageA_DoorwayWorldToLocal");
        private static readonly int ObjectIdColorId = Shader.PropertyToID("_StageA_ObjectIdColor");
        private static readonly int UnsupportedId = Shader.PropertyToID("_StageA_Unsupported");
        private static readonly int CullId = Shader.PropertyToID("_StageA_Cull");

        private readonly DungeonPortalReceiverResponseCapture capture;
        private readonly DungeonPortalReceiverResponseCapture.CaptureState directOnly;
        private readonly DungeonPortalReceiverResponseCapture.CaptureState full;
        private readonly Shader maskShader;
        private readonly EditorSessionSnapshot snapshot;
        private readonly List<RendererMaskInfo> maskRenderers = new List<RendererMaskInfo>();
        private readonly HashSet<Renderer> restoredLightmappedReceiverRenderers = new HashSet<Renderer>();
        private readonly Dictionary<int, int> sourceLightmapToTransientSlot = new Dictionary<int, int>();
        private readonly Dictionary<Renderer, LodMembership> lodMembership =
            new Dictionary<Renderer, LodMembership>();
        private Scene workspaceScene;
        private Scene transientScene;
        private GameObject canonicalRoot;
        private GameObject transientCloneRoot;
        private Transform doorway;
        private GameObject realDoor;
        private Transform doorLeaf;
        private readonly Dictionary<int, Material> supportedMaskMaterials = new Dictionary<int, Material>();
        private Material unsupportedMaskMaterial;
        private bool disposed;
        private string fatalMaskFailure;
        private string nonReadyReason;
        private DungeonPortalReceiverBounceRenderArtifact.DoorPoseEvidence doorPose;
        private DungeonPortalReceiverBounceRenderArtifact.MaskRendererAttestation[] maskAttestations;
        private string stableObjectIdManifestHash;
        private string opaqueDepthAttestationHash;
        private int unsupportedSubmeshCount;
        private string unsupportedReason;

        private DungeonPortalReceiverBounceDirectOnlyRenderSession(
            DungeonPortalReceiverResponseCapture capture,
            Shader maskShader,
            EditorSessionSnapshot snapshot)
        {
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.maskShader = maskShader ?? throw new ArgumentNullException(nameof(maskShader));
            this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            directOnly = capture.DirectOnly;
            full = capture.Full;
        }

        internal DungeonPortalReceiverResponseCapture Capture => capture;
        internal DungeonPortalReceiverResponseCapture.CaptureState DirectOnly => directOnly;
        internal DungeonPortalReceiverResponseCapture.CaptureState Full => full;
        internal DungeonPortalReceiverBounceRenderArtifact.DoorPoseEvidence DoorPose => doorPose;
        internal DungeonPortalReceiverBounceRenderArtifact.MaskRendererAttestation[] MaskAttestations =>
            maskAttestations != null
                ? (DungeonPortalReceiverBounceRenderArtifact.MaskRendererAttestation[])maskAttestations.Clone()
                : Array.Empty<DungeonPortalReceiverBounceRenderArtifact.MaskRendererAttestation>();
        internal string StableObjectIdManifestHash => stableObjectIdManifestHash ?? string.Empty;
        internal string OpaqueDepthAttestationHash => opaqueDepthAttestationHash ?? string.Empty;
        internal int UnsupportedSubmeshCount => unsupportedSubmeshCount;
        internal string UnsupportedReason => unsupportedReason ?? string.Empty;
        internal string FatalMaskFailure => fatalMaskFailure ?? string.Empty;
        internal string NonReadyReason => nonReadyReason ?? string.Empty;
        internal bool CanRenderMasks => string.IsNullOrEmpty(fatalMaskFailure);

        internal static bool TryOpen(
            DungeonPortalReceiverResponseCapture receiverCapture,
            Shader objectIdMrtShader,
            out DungeonPortalReceiverBounceDirectOnlyRenderSession session,
            out string error)
        {
            session = null;
            error = string.Empty;
            EditorSessionSnapshot snapshot = null;
            try
            {
                if (receiverCapture == null || !receiverCapture.TryValidate(out error))
                {
                    if (string.IsNullOrWhiteSpace(error))
                        error = "Capture is null or structurally invalid.";
                    return false;
                }
                if (objectIdMrtShader == null || !objectIdMrtShader.isSupported)
                {
                    error = "The Stage A object-ID MRT shader is unavailable or unsupported by the active renderer.";
                    return false;
                }
                if (!string.Equals(receiverCapture.StableDoorwayId, StableDoorwayId, StringComparison.Ordinal))
                {
                    error = "Capture does not bind the canonical receiver doorway.";
                    return false;
                }
                string workspacePath = receiverCapture.Provenance.workspaceScenePath;
                for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
                {
                    Scene loaded = SceneManager.GetSceneAt(sceneIndex);
                    if (loaded.isLoaded && string.Equals(loaded.path, workspacePath, StringComparison.Ordinal))
                    {
                        error = "Canonical Stage A workspace is already loaded; close it before the isolated transaction.";
                        return false;
                    }
                }

                snapshot = EditorSessionSnapshot.Capture();
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;
                session = new DungeonPortalReceiverBounceDirectOnlyRenderSession(
                    receiverCapture,
                    objectIdMrtShader,
                    snapshot);
                snapshot = null;
                session.OpenWorkspaceAndBuildTransientClone();
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (session != null)
                {
                    try { session.Dispose(); }
                    catch (Exception restoreException) { error += " Restore failure: " + restoreException.Message; }
                    session = null;
                }
                else if (snapshot != null)
                {
                    try
                    {
                        snapshot.Restore();
                        snapshot.AssertRestoredClean();
                    }
                    catch (Exception restoreException)
                    {
                        error += " Restore failure: " + restoreException.Message;
                    }
                }
                return false;
            }
        }

        internal bool TryRenderAll(out RenderedCameraFrame[] frames, out string error)
        {
            frames = Array.Empty<RenderedCameraFrame>();
            error = string.Empty;
            ThrowIfDisposed();
            RenderedCameraFrame[] rendered = null;
            try
            {
                DungeonPortalReceiverResponseCapture.FixedCameraCapture[] cameras = directOnly.fixedCameraCaptures ??
                    Array.Empty<DungeonPortalReceiverResponseCapture.FixedCameraCapture>();
                if (cameras.Length != DungeonPortalReceiverBounceRenderArtifact.FixedCameraCount)
                    throw new InvalidOperationException("DirectOnly does not contain the canonical four fixed cameras.");

                rendered = new RenderedCameraFrame[cameras.Length];
                for (int i = 0; i < cameras.Length; i++)
                    rendered[i] = RenderCamera(cameras[i], i);
                frames = rendered;
                return true;
            }
            catch (Exception exception)
            {
                DisposeFrames(rendered);
                frames = Array.Empty<RenderedCameraFrame>();
                error = exception.Message;
                return false;
            }
        }

        internal static void DisposeFrames(RenderedCameraFrame[] frames)
        {
            if (frames == null)
                return;
            for (int i = 0; i < frames.Length; i++)
            {
                DestroyTransient(frames[i].reconstructedOffHdr);
                DestroyTransient(frames[i].stableObjectIdMask);
                DestroyTransient(frames[i].doorwayLocalZWorldNormalMask);
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            var failures = new List<Exception>();
            try
            {
                foreach (Material material in supportedMaskMaterials.Values)
                    DestroyTransient(material);
                supportedMaskMaterials.Clear();
                DestroyTransient(unsupportedMaskMaterial);
                unsupportedMaskMaterial = null;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            try
            {
                DestroyTransient(transientCloneRoot);
                transientCloneRoot = null;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            try
            {
                snapshot.Restore();
                snapshot.AssertRestoredClean();
                if (transientScene.IsValid() && transientScene.isLoaded)
                    throw new InvalidOperationException("Original SceneSetup restoration left the Stage A transient scene loaded.");
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            finally
            {
                doorway = null;
                realDoor = null;
                doorLeaf = null;
                canonicalRoot = null;
                workspaceScene = default;
                transientScene = default;
                maskRenderers.Clear();
                restoredLightmappedReceiverRenderers.Clear();
                sourceLightmapToTransientSlot.Clear();
                lodMembership.Clear();
            }

            if (failures.Count != 0)
                throw new InvalidOperationException("Stage A editor/session restoration failed.", new AggregateException(failures));
        }

        private void OpenWorkspaceAndBuildTransientClone()
        {
            DungeonPortalReceiverResponseCapture.CaptureProvenance provenance = capture.Provenance;
            if (directOnly.lightmapsMode != LightmapsMode.CombinedDirectional ||
                !string.Equals(directOnly.stateName, DungeonPortalReceiverBounceRenderArtifact.DirectOnlyStateName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Stage A requires the persisted DirectOnly CombinedDirectional state.");
            }

            AssertCurrentPipelineMatches(directOnly);
            workspaceScene = EditorSceneManager.OpenScene(provenance.workspaceScenePath, OpenSceneMode.Single);
            AssertSoleWorkspaceScene(workspaceScene, provenance.workspaceScenePath);
            if (workspaceScene.isDirty)
                throw new InvalidOperationException("Canonical workspace opened dirty; Stage A refuses to save or normalize it.");
            string workspaceHash = GetDependencyHash(provenance.workspaceScenePath);
            if (!string.Equals(workspaceHash, provenance.canonicalWorkspaceDependencyHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Canonical workspace dependency hash differs from the persisted capture.");
            }

            canonicalRoot = ResolveCanonicalRoot(workspaceScene, capture.ReceiverRoomId);
            ResolveWorkspaceTopology(canonicalRoot, out Transform canonicalDoorway, out GameObject canonicalDoor,
                out Transform canonicalLeaf, out Light canonicalInjector);
            AssertFullyOpenDoor(canonicalDoor, canonicalLeaf, out _);
            AssertCanonicalInjectorDisabled(canonicalInjector);
            LightingSettings canonicalLightingSettings = Lightmapping.lightingSettings;
            RenderEnvironmentSnapshot canonicalEnvironment = RenderEnvironmentSnapshot.Capture();

            // Instantiate directly under a parent in a different untitled scene.  The
            // canonical workspace never receives a clone, SetActive call, renderer
            // write, material write, or dirty flag.
            transientScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            if (!transientScene.IsValid() || !transientScene.isLoaded ||
                !EditorSceneManager.SetActiveScene(transientScene))
            {
                throw new InvalidOperationException("Unable to create and activate the isolated Stage A transient scene.");
            }
            canonicalEnvironment.ApplyToActiveScene();
            Lightmapping.lightingSettings = canonicalLightingSettings;
            Lightmapping.lightingDataAsset = null;
            LightmapSettings.lightmaps = Array.Empty<LightmapData>();

            var stagingParent = new GameObject("__StageA_CloneStaging")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            SceneManager.MoveGameObjectToScene(stagingParent, transientScene);
            transientCloneRoot = UnityEngine.Object.Instantiate(canonicalRoot, stagingParent.transform);
            transientCloneRoot.name = "__StageA_DirectOnlyTransientClone__" + capture.ReceiverRoomId;
            transientCloneRoot.hideFlags = HideFlags.HideAndDontSave;
            transientCloneRoot.transform.SetParent(null, true);
            UnityEngine.Object.DestroyImmediate(stagingParent);
            if (!transientCloneRoot.activeSelf)
                throw new InvalidOperationException("Transient receiver clone unexpectedly became inactive.");

            if (workspaceScene.isDirty)
                throw new InvalidOperationException("Canonical workspace became dirty while making a transient clone.");
            if (!EditorSceneManager.CloseScene(workspaceScene, true))
                throw new InvalidOperationException("Unable to close the clean canonical workspace after transient clone creation.");
            canonicalRoot = null;
            if (SceneManager.sceneCount != 1 || !transientScene.isLoaded ||
                !string.Equals(SceneManager.GetActiveScene().path, transientScene.path, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Stage A transient clone scene did not become the sole active scene.");
            }
            ResolveWorkspaceTopology(transientCloneRoot, out doorway, out realDoor, out doorLeaf, out Light cloneInjector);
            doorPose = AssertFullyOpenDoor(realDoor, doorLeaf, out string doorProbeSignature);
            doorPose.preservedDoorLightProbeSignature = doorProbeSignature;
            AssertCanonicalInjectorDisabled(cloneInjector);

            RestoreDirectOnlyLightmapsInMemory();
            ApplyCapturedReceiverLightmapMappings();
            BuildFullInventoryAndMaskAttestations();
            ValidateDynamicDoorProbeLimitation();
            BuildMaskMaterial();
        }

        private static void AssertSoleWorkspaceScene(Scene scene, string expectedPath)
        {
            if (!scene.IsValid() || !scene.isLoaded || SceneManager.sceneCount != 1 ||
                !string.Equals(scene.path, expectedPath, StringComparison.Ordinal) ||
                !string.Equals(SceneManager.GetActiveScene().path, expectedPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Stage A must use the persisted canonical workspace as the sole loaded scene.");
            }
        }

        private static GameObject ResolveCanonicalRoot(Scene scene, string roomId)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            string expectedName = "ReceiverBounce_" + roomId + "_ProductionInstance";
            if (roots.Length != 1 || roots[0] == null || !string.Equals(roots[0].name, expectedName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Canonical workspace must contain exactly one expected receiver root: '" + expectedName + "'.");
            }
            return roots[0];
        }

        private static void ResolveWorkspaceTopology(
            GameObject room,
            out Transform resolvedDoorway,
            out GameObject resolvedRealDoor,
            out Transform resolvedDoorLeaf,
            out Light resolvedInjector)
        {
            if (room == null)
                throw new ArgumentNullException(nameof(room));
            Transform doorways = RequireIndexedChild(room.transform, 5, "Doorways", StableDoorwayId);
            Transform doorModel = RequireIndexedChild(doorways, 0, "Door_SM_A", StableDoorwayId);
            resolvedDoorway = RequireIndexedChild(doorModel, 0, "DoorWayPoint", StableDoorwayId);
            if (resolvedDoorway.GetComponent<Doorway>() == null)
                throw new InvalidOperationException("Canonical stable doorway has no DunGen.Doorway component.");

            Transform realDoorTransform = FindUniqueChild(room.transform, RealDoorName);
            resolvedRealDoor = realDoorTransform.gameObject;
            resolvedDoorLeaf = resolvedRealDoor.transform.Find(DoorLeafName);
            if (resolvedDoorLeaf == null)
                throw new InvalidOperationException("Canonical real door has no Door_01 leaf.");

            Transform injectorTransform = resolvedDoorway.Find(InjectorName);
            resolvedInjector = injectorTransform != null ? injectorTransform.GetComponent<Light>() : null;
            if (resolvedInjector == null || injectorTransform.GetComponents<Light>().Length != 1)
                throw new InvalidOperationException("Canonical doorway has no unique PoC injector.");
        }

        private static Transform RequireIndexedChild(Transform parent, int index, string expectedName, string label)
        {
            if (parent == null || index < 0 || parent.childCount <= index)
                throw new InvalidOperationException("Stable hierarchy segment is missing: '" + label + "'.");
            Transform child = parent.GetChild(index);
            if (child == null || !string.Equals(child.name, expectedName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Stable hierarchy segment drifted at '" + label + "'. expected='" + expectedName + "'.");
            }
            return child;
        }

        private static Transform FindUniqueChild(Transform root, string name)
        {
            Transform found = null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (!string.Equals(all[i].name, name, StringComparison.Ordinal))
                    continue;
                if (found != null)
                    throw new InvalidOperationException("Expected one unique child named '" + name + "'.");
                found = all[i];
            }
            if (found == null)
                throw new InvalidOperationException("Expected child named '" + name + "' was not found.");
            return found;
        }

        private static DungeonPortalReceiverBounceRenderArtifact.DoorPoseEvidence AssertFullyOpenDoor(
            GameObject door,
            Transform leaf,
            out string probeSignature)
        {
            if (door == null || leaf == null)
                throw new ArgumentNullException(door == null ? nameof(door) : nameof(leaf));
            Door[] doors = door.GetComponentsInChildren<Door>(true);
            if (doors.Length != 1 || !doors[0].IsOpen)
                throw new InvalidOperationException("Stage A requires exactly one fully-open real DunGen door.");
            Vector3 euler = NormalizeEuler(leaf.localEulerAngles);
            if (Mathf.Abs(Mathf.DeltaAngle(euler.y, 90f)) > 0.01f)
                throw new InvalidOperationException("The real door leaf is not in the canonical 90-degree fully-open pose.");
            LightProbeGroup[] groups = door.GetComponentsInChildren<LightProbeGroup>(true);
            if (groups.Length != 1 || groups[0].probePositions == null || groups[0].probePositions.Length != 8)
                throw new InvalidOperationException("The real door must preserve exactly eight local light probes.");
            var builder = new StringBuilder(512);
            Vector3[] points = groups[0].probePositions;
            for (int i = 0; i < points.Length; i++)
                AppendVector3(builder, points[i]);
            probeSignature = Hash128.Compute(builder.ToString()).ToString();
            return new DungeonPortalReceiverBounceRenderArtifact.DoorPoseEvidence
            {
                fullOpenPoseVerified = true,
                doorOpenFlagVerified = true,
                leafLocalEulerAngles = euler,
                fullOpenPoseSignature = Hash128.Compute(
                    "Door_01|" + euler.x.ToString("R", CultureInfo.InvariantCulture) + "|" +
                    euler.y.ToString("R", CultureInfo.InvariantCulture) + "|" +
                    euler.z.ToString("R", CultureInfo.InvariantCulture)).ToString(),
                preservedDoorLightProbeCount = points.Length,
                preservedDoorLightProbeSignature = probeSignature
            };
        }

        private static void AssertCanonicalInjectorDisabled(Light injector)
        {
            if (injector == null || injector.type != LightType.Spot || injector.lightmapBakeType != LightmapBakeType.Baked ||
                injector.enabled || !injector.gameObject.activeInHierarchy ||
                Mathf.Abs(injector.bounceIntensity) > 0.0001f)
            {
                throw new InvalidOperationException("Saved P0 canonical workspace does not have the disabled DirectOnly PoC injector.");
            }
        }

        private void RestoreDirectOnlyLightmapsInMemory()
        {
            if (Lightmapping.lightingDataAsset != null)
            {
                throw new InvalidOperationException(
                    "Canonical Stage A workspace unexpectedly has LightingDataAsset; it must remain the unbaked P0 clone.");
            }

            DungeonPortalReceiverResponseCapture.CaptureLightmap[] maps = directOnly.lightmaps ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            if (maps.Length == 0)
                throw new InvalidOperationException("DirectOnly capture has no copied lightmaps.");
            var ordered = new List<DungeonPortalReceiverResponseCapture.CaptureLightmap>(maps);
            ordered.Sort((left, right) => left.sourceLightmapIndex.CompareTo(right.sourceLightmapIndex));
            for (int i = 0; i < maps.Length; i++)
            {
                if (maps[i].sourceLightmapIndex < 0 || maps[i].colorTexture == null || maps[i].directionTexture == null)
                    throw new InvalidOperationException("DirectOnly capture has an invalid CombinedDirectional lightmap record.");
            }

            var restored = new LightmapData[maps.Length];
            sourceLightmapToTransientSlot.Clear();
            for (int destinationIndex = 0; destinationIndex < ordered.Count; destinationIndex++)
            {
                DungeonPortalReceiverResponseCapture.CaptureLightmap map = ordered[destinationIndex];
                if (sourceLightmapToTransientSlot.ContainsKey(map.sourceLightmapIndex))
                    throw new InvalidOperationException("DirectOnly copied lightmap source indexes are duplicated.");
                if (map.colorTexture.width != map.colorWidth || map.colorTexture.height != map.colorHeight ||
                    map.directionTexture.width != map.directionWidth || map.directionTexture.height != map.directionHeight)
                {
                    throw new InvalidOperationException("DirectOnly copied lightmap texture dimensions drifted.");
                }
                restored[destinationIndex] = new LightmapData
                {
                    lightmapColor = map.colorTexture,
                    lightmapDir = map.directionTexture,
                    shadowMask = map.hasShadowMask ? map.shadowMaskTexture : null
                };
                sourceLightmapToTransientSlot.Add(map.sourceLightmapIndex, destinationIndex);
            }

            LightmapSettings.lightmapsMode = LightmapsMode.CombinedDirectional;
            LightmapSettings.lightmaps = restored;
            if (LightmapSettings.lightmapsMode != LightmapsMode.CombinedDirectional ||
                LightmapSettings.lightmaps == null || LightmapSettings.lightmaps.Length != restored.Length)
            {
                throw new InvalidOperationException("Unable to install DirectOnly lightmaps in the transient Stage A session.");
            }
        }

        private void ApplyCapturedReceiverLightmapMappings()
        {
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] captured = directOnly.renderers ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            List<RendererMappingCandidate> cloneCandidates = BuildReceiverMappingCandidates();
            if (captured.Length == 0)
            {
                throw new InvalidOperationException("DirectOnly has no receiver lightmap mappings.");
            }
            var assigned = new HashSet<Renderer>();
            for (int i = 0; i < captured.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.CaptureRenderer source = captured[i];
                RendererMappingCandidate match = default;
                int matchCount = 0;
                for (int candidateIndex = 0; candidateIndex < cloneCandidates.Count; candidateIndex++)
                {
                    RendererMappingCandidate candidate = cloneCandidates[candidateIndex];
                    if (source.rendererBucketIndex == candidate.rendererBucketIndex &&
                        source.componentOrdinal == candidate.componentOrdinal &&
                        string.Equals(source.relativePath, candidate.relativePath, StringComparison.Ordinal) &&
                        string.Equals(source.meshAssetGuid, candidate.meshGuid, StringComparison.Ordinal) &&
                        source.meshLocalId == candidate.meshLocalId &&
                        string.Equals(source.meshUv2Hash, candidate.meshUv2Hash, StringComparison.Ordinal))
                    {
                        match = candidate;
                        matchCount++;
                    }
                }
                if (matchCount != 1 || !assigned.Add(match.renderer) || source.lightmapIndex < 0 ||
                    !IsFinite(source.lightmapScaleOffset))
                {
                    throw new InvalidOperationException(
                        "DirectOnly captured renderer does not resolve one unique clone renderer by path/component/mesh/UV2 identity.");
                }
                if (!sourceLightmapToTransientSlot.TryGetValue(source.lightmapIndex, out int transientSlot))
                {
                    throw new InvalidOperationException(
                        "Captured DirectOnly renderer references a source lightmap absent from the copied atlas layout.");
                }
                match.renderer.lightmapIndex = transientSlot;
                match.renderer.lightmapScaleOffset = source.lightmapScaleOffset;
                restoredLightmappedReceiverRenderers.Add(match.renderer);
            }

            for (int i = 0; i < cloneCandidates.Count; i++)
            {
                RendererMappingCandidate candidate = cloneCandidates[i];
                if (!assigned.Contains(candidate.renderer) && candidate.renderer.lightmapIndex >= 0)
                {
                    throw new InvalidOperationException(
                        "Unmatched receiver renderer is unexpectedly lightmapped in the canonical P0 clone: '" +
                        candidate.relativePath + "'.");
                }
            }
        }

        private List<RendererMappingCandidate> BuildReceiverMappingCandidates()
        {
            Renderer[] all = transientCloneRoot.GetComponentsInChildren<Renderer>(true);
            var candidates = new List<RendererMappingCandidate>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer renderer = all[i];
                if (renderer == null || IsInSubtree(renderer.transform, realDoor.transform))
                    continue;
                Mesh mesh = GetRendererMesh(renderer);
                if (mesh == null)
                    continue;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId) ||
                    string.IsNullOrWhiteSpace(guid) || localId == 0L)
                {
                    throw new InvalidOperationException("Transient renderer mesh is not a persistent project asset: '" + renderer.name + "'.");
                }
                candidates.Add(new RendererMappingCandidate
                {
                    renderer = renderer,
                    relativePath = GetStableRelativePath(transientCloneRoot.transform, renderer.transform),
                    typeName = renderer.GetType().FullName ?? renderer.GetType().Name,
                    componentOrdinal = GetRendererComponentOrdinal(renderer),
                    meshGuid = guid,
                    meshLocalId = localId,
                    meshUv2Hash = ComputeUv2Hash(mesh)
                });
            }

            candidates.Sort(RendererMappingCandidateComparer.Instance);
            var bucketCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                RendererMappingCandidate candidate = candidates[i];
                if (!bucketCounts.TryGetValue(candidate.relativePath, out int bucketIndex))
                    bucketIndex = 0;
                candidate.rendererBucketIndex = bucketIndex;
                bucketCounts[candidate.relativePath] = bucketIndex + 1;
                candidates[i] = candidate;
            }

            return candidates;
        }

        private void BuildFullInventoryAndMaskAttestations()
        {
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] expected =
                directOnly.fullRendererInventory ?? Array.Empty<DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry>();
            if (expected.Length == 0)
                throw new InvalidOperationException("DirectOnly capture has no full renderer inventory.");

            var actual = new Dictionary<string, Renderer>(StringComparer.Ordinal);
            AddFullInventoryScope(actual, "ReceiverRoom", transientCloneRoot.transform,
                GetReceiverRenderersExcludingDoor(transientCloneRoot, realDoor.transform));
            AddFullInventoryScope(actual, "RealDoor", realDoor.transform,
                realDoor.GetComponentsInChildren<Renderer>(true));
            if (actual.Count != expected.Length)
            {
                throw new InvalidOperationException(
                    "Transient full renderer inventory count differs from the persisted DirectOnly inventory.");
            }

            var attestations = new DungeonPortalReceiverBounceRenderArtifact.MaskRendererAttestation[expected.Length];
            var objectIdBuilder = new StringBuilder(expected.Length * 96);
            var depthBuilder = new StringBuilder(expected.Length * 160);
            unsupportedSubmeshCount = 0;
            var unsupportedMessages = new List<string>();
            for (int i = 0; i < expected.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry entry = expected[i];
                if (!actual.TryGetValue(entry.canonicalBucket, out Renderer renderer) || renderer == null)
                {
                    throw new InvalidOperationException(
                        "Transient full renderer inventory is missing canonical bucket: '" + entry.canonicalBucket + "'.");
                }

                RendererMaskInfo info = ClassifyRendererForMask(
                    renderer,
                    entry.canonicalBucket,
                    i + 1,
                    entry.scope);
                maskRenderers.Add(info);
                int submeshCount = info.supportedSubmeshes != null ? info.supportedSubmeshes.Length : 1;
                int supportedCount = 0;
                int unsupportedCount = 0;
                var reasonBuilder = new StringBuilder();
                for (int submesh = 0; submesh < submeshCount; submesh++)
                {
                    if (info.supportedSubmeshes != null && info.supportedSubmeshes[submesh])
                        supportedCount++;
                    else
                    {
                        unsupportedCount++;
                        string reason = info.unsupportedReasons != null ? info.unsupportedReasons[submesh] : "unknown";
                        if (reasonBuilder.Length != 0)
                            reasonBuilder.Append("; ");
                        reasonBuilder.Append(reason);
                    }
                }
                string unsupported = reasonBuilder.ToString();
                string attestationHash = ComputeRendererAttestationHash(info, unsupported);
                attestations[i] = new DungeonPortalReceiverBounceRenderArtifact.MaskRendererAttestation
                {
                    canonicalBucket = entry.canonicalBucket,
                    stableObjectId = i + 1,
                    submeshCount = submeshCount,
                    supportedOpaqueSubmeshCount = supportedCount,
                    unsupportedSubmeshCount = unsupportedCount,
                    attestationHash = attestationHash,
                    unsupportedReason = unsupported
                };
                AppendString(objectIdBuilder, entry.canonicalBucket);
                objectIdBuilder.Append(i + 1).Append('|');
                AppendString(depthBuilder, entry.canonicalBucket);
                depthBuilder.Append(i + 1).Append('|');
                depthBuilder.Append(submeshCount).Append('|');
                depthBuilder.Append(supportedCount).Append('|');
                depthBuilder.Append(unsupportedCount).Append('|');
                AppendString(depthBuilder, attestationHash);
                AppendString(depthBuilder, unsupported);
                if (unsupportedCount > 0)
                {
                    unsupportedSubmeshCount += unsupportedCount;
                    unsupportedMessages.Add(entry.canonicalBucket + ": " + unsupported);
                }
                if (info.cannotRepresentSilhouette && string.IsNullOrEmpty(fatalMaskFailure))
                    fatalMaskFailure = entry.canonicalBucket + ": " + unsupported;
            }

            maskAttestations = attestations;
            stableObjectIdManifestHash = Hash128.Compute(objectIdBuilder.ToString()).ToString();
            opaqueDepthAttestationHash = Hash128.Compute(depthBuilder.ToString()).ToString();
            unsupportedReason = unsupportedMessages.Count == 0 ? string.Empty : string.Join(" | ", unsupportedMessages);
            BuildLodMembershipOrFailClosed();
        }

        private static void AddFullInventoryScope(
            IDictionary<string, Renderer> destination,
            string scope,
            Transform scopeRoot,
            Renderer[] renderers)
        {
            if (destination == null || scopeRoot == null || renderers == null)
                throw new ArgumentNullException("Full-inventory scope input is null.");
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    throw new InvalidOperationException("Transient full renderer inventory contains null renderer.");
                string type = renderer.GetType().FullName ?? renderer.GetType().Name;
                string bucket = scope + "|" + GetStableRelativePath(scopeRoot, renderer.transform) + "|" + type + "|" +
                                GetRendererComponentOrdinal(renderer);
                if (destination.ContainsKey(bucket))
                    throw new InvalidOperationException("Transient full renderer inventory has a duplicate canonical bucket: '" + bucket + "'.");
                destination.Add(bucket, renderer);
            }
        }

        private static Renderer[] GetReceiverRenderersExcludingDoor(GameObject root, Transform doorRoot)
        {
            Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
            var result = new List<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && !IsInSubtree(all[i].transform, doorRoot))
                    result.Add(all[i]);
            }
            return result.ToArray();
        }

        private RendererMaskInfo ClassifyRendererForMask(
            Renderer renderer,
            string bucket,
            int stableObjectId,
            string scope)
        {
            var info = new RendererMaskInfo
            {
                renderer = renderer,
                mesh = GetRendererMesh(renderer),
                stableObjectId = stableObjectId,
                canonicalBucket = bucket
            };
            int submeshCount = info.mesh != null ? info.mesh.subMeshCount : 1;
            info.supportedSubmeshes = new bool[Mathf.Max(1, submeshCount)];
            info.unsupportedReasons = new string[info.supportedSubmeshes.Length];

            if (!(renderer is MeshRenderer) || info.mesh == null)
            {
                SetAllUnsupported(info, "unsupported renderer/deformation type: " + renderer.GetType().FullName, true);
                return info;
            }
            if (((MeshRenderer)renderer).additionalVertexStreams != null || info.mesh.blendShapeCount != 0)
            {
                SetAllUnsupported(info, "additional vertex streams or blend-shape deformation is not reproducible", true);
                return info;
            }
            if (!info.mesh.HasVertexAttribute(VertexAttribute.Normal))
            {
                SetAllUnsupported(info, "mesh has no geometric vertex-normal attribute", true);
                return info;
            }
            if (!AreMaterialPropertyBlocksEmpty(renderer, out string mpbFailure))
            {
                SetAllUnsupported(info, mpbFailure, true);
                return info;
            }

            Material[] materials = renderer.sharedMaterials ?? Array.Empty<Material>();
            if (materials.Length != info.supportedSubmeshes.Length)
            {
                SetAllUnsupported(info, "material/submesh count mismatch", true);
                return info;
            }
            bool dynamicDoor = string.Equals(scope, "RealDoor", StringComparison.Ordinal);
            bool exactLightmapMapping = restoredLightmappedReceiverRenderers.Contains(renderer);
            for (int submesh = 0; submesh < info.supportedSubmeshes.Length; submesh++)
            {
                string materialReason = ClassifyMaterialForManualOpaqueMask(materials[submesh]);
                if (!dynamicDoor && exactLightmapMapping && string.IsNullOrEmpty(materialReason))
                {
                    info.supportedSubmeshes[submesh] = true;
                }
                else
                {
                    string reason = dynamicDoor
                        ? "dynamic-door absolute LightProbes are not persisted by schema-7"
                        : !exactLightmapMapping
                            ? "receiver renderer is not in the exact DirectOnly lightmap index/ST mapping"
                            : materialReason;
                    if (!string.IsNullOrEmpty(materialReason) &&
                        !string.Equals(reason, materialReason, StringComparison.Ordinal))
                    {
                        reason += "; " + materialReason;
                    }
                    info.unsupportedReasons[submesh] = reason;
                    if (!CanConservativelyRepresentUnsupportedMaterial(materialReason))
                        info.cannotRepresentSilhouette = true;
                }
            }
            return info;
        }

        private static void SetAllUnsupported(RendererMaskInfo info, string reason, bool cannotRepresentSilhouette)
        {
            for (int i = 0; i < info.supportedSubmeshes.Length; i++)
            {
                info.supportedSubmeshes[i] = false;
                info.unsupportedReasons[i] = reason;
            }
            info.cannotRepresentSilhouette = cannotRepresentSilhouette;
        }

        private static bool AreMaterialPropertyBlocksEmpty(Renderer renderer, out string failure)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            if (!block.isEmpty)
            {
                failure = "non-empty renderer MaterialPropertyBlock cannot be reproduced by a manual override";
                return false;
            }
            Material[] materials = renderer.sharedMaterials ?? Array.Empty<Material>();
            for (int i = 0; i < materials.Length; i++)
            {
                block.Clear();
                renderer.GetPropertyBlock(block, i);
                if (!block.isEmpty)
                {
                    failure = "non-empty per-material MaterialPropertyBlock cannot be reproduced by a manual override";
                    return false;
                }
            }
            failure = string.Empty;
            return true;
        }

        private static string ClassifyMaterialForManualOpaqueMask(Material material)
        {
            if (material == null || material.shader == null)
                return "missing material or shader";
            string shaderName = material.shader.name ?? string.Empty;
            if (!IsSupportedUrpStaticShader(shaderName))
                return "unknown/custom vertex shader is not silhouette-safe: " + shaderName;
            if (material.renderQueue >= (int)RenderQueue.AlphaTest ||
                string.Equals(material.GetTag("RenderType", false, string.Empty), "Transparent", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(material.GetTag("RenderType", false, string.Empty), "TransparentCutout", StringComparison.OrdinalIgnoreCase) ||
                material.IsKeywordEnabled("_ALPHATEST_ON") || material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
                material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") || material.IsKeywordEnabled("_ALPHAMODULATE_ON") ||
                (material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f))
            {
                return "transparent or alpha-tested source material";
            }
            if (!material.HasProperty("_ZWrite") || Mathf.RoundToInt(material.GetFloat("_ZWrite")) != 1)
                return "source ZWrite is unknown or disabled";
            if (!material.HasProperty("_Cull") || Mathf.RoundToInt(material.GetFloat("_Cull")) != (int)CullMode.Back)
                return "source Cull is unknown or not back-face culling";
            if (material.HasProperty("_ZTest") && Mathf.RoundToInt(material.GetFloat("_ZTest")) != (int)CompareFunction.LessEqual)
                return "source ZTest is not LEqual";
            if ((material.HasProperty("_OffsetFactor") && Mathf.Abs(material.GetFloat("_OffsetFactor")) > 0.000001f) ||
                (material.HasProperty("_OffsetUnits") && Mathf.Abs(material.GetFloat("_OffsetUnits")) > 0.000001f) ||
                (material.HasProperty("_DepthOffset") && Mathf.Abs(material.GetFloat("_DepthOffset")) > 0.000001f))
            {
                return "source depth offset is non-zero";
            }
            return string.Empty;
        }

        private static bool CanConservativelyRepresentUnsupportedMaterial(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return true;
            return reason.StartsWith("transparent or alpha-tested", StringComparison.Ordinal) ||
                   reason.StartsWith("source ZWrite", StringComparison.Ordinal) ||
                   reason.StartsWith("source Cull", StringComparison.Ordinal);
        }

        private static bool IsSupportedUrpStaticShader(string shaderName)
        {
            return string.Equals(shaderName, "Universal Render Pipeline/Lit", StringComparison.Ordinal) ||
                   string.Equals(shaderName, "Universal Render Pipeline/Simple Lit", StringComparison.Ordinal) ||
                   string.Equals(shaderName, "Universal Render Pipeline/Baked Lit", StringComparison.Ordinal) ||
                   string.Equals(shaderName, "Universal Render Pipeline/Unlit", StringComparison.Ordinal);
        }

        private void BuildLodMembershipOrFailClosed()
        {
            LODGroup[] groups = transientCloneRoot.GetComponentsInChildren<LODGroup>(true);
            if (groups.Length == 0)
                return;
            // Camera.Render chooses LOD in URP's culling stage. CommandBuffer.DrawRenderer
            // bypasses that stage; without a render-pipeline proof that our selection is
            // byte-identical, masks for this schema-7 evidence are intentionally unavailable.
            bool hasLiveLodRenderer = false;
            for (int g = 0; g < groups.Length; g++)
            {
                LOD[] lods = groups[g].GetLODs();
                for (int level = 0; level < lods.Length; level++)
                {
                    Renderer[] renderers = lods[level].renderers ?? Array.Empty<Renderer>();
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        if (renderers[r] == null || lodMembership.ContainsKey(renderers[r]))
                            continue;
                        hasLiveLodRenderer = true;
                        lodMembership.Add(renderers[r], new LodMembership(groups[g], level));
                    }
                }
            }
            if (hasLiveLodRenderer)
            {
                fatalMaskFailure =
                    "LODGroup geometry is present; manual DrawRenderer LOD selection is not URP-culling proven.";
            }
        }

        private void ValidateDynamicDoorProbeLimitation()
        {
            // Capture schema 7 persists sampled SH, not the complete absolute dynamic
            // LightProbes data structure needed to reconstruct the movable door.  Keep
            // this visible even if static opaque mask drawing happens to be available.
            nonReadyReason =
                "Schema-7 does not persist absolute dynamic-door LightProbes, and the transient clone is bucket-bound " +
                "but does not yet re-attest every capture parityMetadataHash after cloning; whole-frame/direct reconstruction is not fit-ready.";
        }

        private void BuildMaskMaterial()
        {
            if (!CanRenderMasks)
                return;
            if (maskShader == null || !maskShader.isSupported)
            {
                fatalMaskFailure = "Stage A MRT shader is unsupported by the active graphics device.";
                return;
            }
            unsupportedMaskMaterial = CreateOwnedMaskMaterial("__StageA_ObjectIdMrt_Unsupported");
            unsupportedMaskMaterial.SetColor(ObjectIdColorId, new Color(1f, 0f, 1f, 1f));
            unsupportedMaskMaterial.SetFloat(UnsupportedId, 1f);
            unsupportedMaskMaterial.SetFloat(CullId, (float)CullMode.Off);

            for (int rendererIndex = 0; rendererIndex < maskRenderers.Count; rendererIndex++)
            {
                RendererMaskInfo info = maskRenderers[rendererIndex];
                bool hasSupportedSubmesh = false;
                for (int submesh = 0; submesh < info.supportedSubmeshes.Length; submesh++)
                    hasSupportedSubmesh |= info.supportedSubmeshes[submesh];
                if (!hasSupportedSubmesh)
                    continue;
                if (supportedMaskMaterials.ContainsKey(info.stableObjectId))
                    throw new InvalidOperationException("Stage A stable object ID was duplicated while creating owned mask materials.");

                Material material = CreateOwnedMaskMaterial("__StageA_ObjectIdMrt_" + info.stableObjectId);
                material.SetColor(ObjectIdColorId, EncodeStableObjectId(info.stableObjectId));
                material.SetFloat(UnsupportedId, 0f);
                material.SetFloat(CullId, (float)CullMode.Back);
                supportedMaskMaterials.Add(info.stableObjectId, material);
            }
        }

        private Material CreateOwnedMaskMaterial(string materialName)
        {
            return new Material(maskShader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = false
            };
        }

        private static string ComputeRendererAttestationHash(RendererMaskInfo info, string unsupported)
        {
            var builder = new StringBuilder(512);
            AppendString(builder, info.canonicalBucket);
            builder.Append(info.stableObjectId).Append('|');
            AppendString(builder, info.renderer != null ? info.renderer.GetType().FullName : string.Empty);
            if (info.renderer != null)
            {
                builder.Append(info.renderer.enabled ? '1' : '0').Append('|');
                builder.Append(info.renderer.gameObject.activeInHierarchy ? '1' : '0').Append('|');
                builder.Append(info.renderer.forceRenderingOff ? '1' : '0').Append('|');
                builder.Append(info.renderer.gameObject.layer).Append('|');
                builder.Append(info.renderer.renderingLayerMask).Append('|');
            }
            builder.Append(info.mesh != null ? info.mesh.subMeshCount : 0).Append('|');
            if (info.supportedSubmeshes != null)
            {
                for (int i = 0; i < info.supportedSubmeshes.Length; i++)
                {
                    builder.Append(info.supportedSubmeshes[i] ? '1' : '0').Append('|');
                    AppendString(builder, info.unsupportedReasons != null ? info.unsupportedReasons[i] : string.Empty);
                }
            }
            AppendString(builder, unsupported);
            return Hash128.Compute(builder.ToString()).ToString();
        }

        private RenderedCameraFrame RenderCamera(
            DungeonPortalReceiverResponseCapture.FixedCameraCapture source,
            int cameraIndex)
        {
            AssertCurrentPipelineMatches(directOnly);
            if (source.width != 512 || source.height != 512 || source.width != source.height)
                throw new InvalidOperationException("Stage A only accepts the canonical 512x512 DirectOnly fixed camera contract.");
            GameObject cameraObject = null;
            RenderTexture hdrTarget = null;
            Texture2D reconstructed = null;
            Texture2D objectIdMask = null;
            Texture2D geometryMask = null;
            bool completed = false;
            try
            {
                cameraObject = CreateTransientCamera(source);
                Camera camera = cameraObject.GetComponent<Camera>();
                hdrTarget = CreateRenderTexture(source.width, source.height, 24, RenderTextureFormat.ARGBHalf,
                    "__StageA_ReconstructedOffHDR");
                camera.targetTexture = hdrTarget;
                camera.aspect = source.width / (float)source.height;
                camera.ResetProjectionMatrix();
                AssertCameraContract(camera, source);
                camera.Render();

                reconstructed = ReadBackTexture(hdrTarget, TextureFormat.RGBAHalf, "__StageA_ReconstructedOff");
                Color[] reconstructedPixels = reconstructed.GetPixels();
                if (!TryReadLinearHdrPixels(source.hdrColorTexture, out Color[] directPixels, out string directFailure))
                    throw new InvalidOperationException("Unable to read persisted DirectOnly HDR without importer mutation: " + directFailure);
                if (reconstructedPixels.Length != directPixels.Length)
                    throw new InvalidOperationException("Reconstructed DirectOnly HDR has a pixel-count mismatch.");

                var frame = new RenderedCameraFrame
                {
                    sourceCamera = source,
                    reconstructedOffHdr = reconstructed,
                    fullFrameDrift = ComputeDrift(directPixels, reconstructedPixels, null),
                    targetSignal = UnavailableTargetSignal("Exact supported-opaque mask is unavailable."),
                    pixelOrientation = new DungeonPortalReceiverBounceRenderArtifact.PixelOrientationEvidence
                    {
                        verified = false,
                        renderIntoTextureGpuProjection = true,
                        readPixelsBottomLeftOrigin = true,
                        projectedSampleCount = 0,
                        matchedSampleCount = 0,
                        calibrationSignature = string.Empty,
                        reason = "Stage A does not claim orientation proof without an isolated deterministic calibration render."
                    }
                };

                if (!CanRenderMasks)
                {
                    frame.supportedOpaqueDrift = frame.fullFrameDrift;
                    frame.maskUnavailableReason = FatalMaskFailure;
                    completed = true;
                    return frame;
                }

                RenderSelfOwnedMasks(camera, source, out objectIdMask, out geometryMask);
                frame.stableObjectIdMask = objectIdMask;
                frame.doorwayLocalZWorldNormalMask = geometryMask;
                ClassifyMaskPixels(
                    objectIdMask.GetPixels32(),
                    out int supportedCount,
                    out int unsupportedCount,
                    out int backgroundCount,
                    out int[] supportedIndices);
                frame.supportedOpaquePixelCount = supportedCount;
                frame.unsupportedSentinelPixelCount = unsupportedCount;
                frame.backgroundPixelCount = backgroundCount;
                frame.supportedOpaqueDrift = ComputeDrift(directPixels, reconstructedPixels, supportedIndices);
                if (supportedCount > 0)
                {
                    DungeonPortalReceiverResponseCapture.FixedCameraCapture[] fullCameras = full.fixedCameraCaptures ??
                        Array.Empty<DungeonPortalReceiverResponseCapture.FixedCameraCapture>();
                    string fullFailure = string.Empty;
                    Color[] fullPixels = null;
                    if (cameraIndex < 0 || cameraIndex >= fullCameras.Length ||
                        !string.Equals(fullCameras[cameraIndex].cameraId, source.cameraId, StringComparison.Ordinal) ||
                        fullCameras[cameraIndex].hdrColorTexture == null ||
                        !TryReadLinearHdrPixels(fullCameras[cameraIndex].hdrColorTexture, out fullPixels,
                            out fullFailure))
                    {
                        frame.targetSignal = UnavailableTargetSignal(
                            "Persisted Full HDR is unavailable for the matching DirectOnly camera: " + fullFailure);
                    }
                    else
                    {
                        frame.targetSignal = ComputeTargetSignal(
                            directPixels,
                            fullPixels,
                            supportedIndices,
                            frame.supportedOpaqueDrift);
                    }
                }
                else
                {
                    frame.targetSignal = UnavailableTargetSignal("Mask has zero supported opaque pixels.");
                }
                completed = true;
                return frame;
            }
            finally
            {
                if (!completed)
                {
                    DestroyTransient(reconstructed);
                    DestroyTransient(objectIdMask);
                    DestroyTransient(geometryMask);
                }
                if (hdrTarget != null)
                    DestroyTransient(hdrTarget);
                if (cameraObject != null)
                    DestroyTransient(cameraObject);
            }
        }

        private GameObject CreateTransientCamera(DungeonPortalReceiverResponseCapture.FixedCameraCapture source)
        {
            var result = new GameObject("__StageA_DirectOnlyCamera_" + source.cameraId)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            SceneManager.MoveGameObjectToScene(result, transientScene);
            result.transform.position = doorway.TransformPoint(source.doorwayLocalPosition);
            result.transform.rotation = doorway.rotation * Quaternion.Euler(source.doorwayLocalEulerAngles);
            Camera camera = result.AddComponent<Camera>();
            if (camera.gameObject.scene != transientScene)
                throw new InvalidOperationException("Transient Stage A camera was created outside the isolated scene.");
            camera.clearFlags = source.clearFlags;
            camera.backgroundColor = source.backgroundColor;
            camera.nearClipPlane = source.nearClipPlane;
            camera.farClipPlane = source.farClipPlane;
            camera.fieldOfView = source.fieldOfView;
            camera.orthographic = source.orthographic;
            camera.orthographicSize = source.orthographicSize;
            camera.aspect = source.aspect;
            camera.cullingMask = source.cullingMask;
            camera.renderingPath = source.renderingPath;
            camera.allowHDR = source.hdr;
            camera.allowMSAA = source.allowMsaa;
            camera.allowDynamicResolution = source.allowDynamicResolution;
            camera.useOcclusionCulling = source.useOcclusionCulling;
            camera.ResetProjectionMatrix();

            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            if (cameraData == null)
                throw new InvalidOperationException("URP additional camera data is unavailable for Stage A.");
            cameraData.renderType = CameraRenderType.Base;
            cameraData.renderShadows = true;
            cameraData.requiresDepthOption = CameraOverrideOption.UsePipelineSettings;
            cameraData.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
            cameraData.stopNaN = false;
            cameraData.allowXRRendering = true;
            cameraData.allowHDROutput = true;
            cameraData.renderPostProcessing = source.postProcessingEnabled;
            cameraData.antialiasing = source.antialiasing;
            cameraData.dithering = source.dithering;
            cameraData.volumeLayerMask = source.volumeLayerMask;
            SetUrpRendererIndex(cameraData, source.urpRendererIndex);
            if (result.GetComponent<Volume>() != null)
                throw new InvalidOperationException("Stage A camera must not have a Volume component.");
            AssertUrpFreshBasePolicy(camera, cameraData, source);
            return result;
        }

        private static void AssertUrpFreshBasePolicy(
            Camera camera,
            UniversalAdditionalCameraData cameraData,
            DungeonPortalReceiverResponseCapture.FixedCameraCapture source)
        {
            if (camera == null || cameraData == null ||
                cameraData.renderType != CameraRenderType.Base || !cameraData.renderShadows ||
                cameraData.requiresDepthOption != CameraOverrideOption.UsePipelineSettings ||
                cameraData.requiresColorOption != CameraOverrideOption.UsePipelineSettings || cameraData.stopNaN ||
                !cameraData.clearDepth || !cameraData.allowXRRendering || !cameraData.allowHDROutput ||
                cameraData.cameraStack == null || cameraData.cameraStack.Count != 0 ||
                cameraData.volumeLayerMask.value != source.volumeLayerMask ||
                cameraData.renderPostProcessing != source.postProcessingEnabled ||
                cameraData.antialiasing != source.antialiasing || cameraData.dithering != source.dithering ||
                GetUrpRendererIndex(cameraData) != source.urpRendererIndex)
            {
                throw new InvalidOperationException("Transient Stage A camera does not meet the fresh URP Base/default renderer policy.");
            }
        }

        private static void AssertCameraContract(
            Camera camera,
            DungeonPortalReceiverResponseCapture.FixedCameraCapture source)
        {
            if (camera == null || camera.orthographic != source.orthographic ||
                !Approximately(camera.orthographicSize, source.orthographicSize) ||
                !Approximately(camera.fieldOfView, source.fieldOfView) ||
                !Approximately(camera.nearClipPlane, source.nearClipPlane) ||
                !Approximately(camera.farClipPlane, source.farClipPlane) ||
                !Approximately(camera.aspect, source.aspect) || camera.clearFlags != source.clearFlags ||
                !Approximately(camera.backgroundColor, source.backgroundColor) || camera.cullingMask != source.cullingMask ||
                camera.renderingPath != source.renderingPath || camera.allowHDR != source.hdr ||
                camera.allowMSAA != source.allowMsaa ||
                camera.allowDynamicResolution != source.allowDynamicResolution ||
                camera.useOcclusionCulling != source.useOcclusionCulling)
            {
                throw new InvalidOperationException("Transient Stage A camera no longer matches the persisted fixed-camera contract.");
            }
            Matrix4x4 expected = source.orthographic
                ? Matrix4x4.Ortho(
                    -source.orthographicSize * source.aspect,
                    source.orthographicSize * source.aspect,
                    -source.orthographicSize,
                    source.orthographicSize,
                    source.nearClipPlane,
                    source.farClipPlane)
                : Matrix4x4.Perspective(source.fieldOfView, source.aspect, source.nearClipPlane, source.farClipPlane);
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (!Approximately(camera.projectionMatrix[row, column], expected[row, column]))
                        throw new InvalidOperationException("Transient Stage A camera projection matrix is not the persisted reset projection.");
                }
            }
        }

        private void RenderSelfOwnedMasks(
            Camera camera,
            DungeonPortalReceiverResponseCapture.FixedCameraCapture source,
            out Texture2D objectIdMask,
            out Texture2D geometryMask)
        {
            objectIdMask = null;
            geometryMask = null;
            if (camera == null || unsupportedMaskMaterial == null || !CanRenderMasks)
                throw new InvalidOperationException("Stage A self-owned mask rendering was requested without a valid opaque mask session.");
            if (SystemInfo.supportedRenderTargetCount < 2 ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32) ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                throw new InvalidOperationException("Graphics device cannot provide the required two-target sample-1 Stage A MRT contract.");
            }

            RenderTexture idTarget = null;
            RenderTexture geometryTarget = null;
            CommandBuffer command = null;
            try
            {
                idTarget = CreateRenderTexture(source.width, source.height, 24, RenderTextureFormat.ARGB32,
                    "__StageA_ObjectId");
                geometryTarget = CreateRenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGBHalf,
                    "__StageA_DoorwayLocalZWorldNormal");
                command = new CommandBuffer { name = "StageA Self-Owned ObjectId MRT" };
                RenderTargetIdentifier[] colorTargets =
                {
                    new RenderTargetIdentifier(idTarget.colorBuffer),
                    new RenderTargetIdentifier(geometryTarget.colorBuffer)
                };
                command.SetRenderTarget(colorTargets, new RenderTargetIdentifier(idTarget.depthBuffer));
                command.ClearRenderTarget(
                    true,
                    true,
                    Color.clear,
                    SystemInfo.usesReversedZBuffer ? 0f : 1f);
                Matrix4x4 captureGpuVp =
                    GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
                Matrix4x4 doorwayWorldToLocal = doorway.worldToLocalMatrix;
                ConfigureOwnedMaskMaterialForCamera(unsupportedMaskMaterial, captureGpuVp, doorwayWorldToLocal);
                foreach (Material material in supportedMaskMaterials.Values)
                    ConfigureOwnedMaskMaterialForCamera(material, captureGpuVp, doorwayWorldToLocal);

                // Write assured opaque geometry first.  Then write every unsupported
                // source submesh as the conservative sentinel so a coplanar decal or
                // glass surface cannot be silently overwritten back into a supported
                // object-id pixel by LEqual ordering.
                DrawMaskPhase(command, camera, true);
                DrawMaskPhase(command, camera, false);
                Graphics.ExecuteCommandBuffer(command);
                objectIdMask = ReadBackTexture(idTarget, TextureFormat.RGBA32, "__StageA_ObjectIdReadback");
                geometryMask = ReadBackTexture(geometryTarget, TextureFormat.RGBAHalf, "__StageA_GeometryReadback");
            }
            catch
            {
                DestroyTransient(objectIdMask);
                DestroyTransient(geometryMask);
                objectIdMask = null;
                geometryMask = null;
                throw;
            }
            finally
            {
                if (command != null)
                    command.Release();
                DestroyTransient(idTarget);
                DestroyTransient(geometryTarget);
            }
        }

        private static void ConfigureOwnedMaskMaterialForCamera(
            Material material,
            Matrix4x4 captureGpuVp,
            Matrix4x4 doorwayWorldToLocal)
        {
            if (material == null)
                throw new InvalidOperationException("Stage A owned mask material is missing.");
            material.SetMatrix(CaptureGpuVpId, captureGpuVp);
            material.SetMatrix(DoorwayWorldToLocalId, doorwayWorldToLocal);
        }

        private void DrawMaskPhase(CommandBuffer command, Camera camera, bool supportedPhase)
        {
            for (int rendererIndex = 0; rendererIndex < maskRenderers.Count; rendererIndex++)
            {
                RendererMaskInfo info = maskRenderers[rendererIndex];
                if (!IsVisibleForCamera(info.renderer, camera))
                    continue;
                if (lodMembership.ContainsKey(info.renderer))
                    throw new InvalidOperationException("Mask renderer belongs to a LODGroup despite the fail-closed LOD gate.");
                for (int submesh = 0; submesh < info.supportedSubmeshes.Length; submesh++)
                {
                    bool supported = info.supportedSubmeshes[submesh];
                    if (supported != supportedPhase)
                        continue;
                    Material material;
                    if (supported)
                    {
                        if (!supportedMaskMaterials.TryGetValue(info.stableObjectId, out material) || material == null)
                            throw new InvalidOperationException("Stage A supported mask material is missing for a stable object ID.");
                    }
                    else
                    {
                        material = unsupportedMaskMaterial;
                    }
                    // One self-owned pass writes both MRT targets and depth with LEqual.
                    // It does not reuse URP depth, nor depend on a second Equal pass.
                    command.DrawRenderer(info.renderer, material, submesh, 0);
                }
            }
        }

        private static bool IsVisibleForCamera(Renderer renderer, Camera camera)
        {
            if (renderer == null || camera == null || !renderer.enabled || renderer.forceRenderingOff ||
                renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly ||
                !renderer.gameObject.activeInHierarchy)
            {
                return false;
            }
            int layer = renderer.gameObject.layer;
            return layer >= 0 && layer <= 31 && (camera.cullingMask & (1 << layer)) != 0;
        }

        private static RenderTexture CreateRenderTexture(
            int width,
            int height,
            int depth,
            RenderTextureFormat format,
            string name)
        {
            GraphicsFormat expectedGraphicsFormat;
            switch (format)
            {
                case RenderTextureFormat.ARGB32:
                    expectedGraphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
                    break;
                case RenderTextureFormat.ARGBHalf:
                    expectedGraphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                    break;
                default:
                    throw new InvalidOperationException("Unsupported Stage A render-target format: " + format + ".");
            }
            if (!SystemInfo.IsFormatSupported(expectedGraphicsFormat, GraphicsFormatUsage.Render))
            {
                throw new InvalidOperationException(
                    "Graphics device does not support the exact linear Stage A render-target format: " +
                    expectedGraphicsFormat + ".");
            }
            var target = new RenderTexture(width, height, depth, format, RenderTextureReadWrite.Linear)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite = false,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!target.Create())
            {
                DestroyTransient(target);
                throw new InvalidOperationException("Unable to create self-owned Stage A render target '" + name + "'.");
            }
            if (target.width != width || target.height != height || target.antiAliasing != 1 || target.sRGB ||
                target.graphicsFormat != expectedGraphicsFormat)
            {
                DestroyTransient(target);
                throw new InvalidOperationException(
                    "Stage A render target does not meet the exact linear graphics-format/sample-1 contract.");
            }
            return target;
        }

        private static Texture2D ReadBackTexture(RenderTexture source, TextureFormat format, string name)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            RenderTexture previous = RenderTexture.active;
            Texture2D result = null;
            try
            {
                result = new Texture2D(source.width, source.height, format, false, true)
                {
                    name = name,
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                RenderTexture.active = source;
                result.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
                result.Apply(false, false);
                return result;
            }
            catch
            {
                DestroyTransient(result);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static Color EncodeStableObjectId(int id)
        {
            if (id <= 0 || id >= UnsupportedObjectId)
                throw new ArgumentOutOfRangeException(nameof(id), "Stable Stage A object IDs must not overlap zero/background or sentinel.");
            return new Color(
                (id & 0xFF) / 255f,
                ((id >> 8) & 0xFF) / 255f,
                ((id >> 16) & 0xFF) / 255f,
                1f);
        }

        private void ClassifyMaskPixels(
            Color32[] pixels,
            out int supportedCount,
            out int unsupportedCount,
            out int backgroundCount,
            out int[] supportedIndices)
        {
            if (pixels == null)
                throw new ArgumentNullException(nameof(pixels));
            var supported = new List<int>();
            unsupportedCount = 0;
            backgroundCount = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 value = pixels[i];
                if (IsUnsupportedSentinel(value))
                {
                    unsupportedCount++;
                }
                else if (value.a == 0 && value.r == 0 && value.g == 0 && value.b == 0)
                {
                    backgroundCount++;
                }
                else if (value.a == byte.MaxValue)
                {
                    int stableObjectId = value.r | (value.g << 8) | (value.b << 16);
                    if (stableObjectId <= 0 || stableObjectId >= UnsupportedObjectId ||
                        !supportedMaskMaterials.ContainsKey(stableObjectId))
                    {
                        throw new InvalidOperationException(
                            "Stage A object-ID target contains an unbound or out-of-range supported ID: " +
                            stableObjectId + ".");
                    }
                    supported.Add(i);
                }
                else
                {
                    throw new InvalidOperationException("Stage A object-ID target contains an invalid non-background/non-sentinel pixel.");
                }
            }
            supportedCount = supported.Count;
            supportedIndices = supported.ToArray();
        }

        private static bool IsUnsupportedSentinel(Color32 value)
        {
            return value.r == byte.MaxValue && value.g == 0 && value.b == byte.MaxValue &&
                   value.a == byte.MaxValue;
        }

        private static DungeonPortalReceiverBounceRenderArtifact.DriftMetrics ComputeDrift(
            Color[] expected,
            Color[] actual,
            int[] indices)
        {
            if (expected == null || actual == null || expected.Length != actual.Length)
                throw new ArgumentException("HDR drift inputs must have identical pixel counts.");
            int count = indices != null ? indices.Length : expected.Length;
            if (count <= 0)
            {
                return new DungeonPortalReceiverBounceRenderArtifact.DriftMetrics
                {
                    sampleCount = 0,
                    meanAbsoluteRgb = 0f,
                    rootMeanSquareRgb = 0f,
                    percentile99AbsoluteRgb = 0f,
                    maxAbsoluteRgb = 0f,
                    finite = false,
                    withinPolicy = false
                };
            }

            double absoluteSum = 0d;
            double squareSum = 0d;
            float[] pixelMaxima = new float[count];
            bool finite = true;
            for (int sample = 0; sample < count; sample++)
            {
                int index = indices != null ? indices[sample] : sample;
                if (index < 0 || index >= expected.Length)
                    throw new InvalidOperationException("Mask supported-pixel index is outside the HDR frame.");
                Color delta = actual[index] - expected[index];
                if (!IsFinite(delta))
                {
                    finite = false;
                    break;
                }
                float ar = Mathf.Abs(delta.r);
                float ag = Mathf.Abs(delta.g);
                float ab = Mathf.Abs(delta.b);
                absoluteSum += ar + ag + ab;
                squareSum += delta.r * delta.r + delta.g * delta.g + delta.b * delta.b;
                pixelMaxima[sample] = Mathf.Max(ar, Mathf.Max(ag, ab));
            }
            if (!finite)
            {
                return new DungeonPortalReceiverBounceRenderArtifact.DriftMetrics
                {
                    sampleCount = count,
                    finite = false,
                    withinPolicy = false
                };
            }

            Array.Sort(pixelMaxima);
            float mean = (float)(absoluteSum / (count * 3d));
            float rms = (float)Math.Sqrt(squareSum / (count * 3d));
            int percentileIndex = Mathf.Clamp(Mathf.CeilToInt(count * 0.99f) - 1, 0, count - 1);
            float p99 = pixelMaxima[percentileIndex];
            float maximum = pixelMaxima[count - 1];
            bool within = mean <= DungeonPortalReceiverBounceRenderArtifact.MaximumMeanAbsoluteRgbDrift &&
                          rms <= DungeonPortalReceiverBounceRenderArtifact.MaximumRootMeanSquareRgbDrift &&
                          p99 <= DungeonPortalReceiverBounceRenderArtifact.MaximumPercentile99PixelMaxAbsoluteRgbDrift &&
                          maximum <= DungeonPortalReceiverBounceRenderArtifact.MaximumPixelMaxAbsoluteRgbDrift;
            return new DungeonPortalReceiverBounceRenderArtifact.DriftMetrics
            {
                sampleCount = count,
                meanAbsoluteRgb = mean,
                rootMeanSquareRgb = rms,
                percentile99AbsoluteRgb = p99,
                maxAbsoluteRgb = maximum,
                finite = true,
                withinPolicy = within
            };
        }

        private static DungeonPortalReceiverBounceRenderArtifact.TargetSignalEvidence ComputeTargetSignal(
            Color[] direct,
            Color[] fullPixels,
            int[] supportedIndices,
            DungeonPortalReceiverBounceRenderArtifact.DriftMetrics supportedDrift)
        {
            if (direct == null || fullPixels == null || direct.Length != fullPixels.Length ||
                supportedIndices == null || supportedIndices.Length == 0)
            {
                return UnavailableTargetSignal("Full-minus-Direct target signal lacks an exact supported-opaque sample set.");
            }

            double absoluteSum = 0d;
            double squareSum = 0d;
            for (int i = 0; i < supportedIndices.Length; i++)
            {
                int index = supportedIndices[i];
                if (index < 0 || index >= direct.Length)
                    return UnavailableTargetSignal("Supported-opaque index is outside Full-minus-Direct target HDR.");
                Color delta = fullPixels[index] - direct[index];
                if (!IsFinite(delta))
                    return UnavailableTargetSignal("Full-minus-Direct target HDR has non-finite numeric samples.");
                absoluteSum += Mathf.Abs(delta.r) + Mathf.Abs(delta.g) + Mathf.Abs(delta.b);
                squareSum += delta.r * delta.r + delta.g * delta.g + delta.b * delta.b;
            }
            float mean = (float)(absoluteSum / (supportedIndices.Length * 3d));
            float rms = (float)Math.Sqrt(squareSum / (supportedIndices.Length * 3d));
            if (mean <= 0f || rms <= 0f || !IsFinite(mean) || !IsFinite(rms))
                return UnavailableTargetSignal("Full-minus-Direct target signal is zero or non-finite on the supported opaque subset.");
            float meanRatio = supportedDrift.meanAbsoluteRgb / mean;
            float rmsRatio = supportedDrift.rootMeanSquareRgb / rms;
            bool ratioPass = IsFinite(meanRatio) && IsFinite(rmsRatio) &&
                             meanRatio <= DungeonPortalReceiverBounceRenderArtifact.MaximumDriftToTargetSignalRatio &&
                             rmsRatio <= DungeonPortalReceiverBounceRenderArtifact.MaximumDriftToTargetSignalRatio;
            return new DungeonPortalReceiverBounceRenderArtifact.TargetSignalEvidence
            {
                measured = true,
                finite = true,
                sampleCount = supportedIndices.Length,
                meanAbsoluteRgb = mean,
                rootMeanSquareRgb = rms,
                meanDriftToTargetRatio = meanRatio,
                rmsDriftToTargetRatio = rmsRatio,
                driftBelowTargetRatioPolicy = ratioPass,
                unavailableReason = string.Empty
            };
        }

        private static DungeonPortalReceiverBounceRenderArtifact.TargetSignalEvidence UnavailableTargetSignal(string reason)
        {
            return new DungeonPortalReceiverBounceRenderArtifact.TargetSignalEvidence
            {
                measured = false,
                finite = false,
                sampleCount = 0,
                meanAbsoluteRgb = 0f,
                rootMeanSquareRgb = 0f,
                meanDriftToTargetRatio = 0f,
                rmsDriftToTargetRatio = 0f,
                driftBelowTargetRatioPolicy = false,
                unavailableReason = reason ?? "Target signal is unavailable."
            };
        }

        private static bool TryReadLinearHdrPixels(Texture2D source, out Color[] pixels, out string error)
        {
            pixels = null;
            error = string.Empty;
            if (source == null || source.width <= 0 || source.height <= 0)
            {
                error = "source HDR texture is null or has invalid dimensions";
                return false;
            }

            RenderTexture temporary = null;
            Texture2D readback = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                // Persisted EXR importers intentionally stay non-readable.  This GPU
                // blit/readback does not alter importer state or the source asset.
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
                    error = "temporary HDR readback had an unexpected pixel count";
                    pixels = null;
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
                DestroyTransient(readback);
                if (temporary != null)
                    RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static void AssertCurrentPipelineMatches(DungeonPortalReceiverResponseCapture.CaptureState state)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
                pipeline = GraphicsSettings.defaultRenderPipeline;
            if (pipeline == null)
                throw new InvalidOperationException("Stage A requires a persisted active render pipeline asset.");
            string path = AssetDatabase.GetAssetPath(pipeline);
            string hash = GetDependencyHash(path);
            DungeonPortalReceiverResponseCapture.FixedCameraCapture[] cameras = state.fixedCameraCaptures ??
                Array.Empty<DungeonPortalReceiverResponseCapture.FixedCameraCapture>();
            if (cameras.Length != DungeonPortalReceiverBounceRenderArtifact.FixedCameraCount)
                throw new InvalidOperationException("Capture has no canonical fixed-camera pipeline binding.");
            for (int i = 0; i < cameras.Length; i++)
            {
                if (!string.Equals(path, cameras[i].renderPipelineAssetPath, StringComparison.Ordinal) ||
                    !string.Equals(hash, cameras[i].renderPipelineDependencyHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Active render-pipeline path/hash differs from persisted DirectOnly camera '" +
                                                        cameras[i].cameraId + "'.");
                }
            }
        }

        private static void SetUrpRendererIndex(UniversalAdditionalCameraData data, int expectedIndex)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            data.SetRenderer(expectedIndex);
            var serialized = new SerializedObject(data);
            serialized.Update();
            SerializedProperty property = serialized.FindProperty("m_RendererIndex");
            if (property == null)
                throw new InvalidOperationException("URP additional camera data has no renderer-index serialization field.");
            property.intValue = expectedIndex;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (property.intValue != expectedIndex)
                throw new InvalidOperationException("Unable to apply persisted URP renderer index to Stage A camera.");
        }

        private static int GetUrpRendererIndex(UniversalAdditionalCameraData data)
        {
            if (data == null)
                return -1;
            var serialized = new SerializedObject(data);
            SerializedProperty property = serialized.FindProperty("m_RendererIndex");
            return property != null ? property.intValue : -1;
        }

        private static string GetDependencyHash(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                assetPath.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException("Expected a canonical project asset path for dependency hashing.");
            }
            Hash128 hash = AssetDatabase.GetAssetDependencyHash(assetPath);
            string result = hash.ToString();
            if (string.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException("Asset dependency hash is unavailable: '" + assetPath + "'.");
            return result;
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
            throw new InvalidOperationException("Unable to resolve renderer component ordinal.");
        }

        private static string GetStableRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || (target != root && !target.IsChildOf(root)))
                throw new InvalidOperationException("Stable relative path target lies outside its scope root.");
            if (target == root)
                return ".";
            var segments = new List<string>();
            for (Transform current = target; current != null && current != root; current = current.parent)
                segments.Add(current.name + "[" + current.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) + "]");
            if (segments.Count == 0)
                throw new InvalidOperationException("Stable relative path unexpectedly had no child segments.");
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static bool IsInSubtree(Transform candidate, Transform root)
        {
            return candidate != null && root != null && (candidate == root || candidate.IsChildOf(root));
        }

        private static string ComputeUv2Hash(Mesh mesh)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));
            Vector2[] uv2 = mesh.uv2;
            if (uv2 != null && uv2.Length != 0 && uv2.Length != mesh.vertexCount)
                throw new InvalidOperationException("Mesh has partial UV2 and cannot bind DirectOnly renderer mapping safely.");
            var builder = new StringBuilder((uv2 != null ? uv2.Length : 0) * 24 + 256);
            if (uv2 == null || uv2.Length == 0)
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId) ||
                    string.IsNullOrWhiteSpace(guid) || localId == 0L)
                {
                    throw new InvalidOperationException("Mesh without stored UV2 is not a persisted asset.");
                }
                string assetPath = AssetDatabase.GetAssetPath(mesh);
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
            }
            else
            {
                AppendString(builder, "STORED_UV2");
                builder.Append(mesh.vertexCount).Append('|');
                for (int i = 0; i < uv2.Length; i++)
                {
                    AppendFloat(builder, uv2[i].x);
                    AppendFloat(builder, uv2[i].y);
                }
            }
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                var hex = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    hex.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        private static Vector3 NormalizeEuler(Vector3 value)
        {
            return new Vector3(NormalizeAngle(value.x), NormalizeAngle(value.y), NormalizeAngle(value.z));
        }

        private static float NormalizeAngle(float value)
        {
            value %= 360f;
            if (value < 0f)
                value += 360f;
            return Mathf.Abs(value - 360f) <= 0.00001f ? 0f : value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector4 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(Color value)
        {
            return IsFinite(value.r) && IsFinite(value.g) && IsFinite(value.b) && IsFinite(value.a);
        }

        private static bool Approximately(float left, float right)
        {
            return IsFinite(left) && IsFinite(right) && Mathf.Abs(left - right) <= 0.0001f;
        }

        private static bool Approximately(Color left, Color right)
        {
            return Approximately(left.r, right.r) && Approximately(left.g, right.g) &&
                   Approximately(left.b, right.b) && Approximately(left.a, right.a);
        }

        private static bool Approximately(Matrix4x4 left, Matrix4x4 right)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (!Approximately(left[row, column], right[row, column]))
                        return false;
                }
            }
            return true;
        }

        private static void AppendString(StringBuilder builder, string value)
        {
            value = value ?? string.Empty;
            builder.Append(value.Length).Append(':').Append(value).Append('|');
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

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(DungeonPortalReceiverBounceDirectOnlyRenderSession));
        }

        private static void DestroyTransient(UnityEngine.Object value)
        {
            if (value != null)
                UnityEngine.Object.DestroyImmediate(value);
        }

        private struct RendererMappingCandidate
        {
            public Renderer renderer;
            public string relativePath;
            public string typeName;
            public int componentOrdinal;
            public string meshGuid;
            public long meshLocalId;
            public string meshUv2Hash;
            public int rendererBucketIndex;
        }

        private sealed class RendererMappingCandidateComparer : IComparer<RendererMappingCandidate>
        {
            internal static readonly RendererMappingCandidateComparer Instance = new RendererMappingCandidateComparer();

            public int Compare(RendererMappingCandidate left, RendererMappingCandidate right)
            {
                int result = string.CompareOrdinal(left.relativePath, right.relativePath);
                if (result != 0) return result;
                result = string.CompareOrdinal(left.typeName, right.typeName);
                if (result != 0) return result;
                result = left.componentOrdinal.CompareTo(right.componentOrdinal);
                if (result != 0) return result;
                result = string.CompareOrdinal(left.meshGuid, right.meshGuid);
                return result != 0 ? result : left.meshLocalId.CompareTo(right.meshLocalId);
            }
        }

        private sealed class RenderEnvironmentSnapshot
        {
            private readonly bool fog;
            private readonly FogMode fogMode;
            private readonly Color fogColor;
            private readonly float fogDensity;
            private readonly float fogStartDistance;
            private readonly float fogEndDistance;
            private readonly AmbientMode ambientMode;
            private readonly Color ambientSkyColor;
            private readonly Color ambientEquatorColor;
            private readonly Color ambientGroundColor;
            private readonly float ambientIntensity;
            private readonly Material skybox;
            private readonly Light sun;
            private readonly float haloStrength;
            private readonly float flareStrength;
            private readonly float flareFadeSpeed;
            private readonly Cubemap customReflection;
            private readonly DefaultReflectionMode defaultReflectionMode;
            private readonly int defaultReflectionResolution;
            private readonly float reflectionIntensity;
            private readonly int reflectionBounces;
            private readonly Color subtractiveShadowColor;

            private RenderEnvironmentSnapshot()
            {
                fog = RenderSettings.fog;
                fogMode = RenderSettings.fogMode;
                fogColor = RenderSettings.fogColor;
                fogDensity = RenderSettings.fogDensity;
                fogStartDistance = RenderSettings.fogStartDistance;
                fogEndDistance = RenderSettings.fogEndDistance;
                ambientMode = RenderSettings.ambientMode;
                ambientSkyColor = RenderSettings.ambientSkyColor;
                ambientEquatorColor = RenderSettings.ambientEquatorColor;
                ambientGroundColor = RenderSettings.ambientGroundColor;
                ambientIntensity = RenderSettings.ambientIntensity;
                skybox = RenderSettings.skybox;
                sun = RenderSettings.sun;
                haloStrength = RenderSettings.haloStrength;
                flareStrength = RenderSettings.flareStrength;
                flareFadeSpeed = RenderSettings.flareFadeSpeed;
                customReflection = RenderSettings.customReflection;
                defaultReflectionMode = RenderSettings.defaultReflectionMode;
                defaultReflectionResolution = RenderSettings.defaultReflectionResolution;
                reflectionIntensity = RenderSettings.reflectionIntensity;
                reflectionBounces = RenderSettings.reflectionBounces;
                subtractiveShadowColor = RenderSettings.subtractiveShadowColor;
            }

            internal static RenderEnvironmentSnapshot Capture()
            {
                return new RenderEnvironmentSnapshot();
            }

            internal void ApplyToActiveScene()
            {
                RenderSettings.fog = fog;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogStartDistance = fogStartDistance;
                RenderSettings.fogEndDistance = fogEndDistance;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientSkyColor = ambientSkyColor;
                RenderSettings.ambientEquatorColor = ambientEquatorColor;
                RenderSettings.ambientGroundColor = ambientGroundColor;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.skybox = skybox;
                RenderSettings.sun = sun;
                RenderSettings.haloStrength = haloStrength;
                RenderSettings.flareStrength = flareStrength;
                RenderSettings.flareFadeSpeed = flareFadeSpeed;
                RenderSettings.customReflection = customReflection;
                RenderSettings.defaultReflectionMode = defaultReflectionMode;
                RenderSettings.defaultReflectionResolution = defaultReflectionResolution;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.reflectionBounces = reflectionBounces;
                RenderSettings.subtractiveShadowColor = subtractiveShadowColor;
            }

            internal bool MatchesCurrent()
            {
                return RenderSettings.fog == fog && RenderSettings.fogMode == fogMode &&
                       Approximately(RenderSettings.fogColor, fogColor) && Approximately(RenderSettings.fogDensity, fogDensity) &&
                       Approximately(RenderSettings.fogStartDistance, fogStartDistance) &&
                       Approximately(RenderSettings.fogEndDistance, fogEndDistance) &&
                       RenderSettings.ambientMode == ambientMode &&
                       Approximately(RenderSettings.ambientSkyColor, ambientSkyColor) &&
                       Approximately(RenderSettings.ambientEquatorColor, ambientEquatorColor) &&
                       Approximately(RenderSettings.ambientGroundColor, ambientGroundColor) &&
                       Approximately(RenderSettings.ambientIntensity, ambientIntensity) && RenderSettings.skybox == skybox &&
                       RenderSettings.sun == sun && Approximately(RenderSettings.haloStrength, haloStrength) &&
                       Approximately(RenderSettings.flareStrength, flareStrength) &&
                       Approximately(RenderSettings.flareFadeSpeed, flareFadeSpeed) &&
                       RenderSettings.customReflection == customReflection &&
                       RenderSettings.defaultReflectionMode == defaultReflectionMode &&
                       RenderSettings.defaultReflectionResolution == defaultReflectionResolution &&
                       Approximately(RenderSettings.reflectionIntensity, reflectionIntensity) &&
                       RenderSettings.reflectionBounces == reflectionBounces &&
                       Approximately(RenderSettings.subtractiveShadowColor, subtractiveShadowColor);
            }
        }

        private sealed class EditorSessionSnapshot
        {
            private readonly SceneSetup[] sceneSetup;
            private readonly string activeScenePath;
            private readonly UnityEngine.Object[] selection;
            private readonly UnityEngine.Object activeSelection;
            private readonly Lightmapping.BakeOnSceneLoadMode bakeOnSceneLoadMode;
            private readonly LightingSettings lightingSettings;
            private readonly LightingDataAsset lightingDataAsset;
            private readonly LightmapData[] lightmaps;
            private readonly LightmapsMode lightmapsMode;
            private readonly RenderTexture activeRenderTexture;
            private readonly RenderEnvironmentSnapshot environment;

            private EditorSessionSnapshot(
                SceneSetup[] sceneSetup,
                string activeScenePath,
                UnityEngine.Object[] selection,
                UnityEngine.Object activeSelection,
                Lightmapping.BakeOnSceneLoadMode bakeOnSceneLoadMode,
                LightingSettings lightingSettings,
                LightingDataAsset lightingDataAsset,
                LightmapData[] lightmaps,
                LightmapsMode lightmapsMode,
                RenderTexture activeRenderTexture,
                RenderEnvironmentSnapshot environment)
            {
                this.sceneSetup = sceneSetup;
                this.activeScenePath = activeScenePath;
                this.selection = selection;
                this.activeSelection = activeSelection;
                this.bakeOnSceneLoadMode = bakeOnSceneLoadMode;
                this.lightingSettings = lightingSettings;
                this.lightingDataAsset = lightingDataAsset;
                this.lightmaps = lightmaps;
                this.lightmapsMode = lightmapsMode;
                this.activeRenderTexture = activeRenderTexture;
                this.environment = environment;
            }

            internal static EditorSessionSnapshot Capture()
            {
                AssertAssetOnlySelection(Selection.objects);
                AssertAssetOnlySelection(Selection.activeObject != null
                    ? new[] { Selection.activeObject }
                    : Array.Empty<UnityEngine.Object>());
                Scene active = SceneManager.GetActiveScene();
                return new EditorSessionSnapshot(
                    (SceneSetup[])EditorSceneManager.GetSceneManagerSetup().Clone(),
                    active.path,
                    Selection.objects != null ? (UnityEngine.Object[])Selection.objects.Clone() :
                        Array.Empty<UnityEngine.Object>(),
                    Selection.activeObject,
                    Lightmapping.bakeOnSceneLoad,
                    Lightmapping.lightingSettings,
                    Lightmapping.lightingDataAsset,
                    LightmapSettings.lightmaps != null ? (LightmapData[])LightmapSettings.lightmaps.Clone() : null,
                    LightmapSettings.lightmapsMode,
                    RenderTexture.active,
                    RenderEnvironmentSnapshot.Capture());
            }

            private static void AssertAssetOnlySelection(UnityEngine.Object[] values)
            {
                if (values == null)
                    return;
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] != null && !EditorUtility.IsPersistent(values[i]))
                    {
                        throw new InvalidOperationException(
                            "Stage A refuses scene-object selection because exact restoration across isolated scene reload is not guaranteed.");
                    }
                }
            }

            internal void Restore()
            {
                Lightmapping.BakeOnSceneLoadMode desiredBakeOnSceneLoad = bakeOnSceneLoadMode;
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;
                try
                {
                    EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
                    if (Lightmapping.lightingSettings != lightingSettings)
                        Lightmapping.lightingSettings = lightingSettings;
                    if (Lightmapping.lightingDataAsset != lightingDataAsset)
                        Lightmapping.lightingDataAsset = lightingDataAsset;
                    LightmapSettings.lightmapsMode = lightmapsMode;
                    LightmapSettings.lightmaps = lightmaps;
                    if (!string.IsNullOrWhiteSpace(activeScenePath))
                    {
                        Scene active = SceneManager.GetSceneByPath(activeScenePath);
                        if (!active.IsValid() || !active.isLoaded || !EditorSceneManager.SetActiveScene(active))
                            throw new InvalidOperationException("Unable to restore the original active scene.");
                    }
                    Selection.objects = selection ?? Array.Empty<UnityEngine.Object>();
                    Selection.activeObject = activeSelection;
                    RenderTexture.active = activeRenderTexture;
                }
                finally
                {
                    Lightmapping.bakeOnSceneLoad = desiredBakeOnSceneLoad;
                }
            }

            internal void AssertRestoredClean()
            {
                int expectedLoaded = 0;
                for (int i = 0; i < sceneSetup.Length; i++)
                {
                    if (!sceneSetup[i].isLoaded)
                        continue;
                    expectedLoaded++;
                    Scene scene = SceneManager.GetSceneByPath(sceneSetup[i].path);
                    if (!scene.IsValid() || !scene.isLoaded || scene.isDirty)
                    {
                        throw new InvalidOperationException(
                            "Original SceneSetup was not restored cleanly: '" + sceneSetup[i].path + "'.");
                    }
                }
                if (SceneManager.sceneCount != expectedLoaded)
                    throw new InvalidOperationException("Original loaded-scene count was not restored exactly.");
                if (!string.IsNullOrWhiteSpace(activeScenePath) &&
                    !string.Equals(SceneManager.GetActiveScene().path, activeScenePath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Original active scene was not restored.");
                }
                if (Lightmapping.bakeOnSceneLoad != bakeOnSceneLoadMode ||
                    Lightmapping.lightingSettings != lightingSettings ||
                    Lightmapping.lightingDataAsset != lightingDataAsset ||
                    LightmapSettings.lightmapsMode != lightmapsMode ||
                    !SameLightmapLayout(LightmapSettings.lightmaps, lightmaps) ||
                    RenderTexture.active != activeRenderTexture || !environment.MatchesCurrent())
                {
                    throw new InvalidOperationException("Original global lighting/render-target state was not restored exactly.");
                }
                if (Selection.activeObject != activeSelection || !SameObjectSequence(Selection.objects, selection))
                    throw new InvalidOperationException("Original editor selection was not restored.");
            }

            private static bool SameObjectSequence(UnityEngine.Object[] left, UnityEngine.Object[] right)
            {
                int leftCount = left != null ? left.Length : 0;
                int rightCount = right != null ? right.Length : 0;
                if (leftCount != rightCount)
                    return false;
                for (int i = 0; i < leftCount; i++)
                {
                    if (left[i] != right[i])
                        return false;
                }
                return true;
            }

            private static bool SameLightmapLayout(LightmapData[] left, LightmapData[] right)
            {
                int leftCount = left != null ? left.Length : 0;
                int rightCount = right != null ? right.Length : 0;
                if (leftCount != rightCount)
                    return false;
                for (int i = 0; i < leftCount; i++)
                {
                    LightmapData a = left[i];
                    LightmapData b = right[i];
                    if (a == null || b == null)
                    {
                        if (a != b) return false;
                        continue;
                    }
                    if (a.lightmapColor != b.lightmapColor || a.lightmapDir != b.lightmapDir ||
                        a.shadowMask != b.shadowMask)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        internal struct RenderedCameraFrame
        {
            public DungeonPortalReceiverResponseCapture.FixedCameraCapture sourceCamera;
            public Texture2D reconstructedOffHdr;
            public Texture2D stableObjectIdMask;
            public Texture2D doorwayLocalZWorldNormalMask;
            public DungeonPortalReceiverBounceRenderArtifact.DriftMetrics fullFrameDrift;
            public DungeonPortalReceiverBounceRenderArtifact.DriftMetrics supportedOpaqueDrift;
            public DungeonPortalReceiverBounceRenderArtifact.TargetSignalEvidence targetSignal;
            public int supportedOpaquePixelCount;
            public int unsupportedSentinelPixelCount;
            public int backgroundPixelCount;
            public DungeonPortalReceiverBounceRenderArtifact.PixelOrientationEvidence pixelOrientation;
            public string maskUnavailableReason;
        }

        private sealed class RendererMaskInfo
        {
            public Renderer renderer;
            public Mesh mesh;
            public int stableObjectId;
            public string canonicalBucket;
            public bool[] supportedSubmeshes;
            public string[] unsupportedReasons;
            public bool cannotRepresentSilhouette;
        }

        private readonly struct LodMembership
        {
            public LodMembership(LODGroup group, int lodIndex)
            {
                Group = group;
                LodIndex = lodIndex;
            }

            public LODGroup Group { get; }
            public int LodIndex { get; }
        }
    }
}
