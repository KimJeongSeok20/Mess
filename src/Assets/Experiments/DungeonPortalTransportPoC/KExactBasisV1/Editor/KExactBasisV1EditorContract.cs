using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

namespace DungeonPortalTransportPoC.KExactBasisV1.Editor
{
    /// <summary>
    /// Pinned, fail-closed contracts shared by the isolated KExactBasisV1 authoring,
    /// validation, and smoke-capture tools. Nothing in this class writes production
    /// content or mutates the active source scene.
    /// </summary>
    internal static class KExactBasisV1EditorContract
    {
        internal const string SourceScenePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes/" +
            "Start_Admin_PortalTransportValidation.unity";
        internal const string ExpectedSourceSceneSha256 =
            "F087DD274822D9F8071CE8ADD0E261BC5314B2999EF4BDF3B1C1CB1B5DC4AF79";
        internal const string CorrectedRealtimeManifestPath =
            "Assets/Experiments/DungeonPortalTransportPoC/Evidence/GroundTruth/" +
            "RealtimeProductionState/20260823T172008115Z/" +
            "manifest_REALTIME_DIRECT_POWER_EMISSION_GT_ONLY.txt";
        internal const string ExpectedCorrectedRealtimeManifestSha256 =
            "46DF14C9318C53FB0180D0F05F086DEF08E1B8993970E2642B1A9EDEB6941138";
        internal const string KExactSelectionManifestPath =
            "Assets/Experiments/DungeonPortalTransportPoC/GroundTruth/" +
            "KExactProductionLights/Evidence/20260823T202208288Z/" +
            "manifest_K_EXACT_PRODUCTION_LIGHTS_DIRECT_PILOT_ONLY.txt";
        internal const string ExpectedKExactSelectionManifestSha256 =
            "BF0BE5018FAADE177A315EA4B27102C69BA7DFC3E7732896197FFEEC65BCEBD5";
        internal const string ExpectedSelectionFingerprintSha256 =
            "172845291F29647CE9331C998A3481B793459BA4A7F00EA6599032FEFD3D8234";

        internal const string OwnedRoot =
            "Assets/Experiments/DungeonPortalTransportPoC/KExactBasisV1";
        internal const string SceneFolder = OwnedRoot + "/Scenes";
        internal const string DataFolder = OwnedRoot + "/Data";
        internal const string EvidenceRoot = OwnedRoot + "/Evidence";
        internal const string BuiltScenePath =
            SceneFolder + "/Start_Admin_KExactBasisV1_R8.unity";
        internal const string BuildManifestPath =
            DataFolder + "/build_manifest_KEXACT_BASIS_V1_R8.txt";

        internal const string ValidationRootName = "PortalTransportValidation";
        internal const string ProductionRoomsRootName = "01_ProductionRooms";
        internal const string OldPocRootName = "02_PortalTransportPoC";
        internal const string CamerasRootName = "03_FixedValidationCameras_DISABLED";
        internal const string KExactRuntimeRootName = "05_KExactBasisV1_Runtime";
        internal const string StartRoomName = "StartRoom_R000_ProductionInstance";
        internal const string AdministrativeRoomName =
            "AdminstrativeSegregation_R000_ProductionInstance";
        internal const string DoorRootRelativePath =
            "Doorways/Door_SM_A/DoorWayPoint/" +
            "Door_SM_A_Door_Placement_ActiveSceneInstance";
        internal const string DoorLeafName = "Door_01";
        internal const string StartCameraName = "Start_to_Admin_FixedCamera_DISABLED";
        internal const string AdministrativeCameraName =
            "Admin_to_Start_FixedCamera_DISABLED";

        internal const int ExpectedRendererCount = 389;
        internal const int ExpectedStartLightCount = 24;
        internal const int ExpectedAdministrativeLightCount = 54;
        internal const int ExpectedStartK = 3;
        internal const int ExpectedAdministrativeK = 2;
        // This project defines Rendering Layers 0..7 only. URP masks undefined
        // bits before uploading light data, so portal routing must stay inside the
        // defined range. Production currently owns bits 0/1; the isolated PoC
        // reserves three collision-checked bits from the remaining range.
        internal const uint StartReceiverLayerBit = 1u << 2;
        internal const uint AdministrativeReceiverLayerBit = 1u << 3;
        internal const uint TransportCasterLayerBit = 1u << 4;
        internal const uint DoorReceiverLayerBit = 1u << 5;
        internal const int UnityImportSafeAbsolutePathMaximum = 248;

        private static readonly string[] StartSelectedLightPaths =
        {
            "Ceils[sibling=2]/Ceiling_Lights_DualSided[sibling=1]/" +
            "Spotlight (2)[sibling=2]",
            "Ceils[sibling=2]/Ceiling_Lights_DualSided[sibling=1]/" +
            "Spotlight (3)[sibling=3]",
            "Ceils[sibling=2]/Ceiling_Lights_DualSided[sibling=2]/" +
            "Spotlight (2)[sibling=2]"
        };

