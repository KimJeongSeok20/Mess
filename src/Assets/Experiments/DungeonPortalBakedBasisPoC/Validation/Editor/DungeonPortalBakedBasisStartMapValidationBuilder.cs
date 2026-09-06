using System;
using System.IO;
using System.Text;
using DunGen;
using DunGen.Graph;
using DungeonPortalTransportPoC;
using DungeonPortalTransportPoC.KExactBasisV1.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DungeonPortalBakedBasisPoC.Validation.Editor
{
    /// <summary>
    /// Creates a StartMap-entry harness by copying the already-built DPBB pair scene. At runtime
    /// the controller loads the unmodified production StartMap additively and makes it active,
    /// which preserves the DPBB scene's baked-lightmap table and the actual StartMap entry path.
    /// </summary>
    public static class DungeonPortalBakedBasisStartMapValidationBuilder
    {
        private const string Status = "DPBB_STARTMAP_HARNESS_V2_ACTUAL_V2_SURFACES";
        private const string OutputScenePath =
            "Assets/Experiments/DungeonPortalBakedBasisPoC/Scenes/" +
            "Start_Admin_DPBB_StartMapDungeonEntryValidation.unity";
        private const string OutputManifestPath =
            "Assets/Experiments/DungeonPortalBakedBasisPoC/Data/" +
            "build_manifest_DPBB_StartMapDungeonEntryHarness_V2.txt";
        private const string RuntimeRootName = "DPBB_StartMapDungeonEntry_Runtime";

        [MenuItem("Tools/Dungeon/Lighting/Portal Baked Basis PoC/Build StartMap Dungeon-Entry Harness (Edit Mode)")]
        public static void BuildFromMenu()
        {
            string result = Build();
            if (result.StartsWith("PASS", StringComparison.Ordinal)) Debug.Log(result);
            else Debug.LogError(result);
        }

        public static void BuildCli()
        {
            string result = Build();
            if (!result.StartsWith("PASS", StringComparison.Ordinal))
                throw new InvalidOperationException(result);
            Debug.Log(result);
        }

        public static string Build()
        {
            try
            {
                return BuildOrThrow();
            }
            catch (Exception exception)
            {
                return "FAIL " + Status + ": " + exception;
            }
        }

        private static string BuildOrThrow()
        {
            RequireStableEditModeAndCleanLoadedScenes();
            RequireInputFiles();
            RequireOutputDoesNotExist();

            string startMapHashBefore = ComputeFileSha256(
                DungeonPortalBakedBasisStartMapRuntimeController.StartMapScenePath);
            string pairHashBefore = ComputeFileSha256(
                DungeonPortalBakedBasisValidationContract.BuiltScenePath);
            Scene originalActive = SceneManager.GetActiveScene();
            UnityEngine.Object originalSelection = Selection.activeObject;
            string token = Guid.NewGuid().ToString("N").Substring(0, 12);
            string stagingPath = "Assets/Experiments/DungeonPortalBakedBasisPoC/Scenes/_S_StartMap_" +
                                 token + ".unity";
            Scene staging = default;
            bool stagingOpen = false;
            bool published = false;
            try
            {
                if (!AssetDatabase.CopyAsset(DungeonPortalBakedBasisValidationContract.BuiltScenePath, stagingPath))
                    throw new IOException("Could not create the DPBB StartMap harness staging copy.");
                AssetDatabase.ImportAsset(stagingPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                staging = EditorSceneManager.OpenScene(stagingPath, OpenSceneMode.Additive);
                stagingOpen = staging.IsValid() && staging.isLoaded;
                if (!stagingOpen || !EditorSceneManager.SetActiveScene(staging))
                    throw new InvalidOperationException("Could not activate the DPBB StartMap harness staging scene.");

                RemoveValidationOnlyGlobalVolume(staging);

                KExactBasisV1EditorContract.SceneBindings bindings =
                    KExactBasisV1EditorContract.ResolveSceneBindings(staging, false, false, true);
                DungeonPortalBakedBasisConnectionDriver driver =
                    DungeonPortalBakedBasisValidationContract.RequireDriver(staging);
                DungeonPortalBakedBasisDoorShDriver doorSh =
                    DungeonPortalBakedBasisValidationContract.RequireDoorShDriver(staging);
                DungeonPortalBakedBasisReflectionDriver reflection =
                    DungeonPortalBakedBasisValidationContract.RequireReflectionDriver(staging);
                string driverFailure = null;
                string doorFailure = null;
                string reflectionFailure = null;
                bool driverValid = driver.TryValidateConfiguration(out driverFailure);
                bool doorValid = doorSh.TryValidateConfiguration(out doorFailure);
                bool reflectionValid = reflection.TryValidateConfiguration(out reflectionFailure);
                if (!driverValid || !doorValid || !reflectionValid)
                {
                    throw new InvalidOperationException("DPBB pair is not a valid harness input. Driver=" +
                                                        driverFailure + " DoorSH=" + doorFailure +
                                                        " Reflection=" + reflectionFailure);
                }
                AssertNoRejectedV21Overlay(bindings.Root.gameObject);
                if (bindings.Cameras == null || bindings.Cameras.Length != 2 ||
                    bindings.Cameras[0] == null || bindings.Cameras[1] == null ||
                    bindings.Cameras[0].enabled || bindings.Cameras[1].enabled)
                {
                    throw new InvalidOperationException(
                        "DPBB pair must retain exactly two disabled reference cameras; no render fallback is allowed.");
                }
                if (FindUniqueRoot(staging, RuntimeRootName) != null)
                    throw new InvalidOperationException("A StartMap harness runtime root already exists in the input pair.");

                Transform validationStartDoorway = bindings.DoorRoot != null
                    ? bindings.DoorRoot.parent
                    : null;
                Transform validationAdministrativeDoorway = bindings.AdministrativeRoom.Find(
                    "Doorways/Door_SM_A/DoorWayPoint");
                DungeonFlow requiredFlow = AssetDatabase.LoadAssetAtPath<DungeonFlow>(
                    DungeonPortalBakedBasisStartMapRuntimeController.RequiredDungeonFlowAssetPath);
                TileSet requiredStartTileSet = AssetDatabase.LoadAssetAtPath<TileSet>(
                    DungeonPortalBakedBasisStartMapRuntimeController.RequiredStartTileSetAssetPath);
                TileSet requiredAdministrativeTileSet = AssetDatabase.LoadAssetAtPath<TileSet>(
                    DungeonPortalBakedBasisStartMapRuntimeController.RequiredAdministrativeTileSetAssetPath);
                GameObject requiredStartPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    DungeonPortalBakedBasisStartMapRuntimeController.RequiredStartPrefabAssetPath);
                GameObject requiredAdministrativePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    DungeonPortalBakedBasisStartMapRuntimeController.RequiredAdministrativePrefabAssetPath);
                DungeonPortalBakedRoomBasisData requiredStartBasis =
                    AssetDatabase.LoadAssetAtPath<DungeonPortalBakedRoomBasisData>(
                        DungeonPortalBakedBasisValidationContract.StartV2BasisPath);
                DungeonPortalBakedRoomBasisData requiredAdministrativeBasis =
                    AssetDatabase.LoadAssetAtPath<DungeonPortalBakedRoomBasisData>(
                        DungeonPortalBakedBasisValidationContract.AdministrativeV2BasisPath);
                Shader requiredCompositionShader = AssetDatabase.LoadAssetAtPath<Shader>(
                    DungeonPortalBakedBasisValidationContract.CompositionShaderPath);
                if (validationStartDoorway == null || validationAdministrativeDoorway == null ||
                    requiredFlow == null || requiredStartTileSet == null ||
                    requiredAdministrativeTileSet == null || requiredStartPrefab == null ||
                    requiredAdministrativePrefab == null || requiredStartBasis == null ||
                    requiredAdministrativeBasis == null || requiredCompositionShader == null)
                {
                    throw new InvalidOperationException(
                        "Required validation doorway, production Flow/TileSet/prefab, V2 basis, or composition shader is missing.");
                }
                ReconfigureV2ReflectionProfiles(reflection, driver);

                GameObject runtimeRoot = new GameObject(RuntimeRootName);
                SceneManager.MoveGameObjectToScene(runtimeRoot, staging);
                DungeonPortalBakedBasisStartMapRuntimeController controller =
                    runtimeRoot.AddComponent<DungeonPortalBakedBasisStartMapRuntimeController>();
                controller.ConfigureAuthoring(
                    bindings.Root.gameObject,
                    bindings.Cameras[0],
                    bindings.Cameras[1],
                    driver,
                    doorSh,
                    reflection,
                    bindings.StartRoom,
                    bindings.AdministrativeRoom,
                    validationStartDoorway,
                    validationAdministrativeDoorway,
                    requiredFlow,
                    requiredStartTileSet,
                    requiredAdministrativeTileSet,
                    requiredStartPrefab,
                    requiredAdministrativePrefab,
                    requiredStartBasis,
                    requiredAdministrativeBasis,
                    requiredCompositionShader);
                EditorUtility.SetDirty(controller);
                EditorSceneManager.MarkSceneDirty(staging);
                if (!EditorSceneManager.SaveScene(staging, stagingPath, false))
                    throw new IOException("Could not save the DPBB StartMap harness staging scene.");

                if (!EditorSceneManager.CloseScene(staging, true))
                    throw new IOException("Could not close DPBB StartMap harness staging scene.");
                stagingOpen = false;
                RestoreEditorIdentity(originalActive, originalSelection);
                AssertInputsUnchanged(startMapHashBefore, pairHashBefore);

                string moveError = AssetDatabase.MoveAsset(stagingPath, OutputScenePath);
                if (!string.IsNullOrEmpty(moveError))
                    throw new IOException("Could not publish DPBB StartMap harness: " + moveError);
                published = true;
                AssetDatabase.ImportAsset(OutputScenePath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                Scene verify = EditorSceneManager.OpenScene(OutputScenePath, OpenSceneMode.Additive);
                try
                {
                    KExactBasisV1EditorContract.SceneBindings verified =
                        KExactBasisV1EditorContract.ResolveSceneBindings(verify, false, false, true);
                    DungeonPortalBakedBasisStartMapRuntimeController[] controllers =
                        verify.GetRootGameObjects()[FindRootIndex(verify, RuntimeRootName)]
                            .GetComponentsInChildren<DungeonPortalBakedBasisStartMapRuntimeController>(true);
                    if (controllers.Length != 1 || controllers[0] == null ||
                        controllers[0].ValidationPairRoot != verified.Root.gameObject ||
                        controllers[0].ConnectionDriver == null || controllers[0].DoorShDriver == null ||
                        controllers[0].ReflectionDriver == null ||
                        controllers[0].RequiredDungeonFlow != AssetDatabase.LoadAssetAtPath<DungeonFlow>(
                            DungeonPortalBakedBasisStartMapRuntimeController.RequiredDungeonFlowAssetPath) ||
                        controllers[0].RequiredStartTileSet != AssetDatabase.LoadAssetAtPath<TileSet>(
                            DungeonPortalBakedBasisStartMapRuntimeController.RequiredStartTileSetAssetPath) ||
                        controllers[0].RequiredAdministrativeTileSet != AssetDatabase.LoadAssetAtPath<TileSet>(
                            DungeonPortalBakedBasisStartMapRuntimeController.RequiredAdministrativeTileSetAssetPath) ||
                        controllers[0].RequiredStartPrefab != AssetDatabase.LoadAssetAtPath<GameObject>(
                            DungeonPortalBakedBasisStartMapRuntimeController.RequiredStartPrefabAssetPath) ||
                        controllers[0].RequiredAdministrativePrefab != AssetDatabase.LoadAssetAtPath<GameObject>(
                            DungeonPortalBakedBasisStartMapRuntimeController.RequiredAdministrativePrefabAssetPath) ||
                        controllers[0].RequiredStartRoomBasis != AssetDatabase.LoadAssetAtPath<DungeonPortalBakedRoomBasisData>(
                            DungeonPortalBakedBasisValidationContract.StartV2BasisPath) ||
                        controllers[0].RequiredAdministrativeRoomBasis != AssetDatabase.LoadAssetAtPath<DungeonPortalBakedRoomBasisData>(
                            DungeonPortalBakedBasisValidationContract.AdministrativeV2BasisPath) ||
                        controllers[0].RequiredCompositionShader != AssetDatabase.LoadAssetAtPath<Shader>(
                            DungeonPortalBakedBasisValidationContract.CompositionShaderPath) ||
                        controllers[0].ActualPlayerCamera != null || controllers[0].IsReady ||
                        verified.Cameras[0].enabled || verified.Cameras[1].enabled || verify.isDirty)
                    {
                        throw new InvalidOperationException("Published StartMap harness runtime binding is invalid.");
                    }
                    AssertNoRejectedV21Overlay(verified.Root.gameObject);
                    AssertNoValidationOnlyGlobalVolume(verify);
                }
                finally
                {
                    if (verify.IsValid() && verify.isLoaded)
                        EditorSceneManager.CloseScene(verify, true);
                }

                RestoreEditorIdentity(originalActive, originalSelection);
                AssertInputsUnchanged(startMapHashBefore, pairHashBefore);
                string outputHash = ComputeFileSha256(OutputScenePath);
                WriteManifest(startMapHashBefore, pairHashBefore, outputHash);
                RestoreEditorIdentity(originalActive, originalSelection);
                return "PASS " + Status + "\n" +
                       "scene=" + OutputScenePath + "\n" +
                       "sceneSha256=" + outputHash + "\n" +
                       "actualStartMapLoadedAtRuntime=true\n" +
                       "actualStartMapActiveDuringCapture=true\n" +
                       "generatedPairBinding=actual-generated-V2-renderers+exact-canonical-keys\n" +
                       "generatedSurfaceSuppression=none;actual-generated-V2-renderers-remain-visible\n" +
                       "validationPairVisuals=forceRenderingOff-support-only\n" +
                       "productionStartMapWritten=false\n" +
                       "rejectedV21OverlayReused=false\n" +
                       "visualVerdict=UNREVIEWED";
            }
            finally
            {
                if (stagingOpen && staging.IsValid() && staging.isLoaded)
                    EditorSceneManager.CloseScene(staging, true);
                if (!published && AssetDatabase.LoadAssetAtPath<SceneAsset>(stagingPath) != null &&
                    !AssetDatabase.DeleteAsset(stagingPath))
                {
                    Debug.LogWarning("Could not delete failed StartMap harness staging asset: " + stagingPath);
                }
                RestoreEditorIdentity(originalActive, originalSelection);
                AssertInputsUnchanged(startMapHashBefore, pairHashBefore);
                if (published && !File.Exists(AssetPathToAbsolutePath(OutputScenePath)))
                    throw new IOException("Published StartMap harness unexpectedly disappeared.");
            }
        }

        private static void RequireStableEditModeAndCleanLoadedScenes()
        {
            if (Application.isPlaying || EditorApplication.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || Lightmapping.isRunning)
            {
                throw new InvalidOperationException("Stable Edit Mode without compile/update/lightmapping is required.");
            }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                    throw new InvalidOperationException("Loaded scene is dirty: " + scene.path);
            }
        }

        private static void RequireInputFiles()
        {
            if (!File.Exists(AssetPathToAbsolutePath(DungeonPortalBakedBasisStartMapRuntimeController.StartMapScenePath)) ||
                !File.Exists(AssetPathToAbsolutePath(DungeonPortalBakedBasisValidationContract.BuiltScenePath)) ||
                !File.Exists(AssetPathToAbsolutePath(
                    DungeonPortalBakedBasisStartMapRuntimeController.RequiredDungeonFlowAssetPath)) ||
                !File.Exists(AssetPathToAbsolutePath(
                    DungeonPortalBakedBasisStartMapRuntimeController.RequiredStartTileSetAssetPath)) ||
                !File.Exists(AssetPathToAbsolutePath(
                    DungeonPortalBakedBasisStartMapRuntimeController.RequiredAdministrativeTileSetAssetPath))
                || !File.Exists(AssetPathToAbsolutePath(
                    DungeonPortalBakedBasisStartMapRuntimeController.RequiredStartPrefabAssetPath))
                || !File.Exists(AssetPathToAbsolutePath(
                    DungeonPortalBakedBasisStartMapRuntimeController.RequiredAdministrativePrefabAssetPath))
                || !File.Exists(AssetPathToAbsolutePath(
                    DungeonPortalBakedBasisValidationContract.StartV2BasisPath))
                || !File.Exists(AssetPathToAbsolutePath(
                    DungeonPortalBakedBasisValidationContract.AdministrativeV2BasisPath))
                || !File.Exists(AssetPathToAbsolutePath(
                    DungeonPortalBakedBasisValidationContract.CompositionShaderPath))
                || !File.Exists(AssetPathToAbsolutePath(
                    DungeonPortalBakedBasisV2ReflectionProfileAuthoring.StartProfilePath))
                || !File.Exists(AssetPathToAbsolutePath(
                    DungeonPortalBakedBasisV2ReflectionProfileAuthoring.AdministrativeProfilePath(0)))
                || !File.Exists(AssetPathToAbsolutePath(
                    DungeonPortalBakedBasisV2ReflectionProfileAuthoring.AdministrativeProfilePath(1)))
                || !File.Exists(AssetPathToAbsolutePath(
                    DungeonPortalBakedBasisV2ReflectionProfileAuthoring.AdministrativeProfilePath(2)))
                || !File.Exists(AssetPathToAbsolutePath(
                    DungeonPortalBakedBasisV2ReflectionProfileAuthoring.AdministrativeProfilePath(3))))
            {
                throw new FileNotFoundException(
                    "Actual StartMap, built DPBB pair, required production inputs, V2 basis, shader, or V2 reflection profile is missing.");
            }
        }

        private static void ReconfigureV2ReflectionProfiles(
            DungeonPortalBakedBasisReflectionDriver reflection,
            DungeonPortalBakedBasisConnectionDriver driver)
        {
            if (!reflection.TryGetAuthoringConfiguration(
                    out string startStableId,
                    out ReflectionProbe startOwnedProbe,
                    out _,
                    out string[] administrativeStableIds,
                    out ReflectionProbe[] administrativeOwnedProbes,
                    out _,
                    out string configurationFailure))
            {
                throw new InvalidOperationException(
                    "Could not read validation-owned reflection geometry: " + configurationFailure);
            }

            DungeonPortalRoomReflectionProfile startProfile =
                AssetDatabase.LoadAssetAtPath<DungeonPortalRoomReflectionProfile>(
                    DungeonPortalBakedBasisV2ReflectionProfileAuthoring.StartProfilePath);
            var administrativeProfiles = new DungeonPortalRoomReflectionProfile[4];
            for (int i = 0; i < administrativeProfiles.Length; i++)
            {
                administrativeProfiles[i] =
                    AssetDatabase.LoadAssetAtPath<DungeonPortalRoomReflectionProfile>(
                        DungeonPortalBakedBasisV2ReflectionProfileAuthoring.AdministrativeProfilePath(i));
            }

            SerializedObject serializedReflection = new SerializedObject(reflection);
            SerializedProperty suppressionProperty =
                serializedReflection.FindProperty("productionProbesToSuppress");
            if (startProfile == null || suppressionProperty == null ||
                !suppressionProperty.isArray || suppressionProperty.arraySize != 10)
            {
                throw new InvalidOperationException(
                    "V2 reflection profiles or the ten-entry validation suppression template are missing.");
            }
            for (int i = 0; i < administrativeProfiles.Length; i++)
            {
                if (administrativeProfiles[i] == null)
                    throw new InvalidOperationException("V2 Administrative reflection profile " + i + " is missing.");
            }
            var validationSuppression = new ReflectionProbe[suppressionProperty.arraySize];
            for (int i = 0; i < validationSuppression.Length; i++)
            {
                validationSuppression[i] = suppressionProperty.GetArrayElementAtIndex(i)
                    .objectReferenceValue as ReflectionProbe;
                if (validationSuppression[i] == null)
                {
                    throw new InvalidOperationException(
                        "V2 reflection profile/suppression entry is missing at index " + i + ".");
                }
            }

            startOwnedProbe.customBakedTexture = startProfile.Power100Cubemap;
            startOwnedProbe.intensity = startProfile.FixedProbeIntensity;
            startOwnedProbe.enabled = false;
            EditorUtility.SetDirty(startOwnedProbe);
            for (int i = 0; i < administrativeOwnedProbes.Length; i++)
            {
                ReflectionProbe probe = administrativeOwnedProbes[i];
                DungeonPortalRoomReflectionProfile profile = administrativeProfiles[i];
                if (probe == null || profile == null)
                    throw new InvalidOperationException("V2 Administrative reflection binding is incomplete.");
                probe.customBakedTexture = profile.Power100Cubemap;
                probe.intensity = profile.FixedProbeIntensity;
                probe.enabled = false;
                EditorUtility.SetDirty(probe);
            }

            reflection.ConfigureAuthoring(
                driver,
                startStableId,
                startOwnedProbe,
                startProfile,
                administrativeStableIds,
                administrativeOwnedProbes,
                administrativeProfiles,
                validationSuppression);
            if (!reflection.TryValidateConfiguration(out string failure))
                throw new InvalidOperationException("V2 reflection reconfiguration failed: " + failure);
            EditorUtility.SetDirty(reflection);
        }

        private static void RequireOutputDoesNotExist()
        {
            if (File.Exists(AssetPathToAbsolutePath(OutputScenePath)) ||
                File.Exists(AssetPathToAbsolutePath(OutputManifestPath)))
            {
                throw new InvalidOperationException(
                    "StartMap harness output already exists; this builder never overwrites evidence.");
            }
        }

        private static void AssertNoRejectedV21Overlay(GameObject root)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                string typeName = components[i] != null ? components[i].GetType().FullName : string.Empty;
                if (!string.IsNullOrEmpty(typeName) &&
                    (typeName.StartsWith("DungeonAdjacentLightingPoC.", StringComparison.Ordinal) ||
                     typeName.IndexOf("DungeonAdjacentLightmapExtension", StringComparison.Ordinal) >= 0))
                {
                    throw new InvalidOperationException("Rejected V21 overlay found in DPBB input: " + typeName);
                }
            }
        }

        private static void RemoveValidationOnlyGlobalVolume(Scene scene)
        {
            GameObject target = null;
            int namedObjectCount = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
                {
                    Transform transform = transforms[transformIndex];
                    if (transform == null || !string.Equals(
                            transform.name,
                            DungeonPortalBakedBasisStartMapRuntimeController.ValidationOnlyGlobalVolumeName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    target = transform.gameObject;
                    namedObjectCount++;
                }
            }

            Volume[] volumes = target != null ? target.GetComponents<Volume>() : Array.Empty<Volume>();
            if (namedObjectCount != 1 || target == null || target.scene != scene ||
                volumes.Length != 1 || volumes[0] == null || !volumes[0].isGlobal ||
                !volumes[0].enabled || !target.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "Expected exactly one active validation-only global Volume named '" +
                    DungeonPortalBakedBasisStartMapRuntimeController.ValidationOnlyGlobalVolumeName +
                    "' in the copied DPBB scene; count=" + namedObjectCount + ".");
            }

            UnityEngine.Object.DestroyImmediate(target);
            AssertNoValidationOnlyGlobalVolume(scene);
        }

        private static void AssertNoValidationOnlyGlobalVolume(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
                {
                    Transform transform = transforms[transformIndex];
                    if (transform != null && string.Equals(
                            transform.name,
                            DungeonPortalBakedBasisStartMapRuntimeController.ValidationOnlyGlobalVolumeName,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Published StartMap harness retained the validation-only global Volume.");
                    }
                }
            }
        }

        private static GameObject FindUniqueRoot(Scene scene, string name)
        {
            GameObject result = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null || !string.Equals(roots[i].name, name, StringComparison.Ordinal))
                    continue;
                if (result != null) throw new InvalidOperationException("Duplicate root: " + name);
                result = roots[i];
            }
            return result;
        }

        private static int FindRootIndex(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i] != null && string.Equals(roots[i].name, name, StringComparison.Ordinal)) return i;
            throw new InvalidOperationException("Missing root: " + name);
        }

        private static void WriteManifest(string startMapHash, string pairHash, string outputHash)
        {
            string absolute = AssetPathToAbsolutePath(OutputManifestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute) ?? string.Empty);
            var builder = new StringBuilder();
            builder.AppendLine("schema=DPBBStartMapHarness/v3");
            builder.AppendLine("status=" + Status);
            builder.AppendLine("harnessScene=" + OutputScenePath);
            builder.AppendLine("harnessSceneSha256=" + outputHash);
            builder.AppendLine("actualStartMap=" + DungeonPortalBakedBasisStartMapRuntimeController.StartMapScenePath);
            builder.AppendLine("actualStartMapSha256=" + startMapHash);
            builder.AppendLine("dpbbPairInput=" + DungeonPortalBakedBasisValidationContract.BuiltScenePath);
            builder.AppendLine("dpbbPairInputSha256=" + pairHash);
            builder.AppendLine("runtimeStartMapLoad=additive");
            builder.AppendLine("runtimeActiveScene=StartMap");
            builder.AppendLine("runtimeEntry=DebugRemoteControl.GenerateDungeon+exact-generated-tile-binding+actual-player-fixed-frame+DungeonZoneManager.EnterDungeon");
            builder.AppendLine("debugPointRegistry=diagnostic-only;not-an-entry-gate");
            builder.AppendLine("camera=unique-enabled-child-of-actual-local-owner-FPSController;tag-unmodified;no-fallback");
            builder.AppendLine("requiredDungeonVolume=" +
                               DungeonPortalBakedBasisStartMapRuntimeController.RequiredDungeonVolumeProfileName);
            builder.AppendLine("playerDungeonOnlyLight=HUMAN_SPOT_ON-default;ORACLE_SPOT_OFF-explicit-only");
            builder.AppendLine("validationOnlyGlobalVolume=removed");
            builder.AppendLine("indoorRenderSettingsGate=production-DungeonZoneManager-exact");
            builder.AppendLine("requiredDungeonFlow=" +
                               DungeonPortalBakedBasisStartMapRuntimeController.RequiredDungeonFlowAssetPath);
            builder.AppendLine("requiredStartTileSet=" +
                               DungeonPortalBakedBasisStartMapRuntimeController.RequiredStartTileSetAssetPath);
            builder.AppendLine("requiredAdministrativeTileSet=" +
                               DungeonPortalBakedBasisStartMapRuntimeController.RequiredAdministrativeTileSetAssetPath);
            builder.AppendLine("requiredStartPrefab=" +
                               DungeonPortalBakedBasisStartMapRuntimeController.RequiredStartPrefabAssetPath);
            builder.AppendLine("requiredAdministrativePrefab=" +
                               DungeonPortalBakedBasisStartMapRuntimeController.RequiredAdministrativePrefabAssetPath);
            builder.AppendLine("requiredStartRoomBasis=" +
                               DungeonPortalBakedBasisValidationContract.StartV2BasisPath);
            builder.AppendLine("requiredAdministrativeRoomBasis=" +
                               DungeonPortalBakedBasisValidationContract.AdministrativeV2BasisPath);
            builder.AppendLine("requiredCompositionShader=" +
                               DungeonPortalBakedBasisValidationContract.CompositionShaderPath);
            builder.AppendLine("reflectionProfiles=validation-owned-ReflectionV2;P0-residual-to-P100");
            builder.AppendLine("generationSelection=bounded-regeneration;maxAttempts=16;production-assets-unchanged");
            builder.AppendLine("generatedPairBinding=actual-generated-V2-renderers;exact-canonical-keys;extras-untouched");
            builder.AppendLine("generatedSurfaceSuppression=none;actual-generated-V2-renderers-remain-visible");
            builder.AppendLine("validationPairVisuals=forceRenderingOff-support-only;snapshot-restored");
            builder.AppendLine("generatedPairDuplicateProbes=actual-production-variants-suppressed-by-DPBB-reflection-driver;snapshot-restored");
            builder.AppendLine("nonTargetGeneratedTiles=remain-rendered;included-in-spatial-exclusion-gate");
            builder.AppendLine("spatialGate=both-fixed-cameras-and-player-inside-corresponding-generated-tile;non-target-penetration-forbidden");
            builder.AppendLine("v21Overlay=forbidden");
            builder.AppendLine("visualVerdict=UNREVIEWED");
            File.WriteAllText(absolute, builder.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(OutputManifestPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        private static void RestoreEditorIdentity(Scene active, UnityEngine.Object selected)
        {
            if (active.IsValid() && active.isLoaded && SceneManager.GetActiveScene() != active)
                EditorSceneManager.SetActiveScene(active);
            Selection.activeObject = selected;
        }

        private static void AssertInputsUnchanged(string startMapHash, string pairHash)
        {
            if (!string.Equals(startMapHash, ComputeFileSha256(
                    DungeonPortalBakedBasisStartMapRuntimeController.StartMapScenePath),
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(pairHash, ComputeFileSha256(
                    DungeonPortalBakedBasisValidationContract.BuiltScenePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Build changed the production StartMap or DPBB pair input scene.");
            }
        }

        private static string ComputeFileSha256(string assetPath)
        {
            using (FileStream stream = File.OpenRead(AssetPathToAbsolutePath(assetPath)))
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) builder.Append(hash[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }
    }
}
