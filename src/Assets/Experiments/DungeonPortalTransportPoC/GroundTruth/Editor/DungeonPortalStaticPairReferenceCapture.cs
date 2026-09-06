using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DungeonAdjacentLightingPoC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DungeonPortalTransportPoC.GroundTruth
{
    /// <summary>
    /// Renders the old four-state Start/Admin pair bake as a deliberately limited,
    /// static-room-only reference. The active validation scene is never opened,
    /// saved, or edited. All renderer, light, material, and camera changes happen
    /// on a deep clone in an unsaved preview scene.
    /// </summary>
    public static class DungeonPortalStaticPairReferenceCapture
    {
        public const string Status = "STATIC_ROOM_PAIR_REFERENCE_ONLY";

        private const string ValidationScenePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes/Start_Admin_PortalTransportValidation.unity";
        private const string PairDataPath =
            "Assets/Experiments/DungeonAdjacentLightingPoC/Generated/PairBake/Start_Admin_R000_PairBakeData.asset";
        private const string EvidenceRoot =
            "Assets/Experiments/DungeonPortalTransportPoC/Evidence/GroundTruth";

        private const string ValidationRootName = "PortalTransportValidation";
        private const string ProductionRoomsRootName = "01_ProductionRooms";
        private const string PortalTransportRootName = "02_PortalTransportPoC";
        private const string StartRoomName = "StartRoom_R000_ProductionInstance";
        private const string AdministrativeRoomName =
            "AdminstrativeSegregation_R000_ProductionInstance";
        private const string AddedDoorName =
            "Door_SM_A_Door_Placement_ActiveSceneInstance";
        private const string StartCameraName = "Start_to_Admin_FixedCamera_DISABLED";
        private const string AdministrativeCameraName =
            "Admin_to_Start_FixedCamera_DISABLED";

        private const int ExpectedResolvedRendererCount = 269;
        private const int CaptureWidth = 1920;
        private const int CaptureHeight = 1080;

        private static readonly DungeonAdjacentPairLightmapData.PairPowerState[] States =
        {
            DungeonAdjacentPairLightmapData.PairPowerState.P0P0,
            DungeonAdjacentPairLightmapData.PairPowerState.P100P0,
            DungeonAdjacentPairLightmapData.PairPowerState.P0P100,
            DungeonAdjacentPairLightmapData.PairPowerState.P100P100
        };

        private static readonly string[] CameraNames =
        {
            StartCameraName,
            AdministrativeCameraName
        };

        private static readonly EmissionVariant[] EmissionVariants =
        {
            new EmissionVariant(
                "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials/Lamps_01.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_01_P100.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_01_P0_Black.mat"),
            new EmissionVariant(
                "Assets/Prefabs/map_piece/NewPrison/Power_Material/Lamps_02.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_02_P100.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_02_P0_Black.mat"),
            new EmissionVariant(
                "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials/Lamps_05.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P100.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P0_Black.mat"),
            new EmissionVariant(
                "Assets/Prefabs/map_piece/NewPrison/Power_Material/Lamps_05.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P100.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P0_Black.mat")
        };

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Transport PoC/Capture Static Four-State Pair Reference")]
        public static void CaptureFromMenu()
        {
            Debug.Log(CaptureAllStates());
        }

        /// <summary>
        /// Captures four complete pair-power states from both fixed validation cameras.
        /// The return value and generated evidence intentionally carry a reference-only
        /// status and make no visual-equivalence claim.
        /// </summary>
        public static string CaptureAllStates()
        {
            try
            {
                return CaptureAllStatesOrThrow();
            }
            catch (Exception exception)
            {
                return "FAIL static pair reference capture: " + exception;
            }
        }

        private static string CaptureAllStatesOrThrow()
        {
            Scene activeScene = ValidateEditorAndScenePreconditions();
            GameObject sourceRoot = FindUniqueRoot(activeScene, ValidationRootName);
            DungeonAdjacentPairLightmapData pairData =
                AssetDatabase.LoadAssetAtPath<DungeonAdjacentPairLightmapData>(PairDataPath);
            ValidatePairData(pairData);

            var sourceSnapshot = SourceSceneSnapshot.Capture(activeScene, sourceRoot);
            var selectionSnapshot = SelectionSnapshot.Capture();
            var globalLightmapSnapshot = GlobalLightmapSnapshot.Capture();
            int nonPreviewSceneCount = CountLoadedNonPreviewScenes();
            string utcTimestamp = DateTime.UtcNow.ToString(
                "yyyyMMdd'T'HHmmssfff'Z'",
                CultureInfo.InvariantCulture);
            string outputFolder = EvidenceRoot + "/" + utcTimestamp;

            Scene previewScene = default;
            RenderTexture renderTarget = null;
            RenderTexture readbackTarget = null;
            var bufferedImages = new List<BufferedImage>(States.Length * CameraNames.Length);
            var stateEvidence = new List<StateEvidence>(States.Length);
            int resolvedRendererCount = 0;
            bool restored = false;

            try
            {
                previewScene = EditorSceneManager.NewPreviewScene();
                if (!previewScene.IsValid() || !previewScene.isLoaded ||
                    !EditorSceneManager.IsPreviewScene(previewScene))
                {
                    throw new InvalidOperationException(
                        "Unity did not create the required loaded preview scene.");
                }

                GameObject previewRoot = Object.Instantiate(sourceRoot);
                previewRoot.name = ValidationRootName + "__StaticPairReferenceClone";
                previewRoot.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(previewRoot, previewScene);

                if (activeScene.isDirty)
                {
                    throw new InvalidOperationException(
                        "Deep cloning unexpectedly dirtied the active validation scene.");
                }

                PreviewHierarchy hierarchy = PreparePreviewHierarchy(previewRoot, previewScene);
                var rendererInvariants = CloneRendererInvariant.CaptureRooms(
                    hierarchy.StartRoom,
                    hierarchy.AdministrativeRoom);
                var lightInvariants = CloneLightInvariant.CaptureRooms(
                    hierarchy.StartRoom,
                    hierarchy.AdministrativeRoom);
                List<RendererBinding> bindings = ResolveRendererBindings(
                    pairData,
                    hierarchy.StartRoom,
                    hierarchy.AdministrativeRoom);
                resolvedRendererCount = bindings.Count;
                RequireExpectedResolvedCount(pairData, resolvedRendererCount);

                CreateRenderTargets(out renderTarget, out readbackTarget);

                for (int stateIndex = 0; stateIndex < States.Length; stateIndex++)
                {
                    DungeonAdjacentPairLightmapData.PairPowerState state = States[stateIndex];
                    ResolvePowerState(state, out bool startOn, out bool administrativeOn);

                    rendererInvariants.RestoreBaseMaterials();
                    lightInvariants.RestoreBaseEnabledStates();
                    ApplyPowerState(hierarchy.StartRoom, startOn);
                    ApplyPowerState(hierarchy.AdministrativeRoom, administrativeOn);
                    rendererInvariants.AssertExpectedPowerMaterials(startOn, administrativeOn);
                    lightInvariants.AssertExpectedPowerState(startOn, administrativeOn);

                    StateLightmapApplication lightmapApplication = ApplyPairLightmaps(
                        globalLightmapSnapshot,
                        bindings,
                        state);
                    rendererInvariants.AssertStructuralInvariants();

                    var evidence = new StateEvidence(
                        state,
                        startOn,
                        administrativeOn,
                        bindings.Count,
                        lightmapApplication.AppendedUniquePairCount,
                        lightmapApplication.TotalLightmapCount);

                    for (int cameraIndex = 0; cameraIndex < hierarchy.Cameras.Length; cameraIndex++)
                    {
                        Camera camera = hierarchy.Cameras[cameraIndex];
                        BufferedImage image = CaptureCamera(
                            camera,
                            previewScene,
                            state,
                            renderTarget,
                            readbackTarget);
                        bufferedImages.Add(image);
                        evidence.Images.Add(image);
                    }

                    stateEvidence.Add(evidence);
                }

                if (bufferedImages.Count != States.Length * CameraNames.Length)
                {
                    throw new InvalidOperationException(
                        $"Expected 8 buffered images, found {bufferedImages.Count}.");
                }
            }
            finally
            {
                DestroyRenderTexture(renderTarget);
                DestroyRenderTexture(readbackTarget);
                globalLightmapSnapshot.Restore();

                if (previewScene.IsValid() && previewScene.isLoaded)
                    EditorSceneManager.ClosePreviewScene(previewScene);

                selectionSnapshot.RestoreIfChanged();
                restored = true;
            }

            if (!restored)
                throw new InvalidOperationException("Global capture state was not restored.");
            if (SceneManager.GetActiveScene() != activeScene)
                throw new InvalidOperationException("The active scene changed during capture.");
            if (CountLoadedNonPreviewScenes() != nonPreviewSceneCount)
                throw new InvalidOperationException("The loaded scene set changed during capture.");
            if (activeScene.isDirty)
                throw new InvalidOperationException("The validation scene became dirty during capture.");

            globalLightmapSnapshot.AssertRestored();
            sourceSnapshot.AssertUnchanged();
            selectionSnapshot.AssertRestored();

            string manifest = BuildManifest(
                utcTimestamp,
                pairData,
                resolvedRendererCount,
                stateEvidence,
                bufferedImages,
                globalLightmapSnapshot);
            WriteBufferedEvidence(outputFolder, bufferedImages, manifest);

            if (SceneManager.GetActiveScene() != activeScene || activeScene.isDirty)
                throw new InvalidOperationException(
                    "Evidence import changed the active validation scene state.");
            sourceSnapshot.AssertUnchanged();
            selectionSnapshot.AssertRestored();

            return
                Status + "\n" +
                "output=" + outputFolder + "\n" +
                "images=" + bufferedImages.Count + "\n" +
                "resolvedRenderers=" + resolvedRendererCount + "\n" +
                "expectedResolvedRenderers=" + ExpectedResolvedRendererCount;
        }

        private static Scene ValidateEditorAndScenePreconditions()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Edit Mode is required; Play Mode was not changed.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for Editor compilation/import to finish.");
            if (Lightmapping.isRunning)
                throw new InvalidOperationException("A lightmap bake is currently running.");
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32))
            {
                throw new InvalidOperationException("Required RGBA32 capture formats are unavailable.");
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded ||
                !string.Equals(NormalizePath(activeScene.path), ValidationScenePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The exact validation scene must already be active: " + ValidationScenePath);
            }
            if (activeScene.isDirty)
                throw new InvalidOperationException("The validation scene must be clean before capture.");
            if (CountLoadedNonPreviewScenes() != 1)
            {
                throw new InvalidOperationException(
                    "Exactly one non-preview scene may be loaded for deterministic capture.");
            }

            return activeScene;
        }

        private static int CountLoadedNonPreviewScenes()
        {
            int count = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene))
                    count++;
            }
            return count;
        }

        private static GameObject FindUniqueRoot(Scene scene, string rootName)
        {
            GameObject result = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (!string.Equals(roots[i].name, rootName, StringComparison.Ordinal))
                    continue;
                if (result != null)
                    throw new InvalidOperationException("Duplicate scene root: " + rootName);
                result = roots[i];
            }

            if (result == null)
                throw new InvalidOperationException("Missing scene root: " + rootName);
            return result;
        }

        private static void ValidatePairData(DungeonAdjacentPairLightmapData pairData)
        {
            if (pairData == null)
                throw new InvalidOperationException("Missing pair data: " + PairDataPath);
            if (pairData.RendererStates == null ||
                pairData.RendererStates.Length != ExpectedResolvedRendererCount ||
                pairData.CompleteRendererCount != ExpectedResolvedRendererCount)
            {
                throw new InvalidOperationException(
                    $"Pair data must contain exactly {ExpectedResolvedRendererCount} complete renderer entries; " +
                    $"entries={pairData.RendererStates?.Length ?? 0}, " +
                    $"complete={pairData.CompleteRendererCount}.");
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < pairData.RendererStates.Length; i++)
            {
                DungeonAdjacentPairLightmapData.RendererStateSet states =
                    pairData.RendererStates[i];
                string key = BuildRendererKey(
                    states.receiverRoom,
                    states.relativePath,
                    states.rendererBucketIndex);
                if (!keys.Add(key))
                    throw new InvalidOperationException("Duplicate pair-data renderer key: " + key);

                for (int stateIndex = 0; stateIndex < States.Length; stateIndex++)
                {
                    DungeonAdjacentPairLightmapData.LightmapState lightmap =
                        states.GetState(States[stateIndex]);
                    if (!lightmap.IsValid || lightmap.lightmapDirection == null)
                    {
                        throw new InvalidOperationException(
                            $"Incomplete directional pair state for '{key}' at {States[stateIndex]}.");
                    }
                }
            }
        }

        private static PreviewHierarchy PreparePreviewHierarchy(
            GameObject previewRoot,
            Scene previewScene)
        {
            Transform productionRooms = FindUniqueDescendant(
                previewRoot.transform,
                ProductionRoomsRootName,
                directChildOnly: true);
            Transform portalTransport = FindUniqueDescendant(
                previewRoot.transform,
                PortalTransportRootName,
                directChildOnly: true);
            portalTransport.gameObject.SetActive(false);

            Transform startRoom = FindUniqueDescendant(
                productionRooms,
                StartRoomName,
                directChildOnly: true);
            Transform administrativeRoom = FindUniqueDescendant(
                productionRooms,
                AdministrativeRoomName,
                directChildOnly: true);

            Transform addedDoor = FindUniqueDescendant(
                previewRoot.transform,
                AddedDoorName,
                directChildOnly: false);
            Object.DestroyImmediate(addedDoor.gameObject);

            var cameras = new Camera[CameraNames.Length];
            for (int i = 0; i < CameraNames.Length; i++)
            {
                Transform cameraTransform = FindUniqueDescendant(
                    previewRoot.transform,
                    CameraNames[i],
                    directChildOnly: false);
                Camera camera = cameraTransform.GetComponent<Camera>();
                if (camera == null || camera.enabled)
                {
                    throw new InvalidOperationException(
                        "Fixed camera must exist and remain disabled: " + CameraNames[i]);
                }
                AssertUrpAdditionalCameraData(camera);
                camera.scene = previewScene;
                cameras[i] = camera;
            }

            Camera[] allCameras = previewRoot.GetComponentsInChildren<Camera>(true);
            if (allCameras.Length != CameraNames.Length)
            {
                throw new InvalidOperationException(
                    $"Expected exactly {CameraNames.Length} cloned cameras, found {allCameras.Length}.");
            }

            return new PreviewHierarchy(
                startRoom.gameObject,
                administrativeRoom.gameObject,
                cameras);
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
                    Transform descendant = descendants[i];
                    if (!string.Equals(descendant.name, name, StringComparison.Ordinal))
                        continue;
                    if (result != null)
                        throw new InvalidOperationException("Duplicate descendant: " + name);
                    result = descendant;
                }
            }

            if (result == null)
                throw new InvalidOperationException("Missing hierarchy object: " + name);
            return result;
        }

        private static void AssertUrpAdditionalCameraData(Camera camera)
        {
            Component[] components = camera.GetComponents<Component>();
            int count = 0;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null &&
                    string.Equals(
                        component.GetType().FullName,
                        "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData",
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }

            if (count != 1)
            {
                throw new InvalidOperationException(
                    $"Fixed camera '{camera.name}' must clone exactly one URP additional-camera component; " +
                    $"found {count}.");
            }
        }

        private static List<RendererBinding> ResolveRendererBindings(
            DungeonAdjacentPairLightmapData pairData,
            GameObject startRoom,
            GameObject administrativeRoom)
        {
            var bindings = new List<RendererBinding>(ExpectedResolvedRendererCount);
            var resolvedKeys = new HashSet<string>(StringComparer.Ordinal);
            ResolveRoomBindings(
                pairData,
                startRoom,
                DungeonAdjacentPairLightmapData.RoomRole.Start,
                bindings,
                resolvedKeys);
            ResolveRoomBindings(
                pairData,
                administrativeRoom,
                DungeonAdjacentPairLightmapData.RoomRole.Administrative,
                bindings,
                resolvedKeys);
            return bindings;
        }

        private static void ResolveRoomBindings(
            DungeonAdjacentPairLightmapData pairData,
            GameObject room,
            DungeonAdjacentPairLightmapData.RoomRole role,
            List<RendererBinding> bindings,
            HashSet<string> resolvedKeys)
        {
            MeshRenderer[] renderers = room.GetComponentsInChildren<MeshRenderer>(true);
            var pathUseCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                string relativePath = AnimationUtility.CalculateTransformPath(
                    renderer.transform,
                    room.transform);

                // This increment deliberately happens before the eligibility test.
                // The pair baker used the same ordering, so disabled/ineligible duplicate
                // paths still consume their original deterministic bucket.
                pathUseCounts.TryGetValue(relativePath, out int bucketIndex);
                pathUseCounts[relativePath] = bucketIndex + 1;

                if (!DungeonAdjacentRendererUtility.TryGetEligibleMesh(
                        renderer,
                        out MeshFilter filter,
                        out Mesh mesh))
                {
                    continue;
                }

                if (!pairData.TryGetRendererStates(
                        role,
                        relativePath,
                        bucketIndex,
                        out DungeonAdjacentPairLightmapData.RendererStateSet states))
                {
                    continue;
                }

                if (mesh.vertexCount != states.vertexCount)
                {
                    throw new InvalidOperationException(
                        $"Vertex-count mismatch for {role}:{relativePath}[{bucketIndex}]: " +
                        $"clone={mesh.vertexCount}, pair={states.vertexCount}.");
                }
                if (filter.sharedMesh != mesh)
                    throw new InvalidOperationException("Eligible mesh resolution changed unexpectedly.");

                string key = BuildRendererKey(role, relativePath, bucketIndex);
                if (!resolvedKeys.Add(key))
                    throw new InvalidOperationException("Pair-data key resolved more than once: " + key);
                bindings.Add(new RendererBinding(renderer, states, key));
            }
        }

        private static void RequireExpectedResolvedCount(
            DungeonAdjacentPairLightmapData pairData,
            int resolvedCount)
        {
            if (resolvedCount != ExpectedResolvedRendererCount ||
                resolvedCount != pairData.RendererStates.Length)
            {
                throw new InvalidOperationException(
                    $"Expected {ExpectedResolvedRendererCount} resolved pair renderers, " +
                    $"resolved {resolvedCount} of {pairData.RendererStates.Length}.");
            }
        }

        private static StateLightmapApplication ApplyPairLightmaps(
            GlobalLightmapSnapshot snapshot,
            List<RendererBinding> bindings,
            DungeonAdjacentPairLightmapData.PairPowerState state)
        {
            var lightmaps = new List<LightmapData>(snapshot.Lightmaps.Length + 32);
            lightmaps.AddRange(snapshot.Lightmaps);
            var indices = new Dictionary<LightmapTexturePair, int>();

            for (int i = 0; i < snapshot.Lightmaps.Length; i++)
            {
                LightmapData existing = snapshot.Lightmaps[i];
                if (existing == null || existing.lightmapColor == null)
                    continue;
                var key = new LightmapTexturePair(existing.lightmapColor, existing.lightmapDir);
                if (!indices.ContainsKey(key))
                    indices.Add(key, i);
            }

            int appended = 0;
            for (int i = 0; i < bindings.Count; i++)
            {
                RendererBinding binding = bindings[i];
                DungeonAdjacentPairLightmapData.LightmapState lightmap =
                    binding.States.GetState(state);
                if (!lightmap.IsValid || lightmap.lightmapDirection == null)
                {
                    throw new InvalidOperationException(
                        $"Missing full directional pair state {state} for {binding.Key}.");
                }

                var pair = new LightmapTexturePair(
                    lightmap.lightmapColor,
                    lightmap.lightmapDirection);
                if (!indices.TryGetValue(pair, out int lightmapIndex))
                {
                    lightmapIndex = lightmaps.Count;
                    lightmaps.Add(new LightmapData
                    {
                        lightmapColor = lightmap.lightmapColor,
                        lightmapDir = lightmap.lightmapDirection,
                        shadowMask = null
                    });
                    indices.Add(pair, lightmapIndex);
                    appended++;
                }
                else
                {
                    LightmapData reused = lightmaps[lightmapIndex];
                    if (reused != null && reused.shadowMask != null)
                    {
                        throw new InvalidOperationException(
                            "A deduplicated color/direction pair unexpectedly carries a shadowmask.");
                    }
                }

                binding.Renderer.lightmapIndex = lightmapIndex;
                binding.Renderer.lightmapScaleOffset = lightmap.lightmapScaleOffset;
            }

            LightmapSettings.lightmaps = lightmaps.ToArray();
            LightmapSettings.lightmapsMode = LightmapsMode.CombinedDirectional;
            return new StateLightmapApplication(appended, lightmaps.Count);
        }

        private static void ResolvePowerState(
            DungeonAdjacentPairLightmapData.PairPowerState state,
            out bool startOn,
            out bool administrativeOn)
        {
            startOn = state == DungeonAdjacentPairLightmapData.PairPowerState.P100P0 ||
                      state == DungeonAdjacentPairLightmapData.PairPowerState.P100P100;
            administrativeOn = state == DungeonAdjacentPairLightmapData.PairPowerState.P0P100 ||
                               state == DungeonAdjacentPairLightmapData.PairPowerState.P100P100;
        }

        private static void ApplyPowerState(GameObject room, bool powerOn)
        {
            Light[] lights = room.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                IgnoreLightControl marker = light.GetComponentInParent<IgnoreLightControl>(true);
                if (marker != null && marker.enabled)
                    continue;
                light.enabled = powerOn;
            }

            Renderer[] renderers = room.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                IgnoreEmissionControl marker =
                    renderer.GetComponentInParent<IgnoreEmissionControl>(true);
                if (marker != null && marker.enabled)
                    continue;

                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material replacement = ResolvePowerMaterial(materials[materialIndex], powerOn);
                    if (replacement == null || replacement == materials[materialIndex])
                        continue;
                    materials[materialIndex] = replacement;
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }
        }

        private static Material ResolvePowerMaterial(Material current, bool powerOn)
        {
            if (current == null)
                return null;
            string currentPath = NormalizePath(AssetDatabase.GetAssetPath(current));
            for (int i = 0; i < EmissionVariants.Length; i++)
            {
                EmissionVariant variant = EmissionVariants[i];
                if (!string.Equals(currentPath, variant.Source, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(currentPath, variant.Power100, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(currentPath, variant.Power0, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string targetPath = powerOn ? variant.Power100 : variant.Power0;
                Material target = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
                if (target == null)
                    throw new InvalidOperationException("Missing emission material: " + targetPath);
                return target;
            }

            return current;
        }

        private static void CreateRenderTargets(
            out RenderTexture renderTarget,
            out RenderTexture readbackTarget)
        {
            int requestedMsaa = Mathf.Max(1, QualitySettings.antiAliasing);
            if (requestedMsaa != 1 && requestedMsaa != 2 &&
                requestedMsaa != 4 && requestedMsaa != 8)
            {
                requestedMsaa = 1;
            }

            renderTarget = new RenderTexture(
                CaptureWidth,
                CaptureHeight,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "StaticPairReference_Render",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = requestedMsaa,
                useMipMap = false,
                autoGenerateMips = false
            };
            readbackTarget = new RenderTexture(
                CaptureWidth,
                CaptureHeight,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "StaticPairReference_Readback",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };

            if (!renderTarget.Create() || !readbackTarget.Create())
                throw new InvalidOperationException("Unable to create reference capture targets.");
        }

        private static BufferedImage CaptureCamera(
            Camera camera,
            Scene previewScene,
            DungeonAdjacentPairLightmapData.PairPowerState state,
            RenderTexture renderTarget,
            RenderTexture readbackTarget)
        {
            if (camera == null || camera.enabled)
                throw new InvalidOperationException("Only a disabled cloned fixed camera may render.");
            if (camera.gameObject.scene != previewScene || camera.scene != previewScene)
                throw new InvalidOperationException("Fixed camera is not isolated to the preview scene.");

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            float previousAspect = camera.aspect;
            Matrix4x4 previousProjection = camera.projectionMatrix;
            Texture2D readback = null;
            try
            {
                camera.targetTexture = renderTarget;
                camera.aspect = CaptureWidth / (float)CaptureHeight;
                camera.ResetProjectionMatrix();
                renderTarget.DiscardContents();
                camera.Render();
                Graphics.Blit(renderTarget, readbackTarget);

                RenderTexture.active = readbackTarget;
                readback = new Texture2D(
                    CaptureWidth,
                    CaptureHeight,
                    TextureFormat.RGBA32,
                    false,
                    false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                readback.ReadPixels(
                    new Rect(0f, 0f, CaptureWidth, CaptureHeight),
                    0,
                    0,
                    false);
                readback.Apply(false, false);
                byte[] png = readback.EncodeToPNG();
                if (png == null || png.Length == 0)
                    throw new InvalidOperationException("PNG encoding returned no bytes.");

                string filename =
                    state + "__" + SanitizeFilename(camera.name) + "__" + Status + ".png";
                return new BufferedImage(
                    state,
                    camera.name,
                    filename,
                    png,
                    ComputeSha256(png),
                    camera.transform.position,
                    camera.transform.rotation,
                    camera.fieldOfView,
                    camera.nearClipPlane,
                    camera.farClipPlane,
                    camera.cullingMask,
                    camera.allowHDR,
                    camera.allowMSAA,
                    renderTarget.antiAliasing);
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                camera.aspect = previousAspect;
                camera.projectionMatrix = previousProjection;
                if (readback != null)
                    Object.DestroyImmediate(readback);
            }
        }

        private static void DestroyRenderTexture(RenderTexture target)
        {
            if (target == null)
                return;
            target.Release();
            Object.DestroyImmediate(target);
        }

        private static string BuildManifest(
            string utcTimestamp,
            DungeonAdjacentPairLightmapData pairData,
            int resolvedRendererCount,
            List<StateEvidence> stateEvidence,
            List<BufferedImage> images,
            GlobalLightmapSnapshot globalLightmapSnapshot)
        {
            var builder = new StringBuilder(8192);
            builder.AppendLine("status=" + Status);
            builder.AppendLine("createdUtc=" + utcTimestamp);
            builder.AppendLine("validationScene=" + ValidationScenePath);
            builder.AppendLine("pairData=" + PairDataPath);
            builder.AppendLine("pairId=" + pairData.PairId);
            builder.AppendLine("resolvedRenderers=" + resolvedRendererCount);
            builder.AppendLine("expectedResolvedRenderers=" + ExpectedResolvedRendererCount);
            builder.AppendLine("imageCount=" + images.Count);
            builder.AppendLine("captureWidth=" + CaptureWidth);
            builder.AppendLine("captureHeight=" + CaptureHeight);
            builder.AppendLine("rawPngDimensionsVerified=true");
            builder.AppendLine("unityImporterNpotDisplaySizeIsNotUsedAsRawPngEvidence=true");
            builder.AppendLine("lightmapsModeDuringCapture=" + LightmapsMode.CombinedDirectional);
            builder.AppendLine("lightmapsModeRestored=" + globalLightmapSnapshot.IsModeRestored());
            builder.AppendLine("globalLightmapsRestored=" + globalLightmapSnapshot.AreLightmapsRestored());
            builder.AppendLine("doorCovered=false");
            builder.AppendLine("doorHandling=Added moving door omitted from preview clone because the old pair bake has no door.");
            builder.AppendLine("lightProbeSHCovered=false");
            builder.AppendLine("reflectionCovered=false");
            builder.AppendLine("shadowmaskCovered=false");
            builder.AppendLine("environmentCovered=false");
            builder.AppendLine("environmentNote=Pair bake env differs from current validation.");
            builder.AppendLine("scopeNote=Static room lightmap reference only; this is not a realtime-equivalence verdict.");
            builder.AppendLine("rendererMutation=Clone lightmapIndex/lightmapScaleOffset only, plus exact old-baker lamp emission variants.");
            builder.AppendLine("rendererOverlayMaterial=false");
            builder.AppendLine("materialPropertyBlockMutation=false");
            builder.AppendLine("additionalVertexStreamsMutation=false");
            builder.AppendLine("sourceSceneOpened=false");
            builder.AppendLine("sourceSceneSaved=false");
            builder.AppendLine("playModeChanged=false");

            for (int i = 0; i < stateEvidence.Count; i++)
            {
                StateEvidence state = stateEvidence[i];
                string prefix = "state[" + i + "].";
                builder.AppendLine(prefix + "status=" + Status);
                builder.AppendLine(prefix + "id=" + state.State);
                builder.AppendLine(prefix + "startPower=" + (state.StartOn ? "P100" : "P0"));
                builder.AppendLine(prefix + "administrativePower=" +
                                   (state.AdministrativeOn ? "P100" : "P0"));
                builder.AppendLine(prefix + "resolvedRenderers=" + state.ResolvedRenderers);
                builder.AppendLine(prefix + "appendedUniqueColorDirectionPairs=" +
                                   state.AppendedUniqueColorDirectionPairs);
                builder.AppendLine(prefix + "globalLightmapCount=" + state.TotalLightmapCount);
            }

            for (int i = 0; i < images.Count; i++)
            {
                BufferedImage image = images[i];
                string prefix = "image[" + i + "].";
                builder.AppendLine(prefix + "status=" + Status);
                builder.AppendLine(prefix + "state=" + image.State);
                builder.AppendLine(prefix + "camera=" + image.CameraName);
                builder.AppendLine(prefix + "file=" + image.Filename);
                builder.AppendLine(prefix + "sha256=" + image.Sha256);
                builder.AppendLine(prefix + "bytes=" + image.Bytes.LongLength);
                builder.AppendLine(prefix + "position=" + FormatVector3(image.Position));
                builder.AppendLine(prefix + "rotation=" + FormatQuaternion(image.Rotation));
                builder.AppendLine(prefix + "fieldOfView=" + FormatFloat(image.FieldOfView));
                builder.AppendLine(prefix + "nearClip=" + FormatFloat(image.NearClip));
                builder.AppendLine(prefix + "farClip=" + FormatFloat(image.FarClip));
                builder.AppendLine(prefix + "cullingMask=" + image.CullingMask);
                builder.AppendLine(prefix + "allowHDR=" + image.AllowHdr);
                builder.AppendLine(prefix + "allowMSAA=" + image.AllowMsaa);
                builder.AppendLine(prefix + "renderTargetMSAA=" + image.RenderTargetMsaa);
                builder.AppendLine(prefix + "urpAdditionalCameraDataCloned=true");
                builder.AppendLine(prefix + "postProcessingSettingsCloned=true");
            }

            return builder.ToString();
        }

        private static void WriteBufferedEvidence(
            string outputFolder,
            List<BufferedImage> images,
            string manifest)
        {
            string absoluteFolder = AssetPathToAbsolutePath(outputFolder);
            if (Directory.Exists(absoluteFolder))
                throw new IOException("Refusing to overwrite evidence folder: " + outputFolder);

            Directory.CreateDirectory(absoluteFolder);
            for (int i = 0; i < images.Count; i++)
            {
                BufferedImage image = images[i];
                string absolutePath = Path.Combine(absoluteFolder, image.Filename);
                File.WriteAllBytes(absolutePath, image.Bytes);
                VerifyWrittenPng(absolutePath, image);
            }

            const string manifestName = "manifest_STATIC_ROOM_PAIR_REFERENCE_ONLY.txt";
            File.WriteAllText(
                Path.Combine(absoluteFolder, manifestName),
                manifest,
                new UTF8Encoding(false));

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (int i = 0; i < images.Count; i++)
            {
                string assetPath = outputFolder + "/" + images[i].Filename;
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (imported == null)
                    throw new InvalidOperationException("Imported PNG verification failed: " + assetPath);
            }
            AssetDatabase.ImportAsset(
                outputFolder + "/" + manifestName,
                ImportAssetOptions.ForceSynchronousImport);
        }

        private static void VerifyWrittenPng(string absolutePath, BufferedImage image)
        {
            byte[] written = File.ReadAllBytes(absolutePath);
            if (written.LongLength != image.Bytes.LongLength ||
                !string.Equals(ComputeSha256(written), image.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Written PNG bytes do not match the buffered capture: " + absolutePath);
            }

            if (written.Length < 24 ||
                written[0] != 0x89 || written[1] != 0x50 ||
                written[2] != 0x4E || written[3] != 0x47 ||
                written[4] != 0x0D || written[5] != 0x0A ||
                written[6] != 0x1A || written[7] != 0x0A ||
                written[12] != 0x49 || written[13] != 0x48 ||
                written[14] != 0x44 || written[15] != 0x52)
            {
                throw new InvalidOperationException("Written evidence is not a valid PNG/IHDR stream: " + absolutePath);
            }

            int width = ReadBigEndianInt32(written, 16);
            int height = ReadBigEndianInt32(written, 20);
            if (width != CaptureWidth || height != CaptureHeight)
            {
                throw new InvalidOperationException(
                    $"Written PNG dimensions differ from the capture contract: {absolutePath} " +
                    $"expected={CaptureWidth}x{CaptureHeight}, actual={width}x{height}.");
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
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Expected an Assets-relative path.", nameof(assetPath));
            return Path.GetFullPath(
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    normalized.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string BuildRendererKey(
            DungeonAdjacentPairLightmapData.RoomRole role,
            string relativePath,
            int bucketIndex)
        {
            return role + ":" + relativePath + "[" + bucketIndex + "]";
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
                char character = value[i];
                builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
            }
            return builder.ToString();
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatVector3(Vector3 value)
        {
            return FormatFloat(value.x) + "," + FormatFloat(value.y) + "," + FormatFloat(value.z);
        }

        private static string FormatQuaternion(Quaternion value)
        {
            return FormatFloat(value.x) + "," + FormatFloat(value.y) + "," +
                   FormatFloat(value.z) + "," + FormatFloat(value.w);
        }

        private readonly struct EmissionVariant
        {
            public readonly string Source;
            public readonly string Power100;
            public readonly string Power0;

            public EmissionVariant(string source, string power100, string power0)
            {
                Source = source;
                Power100 = power100;
                Power0 = power0;
            }
        }

        private readonly struct PreviewHierarchy
        {
            public readonly GameObject StartRoom;
            public readonly GameObject AdministrativeRoom;
            public readonly Camera[] Cameras;

            public PreviewHierarchy(
                GameObject startRoom,
                GameObject administrativeRoom,
                Camera[] cameras)
            {
                StartRoom = startRoom;
                AdministrativeRoom = administrativeRoom;
                Cameras = cameras;
            }
        }

        private readonly struct RendererBinding
        {
            public readonly MeshRenderer Renderer;
            public readonly DungeonAdjacentPairLightmapData.RendererStateSet States;
            public readonly string Key;

            public RendererBinding(
                MeshRenderer renderer,
                DungeonAdjacentPairLightmapData.RendererStateSet states,
                string key)
            {
                Renderer = renderer;
                States = states;
                Key = key;
            }
        }

        private readonly struct StateLightmapApplication
        {
            public readonly int AppendedUniquePairCount;
            public readonly int TotalLightmapCount;

            public StateLightmapApplication(int appendedUniquePairCount, int totalLightmapCount)
            {
                AppendedUniquePairCount = appendedUniquePairCount;
                TotalLightmapCount = totalLightmapCount;
            }
        }

        private readonly struct LightmapTexturePair : IEquatable<LightmapTexturePair>
        {
            private readonly Texture2D color;
            private readonly Texture2D direction;

            public LightmapTexturePair(Texture2D color, Texture2D direction)
            {
                this.color = color;
                this.direction = direction;
            }

            public bool Equals(LightmapTexturePair other)
            {
                return ReferenceEquals(color, other.color) &&
                       ReferenceEquals(direction, other.direction);
            }

            public override bool Equals(object obj)
            {
                return obj is LightmapTexturePair other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((color != null ? RuntimeHelpers.GetHashCode(color) : 0) * 397) ^
                           (direction != null ? RuntimeHelpers.GetHashCode(direction) : 0);
                }
            }
        }

        private sealed class BufferedImage
        {
            public readonly DungeonAdjacentPairLightmapData.PairPowerState State;
            public readonly string CameraName;
            public readonly string Filename;
            public readonly byte[] Bytes;
            public readonly string Sha256;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly float FieldOfView;
            public readonly float NearClip;
            public readonly float FarClip;
            public readonly int CullingMask;
            public readonly bool AllowHdr;
            public readonly bool AllowMsaa;
            public readonly int RenderTargetMsaa;

            public BufferedImage(
                DungeonAdjacentPairLightmapData.PairPowerState state,
                string cameraName,
                string filename,
                byte[] bytes,
                string sha256,
                Vector3 position,
                Quaternion rotation,
                float fieldOfView,
                float nearClip,
                float farClip,
                int cullingMask,
                bool allowHdr,
                bool allowMsaa,
                int renderTargetMsaa)
            {
                State = state;
                CameraName = cameraName;
                Filename = filename;
                Bytes = bytes;
                Sha256 = sha256;
                Position = position;
                Rotation = rotation;
                FieldOfView = fieldOfView;
                NearClip = nearClip;
                FarClip = farClip;
                CullingMask = cullingMask;
                AllowHdr = allowHdr;
                AllowMsaa = allowMsaa;
                RenderTargetMsaa = renderTargetMsaa;
            }
        }

        private sealed class StateEvidence
        {
            public readonly DungeonAdjacentPairLightmapData.PairPowerState State;
            public readonly bool StartOn;
            public readonly bool AdministrativeOn;
            public readonly int ResolvedRenderers;
            public readonly int AppendedUniqueColorDirectionPairs;
            public readonly int TotalLightmapCount;
            public readonly List<BufferedImage> Images = new List<BufferedImage>(CameraNames.Length);

            public StateEvidence(
                DungeonAdjacentPairLightmapData.PairPowerState state,
                bool startOn,
                bool administrativeOn,
                int resolvedRenderers,
                int appendedUniqueColorDirectionPairs,
                int totalLightmapCount)
            {
                State = state;
                StartOn = startOn;
                AdministrativeOn = administrativeOn;
                ResolvedRenderers = resolvedRenderers;
                AppendedUniqueColorDirectionPairs = appendedUniqueColorDirectionPairs;
                TotalLightmapCount = totalLightmapCount;
            }
        }

        private sealed class GlobalLightmapSnapshot
        {
            public readonly LightmapData[] Lightmaps;
            private readonly LightmapsMode mode;

            private GlobalLightmapSnapshot(LightmapData[] lightmaps, LightmapsMode mode)
            {
                Lightmaps = lightmaps;
                this.mode = mode;
            }

            public static GlobalLightmapSnapshot Capture()
            {
                return new GlobalLightmapSnapshot(
                    LightmapSettings.lightmaps ?? Array.Empty<LightmapData>(),
                    LightmapSettings.lightmapsMode);
            }

            public void Restore()
            {
                LightmapSettings.lightmaps = Lightmaps;
                LightmapSettings.lightmapsMode = mode;
            }

            public bool AreLightmapsRestored()
            {
                LightmapData[] current = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
                if (current.Length != Lightmaps.Length)
                    return false;
                for (int i = 0; i < current.Length; i++)
                {
                    if (!ReferenceEquals(current[i], Lightmaps[i]))
                        return false;
                }
                return true;
            }

            public bool IsModeRestored()
            {
                return LightmapSettings.lightmapsMode == mode;
            }

            public void AssertRestored()
            {
                if (!AreLightmapsRestored() || !IsModeRestored())
                    throw new InvalidOperationException("Global lightmap state was not restored exactly.");
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
                    throw new InvalidOperationException("The user selection changed during capture.");
            }

            private bool MatchesCurrent()
            {
                Object[] current = Selection.objects;
                if (current.Length != objects.Length || Selection.activeObject != activeObject)
                    return false;
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i] != objects[i])
                        return false;
                }
                return true;
            }
        }

        private sealed class CloneRendererInvariant
        {
            private readonly Entry[] entries;
            private readonly HashSet<Renderer> startRenderers;

            private CloneRendererInvariant(Entry[] entries, HashSet<Renderer> startRenderers)
            {
                this.entries = entries;
                this.startRenderers = startRenderers;
            }

            public static CloneRendererInvariant CaptureRooms(
                GameObject startRoom,
                GameObject administrativeRoom)
            {
                Renderer[] start = startRoom.GetComponentsInChildren<Renderer>(true);
                Renderer[] administrative =
                    administrativeRoom.GetComponentsInChildren<Renderer>(true);
                var all = new List<Renderer>(start.Length + administrative.Length);
                all.AddRange(start);
                all.AddRange(administrative);
                var startSet = new HashSet<Renderer>(start);
                var entries = new Entry[all.Count];
                for (int i = 0; i < all.Count; i++)
                    entries[i] = Entry.Capture(all[i]);
                return new CloneRendererInvariant(entries, startSet);
            }

            public void RestoreBaseMaterials()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].RestoreBaseMaterials();
            }

            public void AssertExpectedPowerMaterials(bool startOn, bool administrativeOn)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    Entry entry = entries[i];
                    bool powerOn = startRenderers.Contains(entry.Renderer)
                        ? startOn
                        : administrativeOn;
                    entry.AssertExpectedPowerMaterials(powerOn);
                }
            }

            public void AssertStructuralInvariants()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].AssertStructuralInvariant();
            }

            private sealed class Entry
            {
                public readonly Renderer Renderer;
                private readonly Material[] baseMaterials;
                private readonly MeshFilter meshFilter;
                private readonly Mesh baseMesh;
                private readonly MeshRenderer meshRenderer;
                private readonly Mesh additionalVertexStreams;
                private readonly bool ignoreEmissionControl;

                private Entry(
                    Renderer renderer,
                    Material[] baseMaterials,
                    MeshFilter meshFilter,
                    Mesh baseMesh,
                    MeshRenderer meshRenderer,
                    Mesh additionalVertexStreams,
                    bool ignoreEmissionControl)
                {
                    Renderer = renderer;
                    this.baseMaterials = baseMaterials;
                    this.meshFilter = meshFilter;
                    this.baseMesh = baseMesh;
                    this.meshRenderer = meshRenderer;
                    this.additionalVertexStreams = additionalVertexStreams;
                    this.ignoreEmissionControl = ignoreEmissionControl;
                }

                public static Entry Capture(Renderer renderer)
                {
                    if (renderer == null)
                        throw new InvalidOperationException("Clone contains a missing Renderer.");
                    if (renderer.HasPropertyBlock())
                    {
                        throw new InvalidOperationException(
                            "Reference capture refuses a clone renderer with a MaterialPropertyBlock: " +
                            renderer.name);
                    }

                    MeshRenderer meshRenderer = renderer as MeshRenderer;
                    MeshFilter meshFilter = meshRenderer != null
                        ? renderer.GetComponent<MeshFilter>()
                        : null;
                    IgnoreEmissionControl marker =
                        renderer.GetComponentInParent<IgnoreEmissionControl>(true);
                    return new Entry(
                        renderer,
                        renderer.sharedMaterials,
                        meshFilter,
                        meshFilter != null ? meshFilter.sharedMesh : null,
                        meshRenderer,
                        meshRenderer != null ? meshRenderer.additionalVertexStreams : null,
                        marker != null && marker.enabled);
                }

                public void RestoreBaseMaterials()
                {
                    Renderer.sharedMaterials = (Material[])baseMaterials.Clone();
                }

                public void AssertExpectedPowerMaterials(bool powerOn)
                {
                    Material[] current = Renderer.sharedMaterials;
                    if (current.Length != baseMaterials.Length)
                        throw new InvalidOperationException("Clone material-slot count changed.");

                    for (int i = 0; i < current.Length; i++)
                    {
                        Material expected = ignoreEmissionControl
                            ? baseMaterials[i]
                            : ResolvePowerMaterial(baseMaterials[i], powerOn);
                        if (current[i] != expected)
                        {
                            throw new InvalidOperationException(
                                $"Unexpected clone material mutation on '{Renderer.name}' slot {i}.");
                        }
                    }
                }

                public void AssertStructuralInvariant()
                {
                    if (Renderer == null)
                        throw new InvalidOperationException("A cloned renderer was destroyed unexpectedly.");
                    if (Renderer.HasPropertyBlock())
                        throw new InvalidOperationException("A clone MaterialPropertyBlock changed.");
                    if (meshFilter != null && meshFilter.sharedMesh != baseMesh)
                        throw new InvalidOperationException("A clone MeshFilter mesh changed.");
                    if (meshRenderer != null &&
                        meshRenderer.additionalVertexStreams != additionalVertexStreams)
                    {
                        throw new InvalidOperationException(
                            "A clone MeshRenderer additionalVertexStreams value changed.");
                    }
                }
            }
        }

        private sealed class CloneLightInvariant
        {
            private readonly Entry[] entries;
            private readonly HashSet<Light> startLights;

            private CloneLightInvariant(Entry[] entries, HashSet<Light> startLights)
            {
                this.entries = entries;
                this.startLights = startLights;
            }

            public static CloneLightInvariant CaptureRooms(
                GameObject startRoom,
                GameObject administrativeRoom)
            {
                Light[] start = startRoom.GetComponentsInChildren<Light>(true);
                Light[] administrative = administrativeRoom.GetComponentsInChildren<Light>(true);
                var all = new List<Light>(start.Length + administrative.Length);
                all.AddRange(start);
                all.AddRange(administrative);
                var entries = new Entry[all.Count];
                for (int i = 0; i < all.Count; i++)
                    entries[i] = Entry.Capture(all[i]);
                return new CloneLightInvariant(entries, new HashSet<Light>(start));
            }

            public void RestoreBaseEnabledStates()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].Restore();
            }

            public void AssertExpectedPowerState(bool startOn, bool administrativeOn)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    Entry entry = entries[i];
                    bool powerOn = startLights.Contains(entry.Light)
                        ? startOn
                        : administrativeOn;
                    entry.AssertExpected(powerOn);
                }
            }

            private readonly struct Entry
            {
                public readonly Light Light;
                private readonly bool baseEnabled;
                private readonly bool ignoreLightControl;

                private Entry(Light light, bool baseEnabled, bool ignoreLightControl)
                {
                    Light = light;
                    this.baseEnabled = baseEnabled;
                    this.ignoreLightControl = ignoreLightControl;
                }

                public static Entry Capture(Light light)
                {
                    IgnoreLightControl marker = light.GetComponentInParent<IgnoreLightControl>(true);
                    return new Entry(light, light.enabled, marker != null && marker.enabled);
                }

                public void Restore()
                {
                    Light.enabled = baseEnabled;
                }

                public void AssertExpected(bool powerOn)
                {
                    bool expected = ignoreLightControl ? baseEnabled : powerOn;
                    if (Light.enabled != expected)
                        throw new InvalidOperationException("Unexpected clone Light.enabled state.");
                }
            }
        }

        private sealed class SourceSceneSnapshot
        {
            private readonly Scene scene;
            private readonly SourceRendererEntry[] renderers;
            private readonly SourceLightEntry[] lights;
            private readonly SourceCameraEntry[] cameras;

            private SourceSceneSnapshot(
                Scene scene,
                SourceRendererEntry[] renderers,
                SourceLightEntry[] lights,
                SourceCameraEntry[] cameras)
            {
                this.scene = scene;
                this.renderers = renderers;
                this.lights = lights;
                this.cameras = cameras;
            }

            public static SourceSceneSnapshot Capture(Scene scene, GameObject root)
            {
                Renderer[] sourceRenderers = root.GetComponentsInChildren<Renderer>(true);
                Light[] sourceLights = root.GetComponentsInChildren<Light>(true);
                Camera[] sourceCameras = root.GetComponentsInChildren<Camera>(true);

                var rendererEntries = new SourceRendererEntry[sourceRenderers.Length];
                for (int i = 0; i < sourceRenderers.Length; i++)
                    rendererEntries[i] = SourceRendererEntry.Capture(sourceRenderers[i]);
                var lightEntries = new SourceLightEntry[sourceLights.Length];
                for (int i = 0; i < sourceLights.Length; i++)
                    lightEntries[i] = SourceLightEntry.Capture(sourceLights[i]);
                var cameraEntries = new SourceCameraEntry[sourceCameras.Length];
                for (int i = 0; i < sourceCameras.Length; i++)
                    cameraEntries[i] = SourceCameraEntry.Capture(sourceCameras[i]);

                return new SourceSceneSnapshot(
                    scene,
                    rendererEntries,
                    lightEntries,
                    cameraEntries);
            }

            public void AssertUnchanged()
            {
                if (!scene.IsValid() || !scene.isLoaded || scene.isDirty)
                    throw new InvalidOperationException("The source validation scene changed.");
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].AssertUnchanged();
                for (int i = 0; i < lights.Length; i++)
                    lights[i].AssertUnchanged();
                for (int i = 0; i < cameras.Length; i++)
                    cameras[i].AssertUnchanged();
            }
        }

        private sealed class SourceRendererEntry
        {
            private readonly Renderer renderer;
            private readonly bool enabled;
            private readonly bool forceRenderingOff;
            private readonly Material[] materials;
            private readonly int lightmapIndex;
            private readonly Vector4 lightmapScaleOffset;
            private readonly uint renderingLayerMask;
            private readonly bool hadPropertyBlock;
            private readonly MeshFilter meshFilter;
            private readonly Mesh mesh;
            private readonly MeshRenderer meshRenderer;
            private readonly Mesh additionalVertexStreams;

            private SourceRendererEntry(Renderer renderer)
            {
                this.renderer = renderer;
                enabled = renderer.enabled;
                forceRenderingOff = renderer.forceRenderingOff;
                materials = renderer.sharedMaterials;
                lightmapIndex = renderer.lightmapIndex;
                lightmapScaleOffset = renderer.lightmapScaleOffset;
                renderingLayerMask = renderer.renderingLayerMask;
                hadPropertyBlock = renderer.HasPropertyBlock();
                meshRenderer = renderer as MeshRenderer;
                meshFilter = meshRenderer != null ? renderer.GetComponent<MeshFilter>() : null;
                mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                additionalVertexStreams =
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
                    renderer.lightmapScaleOffset != lightmapScaleOffset ||
                    renderer.renderingLayerMask != renderingLayerMask ||
                    renderer.HasPropertyBlock() != hadPropertyBlock)
                {
                    throw new InvalidOperationException("A source Renderer state changed.");
                }

                Material[] current = renderer.sharedMaterials;
                if (current.Length != materials.Length)
                    throw new InvalidOperationException("A source material-slot count changed.");
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i] != materials[i])
                        throw new InvalidOperationException("A source material reference changed.");
                }
                if (meshFilter != null && meshFilter.sharedMesh != mesh)
                    throw new InvalidOperationException("A source MeshFilter mesh changed.");
                if (meshRenderer != null &&
                    meshRenderer.additionalVertexStreams != additionalVertexStreams)
                {
                    throw new InvalidOperationException(
                        "A source MeshRenderer additionalVertexStreams value changed.");
                }
            }
        }

        private readonly struct SourceLightEntry
        {
            private readonly Light light;
            private readonly bool enabled;
            private readonly int renderingLayerMask;
            private readonly float intensity;
            private readonly Color color;

            private SourceLightEntry(Light light)
            {
                this.light = light;
                enabled = light.enabled;
                renderingLayerMask = light.renderingLayerMask;
                intensity = light.intensity;
                color = light.color;
            }

            public static SourceLightEntry Capture(Light light)
            {
                return new SourceLightEntry(light);
            }

            public void AssertUnchanged()
            {
                if (light == null || light.enabled != enabled ||
                    light.renderingLayerMask != renderingLayerMask ||
                    light.intensity != intensity || light.color != color)
                {
                    throw new InvalidOperationException("A source Light state changed.");
                }
            }
        }

        private readonly struct SourceCameraEntry
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
                return new SourceCameraEntry(camera);
            }

            public void AssertUnchanged()
            {
                if (camera == null || camera.enabled != enabled ||
                    camera.targetTexture != targetTexture ||
                    camera.transform.position != position || camera.transform.rotation != rotation ||
                    camera.cullingMask != cullingMask || camera.fieldOfView != fieldOfView ||
                    camera.nearClipPlane != nearClip || camera.farClipPlane != farClip ||
                    camera.allowHDR != allowHdr || camera.allowMSAA != allowMsaa)
                {
                    throw new InvalidOperationException("A source fixed Camera state changed.");
                }
            }
        }
    }
}