        private static readonly string[] AdministrativeSelectedLightPaths =
        {
            "Ceils[sibling=2]/Ceiling_Lights_DualSided (4)[sibling=14]/" +
            "Spotlight (2)[sibling=2]",
            "Ceils[sibling=2]/Ceiling_Lights_DualSided (4)[sibling=14]/" +
            "Spotlight (3)[sibling=3]"
        };

        private static readonly string[] ProtectedProductionAssetPaths =
        {
            SourceScenePath,
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/StartRoom_R000.prefab",
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/" +
            "AdminstrativeSegregation_R000.prefab",
            "Assets/Prefabs/map_piece/NewPrison/SomePicees/" +
            "Door_SM_A_Door_Placement.prefab",
            "Assets/Prefabs/map_piece/NewPrison/Tile_modified/StartRoom.prefab",
            "Assets/Prefabs/map_piece/NewPrison/Tile_modified/" +
            "AdminstrativeSegregation.prefab",
            "Assets/Scripts/Dungeon 1/DungeonMapList.asset",
            "Assets/Prefabs/map_piece/NewPrison/New_Prison_Flow.asset",
            "Assets/Prefabs/map_piece/NewPrison/New_Prison_StartTIle.asset",
            "Assets/Prefabs/map_piece/NewPrison/New_Prison_Tiles.asset",
            "Assets/Prefabs/map_piece/NewPrison/New_Prison_Arch.asset"
        };

        internal static Scene RequireCleanSourceSceneActive()
        {
            RequireStableEditMode();
            if (CountLoadedNonPreviewScenes() != 1 || SceneManager.sceneCount != 1)
            {
                throw new InvalidOperationException(
                    "Exactly one loaded non-preview scene is required; no scene was touched.");
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.isDirty ||
                !string.Equals(NormalizePath(scene.path), SourceScenePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The exact clean source validation scene must be active; no scene was touched.");
            }

            ValidatePinnedInputs();
            return scene;
        }

        internal static Scene RequireCleanBuiltSceneActive()
        {
            RequireStableEditMode();
            if (CountLoadedNonPreviewScenes() != 1 || SceneManager.sceneCount != 1)
            {
                throw new InvalidOperationException(
                    "Exactly one loaded non-preview scene is required; no scene was touched.");
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.isDirty ||
                !string.Equals(NormalizePath(scene.path), BuiltScenePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The exact clean KExactBasisV1 scene must be active; no scene was touched.");
            }

            ValidatePinnedInputs();
            if (!File.Exists(AssetPathToAbsolutePath(BuildManifestPath)))
                throw new FileNotFoundException("KExactBasisV1 build manifest is missing.");
            return scene;
        }

        internal static void RequireStableEditMode()
        {
            if (Application.isPlaying || EditorApplication.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stable Edit Mode is required.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Unity is compiling or updating.");
            if (Lightmapping.isRunning)
                throw new InvalidOperationException("Lightmapping is running.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Close Prefab Stage before continuing.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                    throw new InvalidOperationException(
                        "Loaded scene has unsaved changes: " + scene.path + ".");
            }
        }

        internal static void ValidatePinnedInputs()
        {
            RequireSha(SourceScenePath, ExpectedSourceSceneSha256, "source scene");
            RequireSha(
                CorrectedRealtimeManifestPath,
                ExpectedCorrectedRealtimeManifestSha256,
                "corrected REALTIME manifest");
            RequireSha(
                KExactSelectionManifestPath,
                ExpectedKExactSelectionManifestSha256,
                "K-exact selection manifest");

            string realtime = File.ReadAllText(
                AssetPathToAbsolutePath(CorrectedRealtimeManifestPath));
            if (realtime.IndexOf(
                    "status=REALTIME_DIRECT_POWER_EMISSION_GT_ONLY",
                    StringComparison.Ordinal) < 0 ||
                realtime.IndexOf("stateCameraRecordCount=40", StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    "Corrected REALTIME manifest status/completeness changed.");
            }

            string selection = File.ReadAllText(
                AssetPathToAbsolutePath(KExactSelectionManifestPath));
            if (selection.IndexOf(
                    "status=K_EXACT_PRODUCTION_LIGHTS_DIRECT_PILOT_ONLY",
                    StringComparison.Ordinal) < 0 ||
                selection.IndexOf("strictSelectedStartK=3", StringComparison.Ordinal) < 0 ||
                selection.IndexOf(
                    "strictSelectedAdministrativeK=2",
                    StringComparison.Ordinal) < 0 ||
                selection.IndexOf(
                    "selectionFingerprintSha256=" +
                    ExpectedSelectionFingerprintSha256.ToLowerInvariant(),
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException(
                    "K-exact selection manifest status/K/fingerprint changed.");
            }
        }

        internal static SceneBindings ResolveSceneBindings(
            Scene scene,
            bool expectOldPocActive,
            bool expectKExactRoot,
            bool expectSelectedProductionLightsEnabled = true)
        {
            GameObject root = FindUniqueRoot(scene, ValidationRootName);
            Transform rooms = FindUniqueDescendant(
                root.transform, ProductionRoomsRootName, true);
            Transform oldPoc = FindUniqueDescendant(root.transform, OldPocRootName, true);
            Transform cameras = FindUniqueDescendant(root.transform, CamerasRootName, true);
            Transform start = FindUniqueDescendant(rooms, StartRoomName, true);
            Transform administrative = FindUniqueDescendant(
                rooms, AdministrativeRoomName, true);
            Transform doorRoot = start.Find(DoorRootRelativePath);
            Transform doorLeaf = doorRoot != null ? doorRoot.Find(DoorLeafName) : null;
            if (doorRoot == null || doorLeaf == null)
                throw new InvalidOperationException("Canonical moving-door hierarchy is missing.");

            if (oldPoc.gameObject.activeSelf != expectOldPocActive)
            {
                throw new InvalidOperationException(
                    "Old PoC root activeSelf differs from the required scene contract.");
            }

            Transform kExact = FindOptionalDirectChild(root.transform, KExactRuntimeRootName);
            if ((kExact != null) != expectKExactRoot)
                throw new InvalidOperationException("KExactBasisV1 runtime-root contract changed.");
            if (kExact != null && !kExact.gameObject.activeSelf)
                throw new InvalidOperationException("KExactBasisV1 runtime root must be activeSelf.");

            DungeonPortalDoorAngleSource[] angleSources =
                oldPoc.GetComponentsInChildren<DungeonPortalDoorAngleSource>(true);
            if (angleSources.Length != 1 || !angleSources[0].IsConfigured ||
                angleSources[0].DoorLeaf != doorLeaf)
                throw new InvalidOperationException("Canonical door-angle source changed.");

            DungeonTileLightmapSwitcher startSwitcher =
                start.GetComponent<DungeonTileLightmapSwitcher>();
            DungeonTileLightmapSwitcher administrativeSwitcher =
                administrative.GetComponent<DungeonTileLightmapSwitcher>();
            DungeonTilePowerBakeSet startPower = start.GetComponent<DungeonTilePowerBakeSet>();
            DungeonTilePowerBakeSet administrativePower =
                administrative.GetComponent<DungeonTilePowerBakeSet>();
            if (startSwitcher == null || administrativeSwitcher == null ||
                startPower == null || administrativePower == null ||
                startPower.Power00Bake == null || startPower.Power100Bake == null ||
                administrativePower.Power00Bake == null ||
                administrativePower.Power100Bake == null)
            {
                throw new InvalidOperationException("Production power/lightmap binding changed.");
            }

            Camera[] fixedCameras = ResolveFixedCameras(cameras);
            Renderer[] allRenderers = rooms.GetComponentsInChildren<Renderer>(true);
            if (allRenderers.Length != ExpectedRendererCount)
            {
                throw new InvalidOperationException(
                    "Production renderer count changed. expected=" + ExpectedRendererCount +
                    " actual=" + allRenderers.Length + ".");
            }

            Light[] startLights = start.GetComponentsInChildren<Light>(true);
            Light[] administrativeLights =
                administrative.GetComponentsInChildren<Light>(true);
            if (startLights.Length != ExpectedStartLightCount ||
                administrativeLights.Length != ExpectedAdministrativeLightCount)
            {
                throw new InvalidOperationException("Production Light counts changed.");
            }

            Light[] selectedStart = ResolveSelectedLights(
                start,
                StartSelectedLightPaths,
                "Start",
                expectSelectedProductionLightsEnabled);
            Light[] selectedAdministrative = ResolveSelectedLights(
                administrative,
                AdministrativeSelectedLightPaths,
                "Administrative",
                expectSelectedProductionLightsEnabled);

            return new SceneBindings(
                scene,
                root,
                rooms,
                oldPoc,
                cameras,
                kExact,
                start,
                administrative,
                doorRoot,
                doorLeaf,
                angleSources[0],
                startSwitcher,
                administrativeSwitcher,
                startPower,
                administrativePower,
                fixedCameras,
                selectedStart,
                selectedAdministrative);
        }

        private static Light[] ResolveSelectedLights(
            Transform room,
            string[] expectedPaths,
            string label,
            bool expectEnabled)
        {
            var map = new Dictionary<string, Light>(StringComparer.Ordinal);
            Light[] lights = room.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                string key = GetSiblingIndexedRelativePath(room, lights[i].transform);
                if (!map.TryAdd(key, lights[i]))
                    throw new InvalidOperationException(label + " Light key is duplicated: " + key);
            }

            var result = new Light[expectedPaths.Length];
            for (int i = 0; i < result.Length; i++)
            {
                if (!map.TryGetValue(expectedPaths[i], out result[i]) || result[i] == null)
                    throw new InvalidOperationException(
                        label + " selected production Light is missing: " + expectedPaths[i]);
                if (result[i].enabled != expectEnabled ||
                    result[i].lightmapBakeType != LightmapBakeType.Baked ||
                    result[i].shadows != LightShadows.Soft)
                {
                    throw new InvalidOperationException(
                        label + " selected production Light descriptor changed: " +
                        expectedPaths[i] +
                        ". expectedEnabled=" + expectEnabled +
                        " actualEnabled=" + result[i].enabled + ".");
                }
            }

            return result;
        }

        internal static Camera[] ResolveFixedCameras(Transform cameraRoot)
        {
            Camera start = RequireDirectChildCamera(cameraRoot, StartCameraName);
            Camera administrative = RequireDirectChildCamera(
                cameraRoot, AdministrativeCameraName);
            ValidateFixedCamera(start, 0);
            ValidateFixedCamera(administrative, 1);
            Camera[] all = cameraRoot.GetComponentsInChildren<Camera>(true);
            if (all.Length != 2)
                throw new InvalidOperationException("Exactly two fixed cameras are required.");
            return new[] { start, administrative };
        }

        internal static void ValidateFixedCamera(Camera camera, int index)
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

            if (camera == null || camera.name != expectedName || camera.enabled ||
                camera.targetTexture != null || camera.orthographic ||
                camera.usePhysicalProperties || !camera.allowHDR || !camera.allowMSAA ||
                Vector3.Distance(camera.transform.position, expectedPosition) > 0.00001f ||
                Quaternion.Angle(camera.transform.rotation, expectedRotation) > 0.001f ||
                Vector3.Distance(camera.transform.localScale, Vector3.one) > 0.00001f ||
                !Mathf.Approximately(camera.fieldOfView, 90f) ||
                Mathf.Abs(camera.nearClipPlane - 0.01f) > 0.000001f ||
                !Mathf.Approximately(camera.farClipPlane, 1000f) ||
                camera.cullingMask != 262135)
            {
                throw new InvalidOperationException(
                    "Fixed camera exact contract changed: " + expectedName + ".");
            }

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data == null || data.renderType != CameraRenderType.Base ||
                !data.renderPostProcessing)
            {
                throw new InvalidOperationException(
                    "Fixed camera URP/post-processing contract changed: " + expectedName + ".");
            }

            Matrix4x4 expectedProjection = Matrix4x4.Perspective(
                camera.fieldOfView,
                camera.aspect,
                camera.nearClipPlane,
                camera.farClipPlane);
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
            {
                if (Mathf.Abs(camera.projectionMatrix[row, column] -
                              expectedProjection[row, column]) > 0.00001f)
                {
                    throw new InvalidOperationException(
                        "Fixed camera has a non-standard projection matrix: " +
                        expectedName + ".");
                }
            }
        }

        internal static RendererParityReport AssertRendererParity(
            SceneBindings source,
            SceneBindings candidate)
        {
            Dictionary<string, Renderer> sourceMap = BuildRendererMap(source.ProductionRooms);
            Dictionary<string, Renderer> candidateMap = BuildRendererMap(candidate.ProductionRooms);
            if (sourceMap.Count != ExpectedRendererCount ||
                candidateMap.Count != ExpectedRendererCount ||
                !sourceMap.Keys.OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(
                    candidateMap.Keys.OrderBy(value => value, StringComparer.Ordinal),
                    StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Adjacent-OFF renderer identity/count parity failed.");
            }

            int candidateSceneRendererCount =
                candidate.Root.GetComponentsInChildren<Renderer>(true).Length;
            int additionalRendererCount = candidateSceneRendererCount - sourceMap.Count;
            if (additionalRendererCount != 0 ||
                (candidate.KExactRoot != null &&
                 candidate.KExactRoot.GetComponentsInChildren<Renderer>(true).Length != 0))
            {
                throw new InvalidOperationException(
                    "KExactBasisV1 must add zero renderers. additional=" +
                    additionalRendererCount + ".");
            }

            var canonical = new StringBuilder(sourceMap.Count * 256);
            foreach (string key in sourceMap.Keys.OrderBy(value => value, StringComparer.Ordinal))
            {
                Renderer left = sourceMap[key];
                Renderer right = candidateMap[key];
                AssertRendererEqual(source.ProductionRooms, candidate.ProductionRooms,
                    key, left, right);
                canonical.Append(key).Append('|')
                    .Append(GetRendererFingerprint(source.ProductionRooms, left))
                    .Append('\n');
            }

            return new RendererParityReport(
                sourceMap.Count,
                additionalRendererCount,
                ComputeSha256(Encoding.UTF8.GetBytes(canonical.ToString())));
        }

        internal static string ComputeRendererAggregateFingerprint(Transform productionRooms)
        {
            Dictionary<string, Renderer> map = BuildRendererMap(productionRooms);
            var canonical = new StringBuilder(map.Count * 256);
            foreach (string key in map.Keys.OrderBy(value => value, StringComparer.Ordinal))
            {
                canonical.Append(key).Append('|')
                    .Append(GetRendererFingerprint(productionRooms, map[key]))
                    .Append('\n');
            }
            return ComputeSha256(Encoding.UTF8.GetBytes(canonical.ToString()));
        }

        private static Dictionary<string, Renderer> BuildRendererMap(Transform root)
        {
            var map = new Dictionary<string, Renderer>(StringComparer.Ordinal);
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                string key = GetStableComponentKey(root, renderers[i]);
                if (!map.TryAdd(key, renderers[i]))
                    throw new InvalidOperationException("Duplicate Renderer stable key: " + key);
            }
            return map;
        }

        private static void AssertRendererEqual(
            Transform sourceRoot,
            Transform candidateRoot,
            string key,
            Renderer source,
            Renderer candidate)
        {
            if (source == null || candidate == null || source.GetType() != candidate.GetType() ||
                source.enabled != candidate.enabled ||
                source.forceRenderingOff != candidate.forceRenderingOff ||
                source.shadowCastingMode != candidate.shadowCastingMode ||
                source.receiveShadows != candidate.receiveShadows ||
                source.staticShadowCaster != candidate.staticShadowCaster ||
                source.lightProbeUsage != candidate.lightProbeUsage ||
                source.reflectionProbeUsage != candidate.reflectionProbeUsage ||
                source.motionVectorGenerationMode != candidate.motionVectorGenerationMode ||
                source.allowOcclusionWhenDynamic != candidate.allowOcclusionWhenDynamic ||
                source.renderingLayerMask != candidate.renderingLayerMask ||
                source.rendererPriority != candidate.rendererPriority ||
                source.sortingLayerID != candidate.sortingLayerID ||
                source.sortingOrder != candidate.sortingOrder ||
                source.lightmapIndex != candidate.lightmapIndex ||
                source.lightmapScaleOffset != candidate.lightmapScaleOffset ||
                source.realtimeLightmapIndex != candidate.realtimeLightmapIndex ||
                source.realtimeLightmapScaleOffset != candidate.realtimeLightmapScaleOffset ||
                GetReferencePath(sourceRoot, source.probeAnchor) !=
                GetReferencePath(candidateRoot, candidate.probeAnchor))
            {
                throw new InvalidOperationException(
                    "Renderer setting/lightmap/reflection parity failed: " + key);
            }

            Mesh sourceMesh = ResolveRendererMesh(source);
            Mesh candidateMesh = ResolveRendererMesh(candidate);
            if (sourceMesh != candidateMesh || GetObjectIdentity(sourceMesh) !=
                GetObjectIdentity(candidateMesh))
                throw new InvalidOperationException("Renderer mesh parity failed: " + key);

            MeshRenderer sourceMeshRenderer = source as MeshRenderer;
            MeshRenderer candidateMeshRenderer = candidate as MeshRenderer;
            if ((sourceMeshRenderer == null) != (candidateMeshRenderer == null) ||
                (sourceMeshRenderer != null &&
                 sourceMeshRenderer.additionalVertexStreams !=
                 candidateMeshRenderer.additionalVertexStreams))
            {
                throw new InvalidOperationException(
                    "Renderer additionalVertexStreams parity failed: " + key);
            }

            Material[] sourceMaterials = source.sharedMaterials;
            Material[] candidateMaterials = candidate.sharedMaterials;
            if (sourceMaterials.Length != candidateMaterials.Length)
                throw new InvalidOperationException("Material-array parity failed: " + key);
            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                AssertMaterialEqual(key, i, sourceMaterials[i], candidateMaterials[i]);
            }

            string sourceMpb = ComputeMaterialPropertyBlockFingerprint(source);
            string candidateMpb = ComputeMaterialPropertyBlockFingerprint(candidate);
            if (!string.Equals(sourceMpb, candidateMpb, StringComparison.Ordinal))
                throw new InvalidOperationException("MaterialPropertyBlock parity failed: " + key);
        }

        private static void AssertMaterialEqual(
            string rendererKey,
            int slot,
            Material source,
            Material candidate)
        {
            if (source != candidate)
                throw new InvalidOperationException(
                    "Material reference parity failed: " + rendererKey + " slot=" + slot);
            if (source == null)
                return;
            string[] sourceKeywords = source.shaderKeywords ?? Array.Empty<string>();
            string[] candidateKeywords = candidate.shaderKeywords ?? Array.Empty<string>();
            Array.Sort(sourceKeywords, StringComparer.Ordinal);
            Array.Sort(candidateKeywords, StringComparer.Ordinal);
            if (source.shader != candidate.shader || source.renderQueue != candidate.renderQueue ||
                !sourceKeywords.SequenceEqual(candidateKeywords, StringComparer.Ordinal) ||
                !string.Equals(GetObjectIdentity(source), GetObjectIdentity(candidate),
                    StringComparison.Ordinal) ||
                !string.Equals(GetObjectIdentity(source.shader),
                    GetObjectIdentity(candidate.shader), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Material shader/keyword/queue parity failed: " + rendererKey +
                    " slot=" + slot);
            }
        }

        private static string GetRendererFingerprint(Transform root, Renderer renderer)
        {
            var builder = new StringBuilder(512);
            builder.Append(renderer.GetType().FullName).Append('|')
                .Append(GetObjectIdentity(ResolveRendererMesh(renderer))).Append('|')
                .Append(renderer.enabled).Append('|')
                .Append(renderer.forceRenderingOff).Append('|')
                .Append(renderer.lightmapIndex).Append('|')
                .Append(FormatVector4(renderer.lightmapScaleOffset)).Append('|')
                .Append(renderer.realtimeLightmapIndex).Append('|')
                .Append(FormatVector4(renderer.realtimeLightmapScaleOffset)).Append('|')
                .Append(renderer.lightProbeUsage).Append('|')
                .Append(renderer.reflectionProbeUsage).Append('|')
                .Append(GetReferencePath(root, renderer.probeAnchor)).Append('|')
                .Append(renderer.renderingLayerMask).Append('|')
                .Append(ComputeMaterialPropertyBlockFingerprint(renderer));
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                builder.Append('|').Append(GetObjectIdentity(material));
                if (material != null)
                {
                    string[] keywords = material.shaderKeywords ?? Array.Empty<string>();
                    Array.Sort(keywords, StringComparer.Ordinal);
                    builder.Append('|').Append(GetObjectIdentity(material.shader))
                        .Append('|').Append(material.renderQueue)
                        .Append('|').Append(string.Join(",", keywords));
                }
            }
            return ComputeSha256(Encoding.UTF8.GetBytes(builder.ToString()));
        }

        internal static string ComputeMaterialPropertyBlockFingerprint(Renderer renderer)
        {
            var builder = new StringBuilder(1024);
            builder.Append("has=").Append(renderer.HasPropertyBlock());
            AppendPropertyBlockFingerprint(builder, renderer, -1, null);
            Material[] materials = renderer.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
                AppendPropertyBlockFingerprint(builder, renderer, slot, materials[slot]);
            return ComputeSha256(Encoding.UTF8.GetBytes(builder.ToString()));
        }

        private static void AppendPropertyBlockFingerprint(
            StringBuilder builder,
            Renderer renderer,
            int materialIndex,
            Material material)
        {
            var block = new MaterialPropertyBlock();
            if (materialIndex < 0)
                renderer.GetPropertyBlock(block);
            else
                renderer.GetPropertyBlock(block, materialIndex);
            builder.Append("|slot=").Append(materialIndex)
                .Append(";empty=").Append(block.isEmpty);
            try
            {
                builder.Append(";json=").Append(EditorJsonUtility.ToJson(block, false));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Unable to fingerprint MaterialPropertyBlock JSON.", exception);
            }

            AppendVectorArray(builder, block, "unity_SHAr");
            AppendVectorArray(builder, block, "unity_SHAg");
            AppendVectorArray(builder, block, "unity_SHAb");
            AppendVectorArray(builder, block, "unity_SHBr");
            AppendVectorArray(builder, block, "unity_SHBg");
            AppendVectorArray(builder, block, "unity_SHBb");
            AppendVectorArray(builder, block, "unity_SHC");
            AppendVectorArray(builder, block, "unity_ProbesOcclusion");

            Shader shader = material != null ? material.shader : null;
            if (shader == null)
                return;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                int id = Shader.PropertyToID(name);
                string type = shader.GetPropertyType(i).ToString();
                builder.Append(';').Append(name).Append(':').Append(type).Append('=');
                switch (type)
                {
                    case "Color":
                    case "Vector":
                        builder.Append(FormatVector4(block.GetVector(id)));
                        break;
                    case "Float":
                    case "Range":
                    case "Int":
                        builder.Append(FormatFloat(block.GetFloat(id)));
                        break;
                    case "TexEnv":
                        builder.Append(GetObjectIdentity(block.GetTexture(id)));
                        break;
                    default:
                        builder.Append("unsupported-public-readback");
                        break;
                }
            }
        }

        private static void AppendVectorArray(
            StringBuilder builder,
            MaterialPropertyBlock block,
            string propertyName)
        {
            Vector4[] values = block.GetVectorArray(Shader.PropertyToID(propertyName));
            builder.Append(';').Append(propertyName).Append("[]=");
            if (values == null)
                return;
            for (int i = 0; i < values.Length; i++)
            {
                if (i != 0)
                    builder.Append(',');
                builder.Append(FormatVector4(values[i]));
            }
        }

        private static Mesh ResolveRendererMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        internal static string GetStableComponentKey(Transform root, Component component)
        {
            string path = GetSiblingIndexedRelativePath(root, component.transform);
            Component[] components = component.GetComponents<Component>();
            int ordinal = 0;
            bool found = false;
            for (int i = 0; i < components.Length; i++)
            {
                Component candidate = components[i];
                if (candidate == null || candidate.GetType() != component.GetType())
                    continue;
                if (candidate == component)
                {
                    found = true;
                    break;
                }
                ordinal++;
            }
            if (!found)
                throw new InvalidOperationException("Component ordinal cannot be resolved.");
            return path + "|" + component.GetType().FullName + "[ordinal=" + ordinal + "]";
        }

        internal static string GetSiblingIndexedRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || (target != root && !target.IsChildOf(root)))
                throw new InvalidOperationException("Hierarchy-path input is outside its root.");
            if (target == root)
                return string.Empty;
            var segments = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                segments.Add(current.name + "[sibling=" + current.GetSiblingIndex() + "]");
                current = current.parent;
            }
            if (current != root)
                throw new InvalidOperationException("Hierarchy path escaped its root.");
            segments.Reverse();
            return string.Join("/", segments);
        }

        internal static string GetHumanRelativePath(Transform root, Transform target)
        {
            if (root == target)
                return string.Empty;
            var names = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }
            if (current != root)
                throw new InvalidOperationException("Human path escaped its root.");
            names.Reverse();
            return string.Join("/", names);
        }

        internal static GameObject FindUniqueRoot(Scene scene, string name)
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

        internal static Transform FindUniqueDescendant(
            Transform root,
            string name,
            bool directChild)
        {
            Transform result = null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform candidate = all[i];
                if (candidate == root ||
                    !string.Equals(candidate.name, name, StringComparison.Ordinal) ||
                    (directChild && candidate.parent != root))
                    continue;
                if (result != null)
                    throw new InvalidOperationException("Duplicate transform: " + name);
                result = candidate;
            }
            if (result == null)
                throw new InvalidOperationException("Missing transform: " + name);
            return result;
        }

        internal static Transform FindOptionalDirectChild(Transform root, string name)
        {
            Transform result = null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform candidate = root.GetChild(i);
                if (!string.Equals(candidate.name, name, StringComparison.Ordinal))
                    continue;
                if (result != null)
                    throw new InvalidOperationException("Duplicate direct child: " + name);
                result = candidate;
            }
            return result;
        }

        private static Camera RequireDirectChildCamera(Transform root, string name)
        {
            Transform transform = FindUniqueDescendant(root, name, true);
            Camera camera = transform.GetComponent<Camera>();
            if (camera == null)
                throw new InvalidOperationException("Missing Camera on " + name + ".");
            return camera;
        }

        internal static void EnsureOwnedFolders()
        {
            EnsureFolder(OwnedRoot);
            EnsureFolder(SceneFolder);
            EnsureFolder(DataFolder);
            EnsureFolder(EvidenceRoot);
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            string[] folders = { OwnedRoot, SceneFolder, DataFolder, EvidenceRoot };
            for (int i = 0; i < folders.Length; i++)
            {
                if (!AssetDatabase.IsValidFolder(folders[i]) ||
                    !Directory.Exists(AssetPathToAbsolutePath(folders[i])))
                    throw new IOException(
                        "Unity did not register the exact owned folder: " + folders[i]);
            }
        }

        private static void EnsureFolder(string assetPath)
        {
            string absolute = AssetPathToAbsolutePath(assetPath);
            Directory.CreateDirectory(absolute);
            if (!Directory.Exists(absolute))
                throw new IOException("Unable to create owned folder: " + assetPath);
        }

        internal static void AssertImportSafePath(
            string assetPath,
            bool includeMeta,
            string label)
        {
            string absolute = AssetPathToAbsolutePath(assetPath);
            AssertAbsolutePathLength(absolute, label);
            if (includeMeta)
                AssertAbsolutePathLength(absolute + ".meta", label + " .meta");
        }

        internal static void AssertAbsolutePathLength(string absolutePath, string label)
        {
            string full = Path.GetFullPath(absolutePath);
            if (full.Length > UnityImportSafeAbsolutePathMaximum)
            {
                throw new PathTooLongException(
                    label + " exceeds " + UnityImportSafeAbsolutePathMaximum +
                    " absolute characters (" + full.Length + "): " + full);
            }
        }

        internal static int CountLoadedNonPreviewScenes()
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

        internal static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        internal static string ComputeFileSha256(string assetPath)
        {
            string absolute = AssetPathToAbsolutePath(assetPath);
            if (!File.Exists(absolute))
                throw new FileNotFoundException("Required file is missing.", absolute);
            using (FileStream stream = File.OpenRead(absolute))
            using (SHA256 sha = SHA256.Create())
                return BytesToHex(sha.ComputeHash(stream));
        }

        internal static string ComputeSha256(byte[] bytes)
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

        private static void RequireSha(string path, string expected, string label)
        {
            string actual = ComputeFileSha256(path);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    label + " raw SHA-256 changed. expected=" + expected +
                    " actual=" + actual + ".");
        }

        internal static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        internal static string Sanitize(string value)
        {
            return (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        }

        internal static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        internal static string FormatDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        internal static string FormatVector3(Vector3 value)
        {
            return FormatFloat(value.x) + "," + FormatFloat(value.y) + "," +
                   FormatFloat(value.z);
        }

        internal static string FormatQuaternion(Quaternion value)
        {
            return FormatFloat(value.x) + "," + FormatFloat(value.y) + "," +
                   FormatFloat(value.z) + "," + FormatFloat(value.w);
        }

        internal static string FormatVector4(Vector4 value)
        {
            return FormatFloat(value.x) + "," + FormatFloat(value.y) + "," +
                   FormatFloat(value.z) + "," + FormatFloat(value.w);
        }

        private static string GetReferencePath(Transform root, Transform value)
        {
            if (value == null)
                return "<null>";
            if (value == root || value.IsChildOf(root))
                return GetSiblingIndexedRelativePath(root, value);
            return GetObjectIdentity(value);
        }

        internal static string GetObjectIdentity(Object value)
        {
            if (value == null)
                return "<null>";
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    value, out string guid, out long localId))
                return guid + ":" + localId.ToString(CultureInfo.InvariantCulture);
            return value.GetType().FullName + ":scene:" + value.name;
        }

        internal sealed class SceneBindings
        {
            internal readonly Scene Scene;
            internal readonly GameObject Root;
            internal readonly Transform ProductionRooms;
            internal readonly Transform OldPocRoot;
            internal readonly Transform CameraRoot;
            internal readonly Transform KExactRoot;
            internal readonly Transform StartRoom;
            internal readonly Transform AdministrativeRoom;
            internal readonly Transform DoorRoot;
            internal readonly Transform DoorLeaf;
            internal readonly DungeonPortalDoorAngleSource DoorAngleSource;
            internal readonly DungeonTileLightmapSwitcher StartSwitcher;
            internal readonly DungeonTileLightmapSwitcher AdministrativeSwitcher;
            internal readonly DungeonTilePowerBakeSet StartPowerSet;
            internal readonly DungeonTilePowerBakeSet AdministrativePowerSet;
            internal readonly Camera[] Cameras;
            internal readonly Light[] SelectedStartLights;
            internal readonly Light[] SelectedAdministrativeLights;

            internal SceneBindings(
                Scene scene,
                GameObject root,
                Transform productionRooms,
                Transform oldPocRoot,
                Transform cameraRoot,
                Transform kExactRoot,
                Transform startRoom,
                Transform administrativeRoom,
                Transform doorRoot,
                Transform doorLeaf,
                DungeonPortalDoorAngleSource doorAngleSource,
                DungeonTileLightmapSwitcher startSwitcher,
                DungeonTileLightmapSwitcher administrativeSwitcher,
                DungeonTilePowerBakeSet startPowerSet,
                DungeonTilePowerBakeSet administrativePowerSet,
                Camera[] cameras,
                Light[] selectedStartLights,
                Light[] selectedAdministrativeLights)
            {
                Scene = scene;
                Root = root;
                ProductionRooms = productionRooms;
                OldPocRoot = oldPocRoot;
                CameraRoot = cameraRoot;
                KExactRoot = kExactRoot;
                StartRoom = startRoom;
                AdministrativeRoom = administrativeRoom;
                DoorRoot = doorRoot;
                DoorLeaf = doorLeaf;
                DoorAngleSource = doorAngleSource;
                StartSwitcher = startSwitcher;
                AdministrativeSwitcher = administrativeSwitcher;
                StartPowerSet = startPowerSet;
                AdministrativePowerSet = administrativePowerSet;
                Cameras = cameras;
                SelectedStartLights = selectedStartLights;
                SelectedAdministrativeLights = selectedAdministrativeLights;
            }
        }

        internal readonly struct RendererParityReport
        {
            internal readonly int RendererCount;
            internal readonly int AdditionalRendererCount;
            internal readonly string FingerprintSha256;

            internal RendererParityReport(
                int rendererCount,
                int additionalRendererCount,
                string fingerprintSha256)
            {
                RendererCount = rendererCount;
                AdditionalRendererCount = additionalRendererCount;
                FingerprintSha256 = fingerprintSha256;
            }
        }

        internal sealed class SelectionSnapshot
        {
            private readonly Object[] objects;
            private readonly Object active;

            private SelectionSnapshot(Object[] objects, Object active)
            {
                this.objects = objects;
                this.active = active;
            }

            internal static SelectionSnapshot Capture()
            {
                return new SelectionSnapshot(Selection.objects, Selection.activeObject);
            }

            internal void Restore()
            {
                if (Matches())
                    return;
                Selection.objects = objects;
                Selection.activeObject = active;
            }

            internal void AssertRestored()
            {
                if (!Matches())
                    throw new InvalidOperationException("Editor selection was not restored.");
            }

            private bool Matches()
            {
                Object[] current = Selection.objects;
                if (Selection.activeObject != active || current.Length != objects.Length)
                    return false;
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i] != objects[i])
                        return false;
                }
                return true;
            }
        }

        internal sealed class ProtectedAssetSnapshot
        {
            private readonly Dictionary<string, string> hashes;

            private ProtectedAssetSnapshot(Dictionary<string, string> hashes)
            {
                this.hashes = hashes;
            }

            internal static ProtectedAssetSnapshot Capture()
            {
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < ProtectedProductionAssetPaths.Length; i++)
                {
                    string path = ProtectedProductionAssetPaths[i];
                    result.Add(path, ComputeFileSha256(path));
                    string meta = path + ".meta";
                    if (File.Exists(AssetPathToAbsolutePath(meta)))
                        result.Add(meta, ComputeFileSha256(meta));
                }
                return new ProtectedAssetSnapshot(result);
            }

            internal string FingerprintSha256
            {
                get
                {
                    var builder = new StringBuilder();
                    foreach (KeyValuePair<string, string> pair in
                             hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                        builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
                    return ComputeSha256(Encoding.UTF8.GetBytes(builder.ToString()));
                }
            }

            internal void AssertUnchanged()
            {
                foreach (KeyValuePair<string, string> pair in hashes)
                {
                    string current = ComputeFileSha256(pair.Key);
                    if (!string.Equals(current, pair.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "Protected production asset changed: " + pair.Key + ".");
                    }
                }
                RequireSha(SourceScenePath, ExpectedSourceSceneSha256, "source scene");
            }
        }
    }
}
