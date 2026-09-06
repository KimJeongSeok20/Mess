using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DungeonPortalBakedBasisPoC;
using DungeonPortalBakedBasisPoC.Validation;
using DungeonPortalTransportPoC;
using DungeonPortalTransportPoC.KExactBasisV1.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonPortalBakedBasisPoC.Validation.Editor
{
    /// <summary>
    /// Staged-copy builder for the true baked-basis validation scene. It never saves the pinned
    /// source scene and refuses to overwrite its own published scene or manifest.
    /// </summary>
    public static class DungeonPortalBakedBasisSceneBuilder
    {
        private const string Status = "DPBB_SCENE_BUILD_V1";

        [MenuItem("Tools/Dungeon/Lighting/Portal Baked Basis PoC/Build Isolated Validation Scene (Edit Mode)")]
        public static void BuildFromMenu()
        {
            string result = Build();
            if (result.StartsWith("PASS", StringComparison.Ordinal))
                Debug.Log(result);
            else
                Debug.LogError(result);
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
            Scene sourceScene = DungeonPortalBakedBasisValidationContract.RequireCleanSourceSceneActive();
            KExactBasisV1EditorContract.SelectionSnapshot selection =
                KExactBasisV1EditorContract.SelectionSnapshot.Capture();
            KExactBasisV1EditorContract.ProtectedAssetSnapshot protectedAssets =
                KExactBasisV1EditorContract.ProtectedAssetSnapshot.Capture();
            KExactBasisV1EditorContract.SceneBindings sourceBindings =
                KExactBasisV1EditorContract.ResolveSceneBindings(sourceScene, true, false);
            Scene originalActive = SceneManager.GetActiveScene();
            bool originalPlaying = EditorApplication.isPlaying;

            DungeonPortalBakedBasisValidationContract.EnsureOwnedFolders();
            RequireOutputDoesNotExist();
            DungeonPortalBakedRoomBasisData startBasis = RequireBasis(
                DungeonPortalBakedBasisValidationContract.StartBasisPath,
                DungeonPortalBakedBasisValidationContract.StartRoomId);
            DungeonPortalBakedRoomBasisData administrativeBasis = RequireBasis(
                DungeonPortalBakedBasisValidationContract.AdministrativeBasisPath,
                DungeonPortalBakedBasisValidationContract.AdministrativeRoomId);
            Shader compositionShader = AssetDatabase.LoadAssetAtPath<Shader>(
                DungeonPortalBakedBasisValidationContract.CompositionShaderPath);
            if (compositionShader == null || !string.Equals(
                    compositionShader.name,
                    "Hidden/DungeonPortalBakedBasisPoC/Compose",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The exact DPBB composition shader is missing.");
            }
            DungeonPortalRoomReflectionProfile[] reflectionProfiles =
                RequireReflectionProfiles();

            string token = Guid.NewGuid().ToString("N").Substring(0, 12);
            string stagingPath = DungeonPortalBakedBasisValidationContract.SceneFolder +
                                 "/_S_" + token + ".unity";
            Scene stagingScene = default;
            bool stagingOpen = false;
            bool published = false;
            KExactBasisV1EditorContract.RendererParityReport parity = default;
            RuntimeReport runtimeReport = default;

            try
            {
                if (!AssetDatabase.CopyAsset(
                        DungeonPortalBakedBasisValidationContract.SourceScenePath, stagingPath))
                {
                    throw new IOException("AssetDatabase.CopyAsset rejected the DPBB staging scene.");
                }
                AssetDatabase.ImportAsset(stagingPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                stagingScene = EditorSceneManager.OpenScene(stagingPath, OpenSceneMode.Additive);
                stagingOpen = stagingScene.IsValid() && stagingScene.isLoaded;
                if (!stagingOpen || !EditorSceneManager.SetActiveScene(stagingScene))
                    throw new InvalidOperationException("Could not activate the DPBB staging scene.");

                KExactBasisV1EditorContract.SceneBindings before =
                    KExactBasisV1EditorContract.ResolveSceneBindings(stagingScene, true, false);
                KExactBasisV1EditorContract.AssertRendererParity(sourceBindings, before);
                AssertLightmapReferenceParity(sourceBindings, before);

                before.OldPocRoot.gameObject.SetActive(false);
                GameObject runtimeObject = new GameObject(
                    DungeonPortalBakedBasisValidationContract.RuntimeRootName);
                SceneManager.MoveGameObjectToScene(runtimeObject, stagingScene);
                runtimeObject.transform.SetParent(before.Root.transform, false);

                runtimeReport = ConfigureRuntime(
                    runtimeObject.transform,
                    before,
                    startBasis,
                    administrativeBasis,
                    compositionShader,
                    reflectionProfiles);

                KExactBasisV1EditorContract.SceneBindings after =
                    KExactBasisV1EditorContract.ResolveSceneBindings(stagingScene, false, false);
                ValidateRuntimeScene(after, startBasis, administrativeBasis, reflectionProfiles);
                parity = KExactBasisV1EditorContract.AssertRendererParity(sourceBindings, after);
                AssertLightmapReferenceParity(sourceBindings, after);
                if (runtimeObject.GetComponentsInChildren<Renderer>(true).Length != 0)
                    throw new InvalidOperationException("DPBB runtime root added a Renderer.");

                EditorSceneManager.MarkSceneDirty(stagingScene);
                if (!EditorSceneManager.SaveScene(stagingScene, stagingPath, false))
                    throw new IOException("Could not save the owned DPBB staging scene.");
                if (sourceScene.isDirty)
                    throw new InvalidOperationException("DPBB staging authoring dirtied the source scene.");

                if (!EditorSceneManager.CloseScene(stagingScene, true))
                    throw new IOException("Could not close the DPBB staging scene.");
                stagingOpen = false;
                RestoreEditorIdentity(originalActive, selection, originalPlaying);
                protectedAssets.AssertUnchanged();

                string moveError = AssetDatabase.MoveAsset(
                    stagingPath, DungeonPortalBakedBasisValidationContract.BuiltScenePath);
                if (!string.IsNullOrEmpty(moveError))
                    throw new IOException("Could not publish DPBB scene: " + moveError);
                published = true;
                AssetDatabase.ImportAsset(DungeonPortalBakedBasisValidationContract.BuiltScenePath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                Scene verification = EditorSceneManager.OpenScene(
                    DungeonPortalBakedBasisValidationContract.BuiltScenePath, OpenSceneMode.Additive);
                try
                {
                    KExactBasisV1EditorContract.SceneBindings verified =
                        KExactBasisV1EditorContract.ResolveSceneBindings(verification, false, false);
                    ValidateRuntimeScene(
                        verified, startBasis, administrativeBasis, reflectionProfiles);
                    parity = KExactBasisV1EditorContract.AssertRendererParity(sourceBindings, verified);
                    AssertLightmapReferenceParity(sourceBindings, verified);
                    if (verification.isDirty || sourceScene.isDirty)
                        throw new InvalidOperationException("Read-only DPBB verification dirtied a scene.");
                }
                finally
                {
                    if (verification.IsValid() && verification.isLoaded)
                        EditorSceneManager.CloseScene(verification, true);
                }

                RestoreEditorIdentity(originalActive, selection, originalPlaying);
                protectedAssets.AssertUnchanged();
                string sceneSha = DungeonPortalBakedBasisValidationContract.ComputeFileSha256(
                    DungeonPortalBakedBasisValidationContract.BuiltScenePath);
                WriteManifest(sceneSha, protectedAssets.FingerprintSha256, parity, runtimeReport,
                    startBasis, administrativeBasis);
                protectedAssets.AssertUnchanged();
                RestoreEditorIdentity(originalActive, selection, originalPlaying);

                return "PASS " + Status + "\n" +
                       "scene=" + DungeonPortalBakedBasisValidationContract.BuiltScenePath + "\n" +
                       "sceneSha256=" + sceneSha + "\n" +
                       "rendererCount=" + parity.RendererCount + "\n" +
                       "additionalRendererCount=" + parity.AdditionalRendererCount + "\n" +
                       "visualVerdict=UNREVIEWED\n" +
                       "visualParityClaimed=false\n" +
                       "productionAssetsWritten=false";
            }
            catch
            {
                // Keep a failed staging copy for diagnosis; do not overwrite or delete evidence.
                if (published)
                    Debug.LogError("[DPBB] A scene was published before a later build step failed; inspect it manually.");
                throw;
            }
            finally
            {
                if (stagingOpen && stagingScene.IsValid() && stagingScene.isLoaded)
                    EditorSceneManager.CloseScene(stagingScene, true);
                RestoreEditorIdentity(originalActive, selection, originalPlaying);
                protectedAssets.AssertUnchanged();
            }
        }

        private static RuntimeReport ConfigureRuntime(
            Transform runtimeRoot,
            KExactBasisV1EditorContract.SceneBindings bindings,
            DungeonPortalBakedRoomBasisData startBasis,
            DungeonPortalBakedRoomBasisData administrativeBasis,
            Shader compositionShader,
            DungeonPortalRoomReflectionProfile[] reflectionProfiles)
        {
            if (runtimeRoot == null || runtimeRoot.GetComponentsInChildren<Renderer>(true).Length != 0)
                throw new InvalidOperationException("DPBB runtime root creation contract failed.");

            DungeonPortalDoorAngleSource liveDoorSource = CreateLiveDoorAngleSource(
                runtimeRoot,
                bindings.DoorAngleSource,
                bindings.DoorLeaf);
            DungeonPortalBakedBasisRoomCompositor startCompositor =
                bindings.StartRoom.gameObject.AddComponent<DungeonPortalBakedBasisRoomCompositor>();
            DungeonPortalBakedBasisRoomCompositor administrativeCompositor =
                bindings.AdministrativeRoom.gameObject.AddComponent<DungeonPortalBakedBasisRoomCompositor>();

            float startPower = ToPower01(bindings.StartSwitcher.CurrentPowerLevel);
            float administrativePower = ToPower01(bindings.AdministrativeSwitcher.CurrentPowerLevel);
            startCompositor.ConfigureAuthoring(
                startBasis,
                bindings.StartRoom,
                compositionShader,
                CreateBindings(bindings.StartRoom, startBasis),
                new[]
                {
                    CreateIncoming(
                        DungeonPortalBakedBasisValidationContract.AdministrativeToStartConnectionId,
                        DungeonPortalBakedBasisValidationContract.DoorwayId,
                        administrativeBasis,
                        DungeonPortalBakedBasisValidationContract.DoorwayId,
                        administrativePower)
                },
                startPower,
                true,
                0.5f);
            administrativeCompositor.ConfigureAuthoring(
                administrativeBasis,
                bindings.AdministrativeRoom,
                compositionShader,
                CreateBindings(bindings.AdministrativeRoom, administrativeBasis),
                new[]
                {
                    CreateIncoming(
                        DungeonPortalBakedBasisValidationContract.StartToAdministrativeConnectionId,
                        DungeonPortalBakedBasisValidationContract.DoorwayId,
                        startBasis,
                        DungeonPortalBakedBasisValidationContract.DoorwayId,
                        startPower)
                },
                administrativePower,
                true,
                0.5f);

            GameObject driverObject = new GameObject(
                DungeonPortalBakedBasisValidationContract.DriverName);
            driverObject.transform.SetParent(runtimeRoot, false);
            DungeonPortalBakedBasisConnectionDriver driver =
                driverObject.AddComponent<DungeonPortalBakedBasisConnectionDriver>();
            driver.ConfigureAuthoring(
                "Start_Admin_BakedBasisV1",
                startCompositor,
                administrativeCompositor,
                startBasis,
                administrativeBasis,
                DungeonPortalBakedBasisValidationContract.DoorwayId,
                DungeonPortalBakedBasisValidationContract.DoorwayId,
                liveDoorSource,
                bindings.StartSwitcher,
                bindings.AdministrativeSwitcher,
                startPower,
                administrativePower,
                0.35f,
                0.25f);

            DungeonDoorDualSideProbeReceiver[] productionDoorReceivers =
                bindings.DoorLeaf.GetComponentsInChildren<DungeonDoorDualSideProbeReceiver>(true);
            if (productionDoorReceivers.Length != 1 || productionDoorReceivers[0] == null)
            {
                throw new InvalidOperationException(
                    "Expected exactly one production DungeonDoorDualSideProbeReceiver on the validation door.");
            }
            productionDoorReceivers[0].enabled = false;

            GameObject doorShObject = new GameObject(
                DungeonPortalBakedBasisValidationContract.DoorShDriverName);
            doorShObject.transform.SetParent(runtimeRoot, false);
            DungeonPortalBakedBasisDoorShDriver doorSh =
                doorShObject.AddComponent<DungeonPortalBakedBasisDoorShDriver>();
            doorSh.ConfigureAuthoring(
                driver,
                bindings.DoorLeaf,
                bindings.StartRoom,
                bindings.AdministrativeRoom,
                startBasis,
                administrativeBasis,
                DungeonPortalBakedBasisValidationContract.DoorwayId,
                DungeonPortalBakedBasisValidationContract.DoorwayId);

            DungeonPortalBakedBasisReflectionDriver reflectionDriver =
                CreateReflectionRuntime(
                    runtimeRoot,
                    KExactBasisV1EditorContract.FindUniqueRoot(
                        bindings.Scene,
                        DungeonPortalBakedBasisValidationContract
                            .LegacyReflectionOwnerRootName).transform,
                    driver,
                    reflectionProfiles);

            bool driverValid = driver.TryValidateConfiguration(out string driverFailure);
            bool doorShValid = doorSh.TryValidateConfiguration(out string doorShFailure);
            bool reflectionValid = reflectionDriver.TryValidateConfiguration(
                out string reflectionFailure);
            if (!driverValid || !doorShValid || !reflectionValid)
            {
                throw new InvalidOperationException("DPBB runtime configuration failed. Driver=" +
                                                    driverFailure + " DoorSH=" + doorShFailure +
                                                    " Reflection=" + reflectionFailure);
            }

            EditorUtility.SetDirty(liveDoorSource);
            EditorUtility.SetDirty(startCompositor);
            EditorUtility.SetDirty(administrativeCompositor);
            EditorUtility.SetDirty(driver);
            EditorUtility.SetDirty(doorSh);
            EditorUtility.SetDirty(reflectionDriver);
            EditorUtility.SetDirty(productionDoorReceivers[0]);
            return new RuntimeReport(
                startBasis.CanonicalRenderers.Length,
                administrativeBasis.CanonicalRenderers.Length,
                productionDoorReceivers.Length,
                reflectionDriver.OwnedProbeCount,
                reflectionDriver.SuppressedProductionProbeCount);
        }

        private static DungeonPortalBakedBasisRoomCompositor.IncomingDoorState CreateIncoming(
            string connectionId,
            string receiverDoorId,
            DungeonPortalBakedRoomBasisData sourceBasis,
            string sourceDoorId,
            float sourcePower)
        {
            var result = new DungeonPortalBakedBasisRoomCompositor.IncomingDoorState();
            result.ConfigureAuthoring(
                connectionId,
                receiverDoorId,
                sourceBasis,
                sourceDoorId,
                sourcePower,
                0f,
                1f,
                true);
            return result;
        }

        private static DungeonPortalBakedBasisRoomCompositor.ExplicitRendererBinding[] CreateBindings(
            Transform roomRoot,
            DungeonPortalBakedRoomBasisData basis)
        {
            Dictionary<string, Renderer> rendererByKey =
                DungeonPortalBakedBasisValidationContract.BuildRendererKeyMap(roomRoot);
            DungeonPortalBakedRoomBasisData.CanonicalRendererEntry[] entries =
                basis.CanonicalRenderers;
            var result = new DungeonPortalBakedBasisRoomCompositor.ExplicitRendererBinding[entries.Length];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                DungeonPortalBakedRoomBasisData.CanonicalRendererEntry entry = entries[i];
                if (entry == null || !seen.Add(entry.CanonicalRendererKey) ||
                    !rendererByKey.TryGetValue(entry.CanonicalRendererKey, out Renderer renderer))
                {
                    throw new InvalidOperationException(
                        "Canonical renderer binding cannot resolve '" +
                        (entry != null ? entry.CanonicalRendererKey : "<null>") + "'.");
                }
                var binding = new DungeonPortalBakedBasisRoomCompositor.ExplicitRendererBinding();
                binding.ConfigureAuthoring(entry.CanonicalRendererKey, renderer);
                result[i] = binding;
            }
            return result;
        }

        private static DungeonPortalDoorAngleSource CreateLiveDoorAngleSource(
            Transform runtimeRoot,
            DungeonPortalDoorAngleSource canonicalSource,
            Transform doorLeaf)
        {
            if (runtimeRoot == null || canonicalSource == null || doorLeaf == null ||
                !canonicalSource.IsConfigured || canonicalSource.DoorLeaf != doorLeaf)
            {
                throw new InvalidOperationException("Canonical door-angle source is unavailable.");
            }
            SerializedObject serialized = new SerializedObject(canonicalSource);
            serialized.UpdateIfRequiredOrScript();
            SerializedProperty closed = serialized.FindProperty("closedLocalRotation");
            SerializedProperty axis = serialized.FindProperty("localHingeAxis");
            SerializedProperty angle = serialized.FindProperty("openAngleDegrees");
            if (closed == null || axis == null || angle == null ||
                axis.vector3Value.sqrMagnitude <= Mathf.Epsilon ||
                !float.IsFinite(angle.floatValue) || Mathf.Abs(angle.floatValue) <= 0.001f)
            {
                throw new InvalidOperationException("Canonical door-angle settings cannot be cloned safely.");
            }

            GameObject liveObject = new GameObject(
                DungeonPortalBakedBasisValidationContract.LiveDoorSourceName);
            liveObject.transform.SetParent(runtimeRoot, false);
            DungeonPortalDoorAngleSource live = liveObject.AddComponent<DungeonPortalDoorAngleSource>();
            live.Configure(doorLeaf, closed.quaternionValue, axis.vector3Value.normalized,
                angle.floatValue);
            if (!live.IsConfigured || live.DoorLeaf != doorLeaf)
                throw new InvalidOperationException("Live DPBB door-angle source configuration failed.");
            return live;
        }

        private static DungeonPortalRoomReflectionProfile[] RequireReflectionProfiles()
        {
            DungeonPortalBakedBasisValidationContract.ReflectionSpec[] specs =
                DungeonPortalBakedBasisValidationContract.ReflectionSpecs;
            if (specs == null || specs.Length != 5)
                throw new InvalidOperationException("The DPBB five-profile reflection contract changed.");

            var result = new DungeonPortalRoomReflectionProfile[specs.Length];
            int startCount = 0;
            int administrativeCount = 0;
            for (int i = 0; i < specs.Length; i++)
            {
                DungeonPortalBakedBasisValidationContract.ReflectionSpec spec = specs[i];
                DungeonPortalRoomReflectionProfile profile =
                    AssetDatabase.LoadAssetAtPath<DungeonPortalRoomReflectionProfile>(
                        spec.ProfilePath);
                string profileFailure = profile == null ? "asset is missing" : null;
                if (profile == null || !profile.TryValidate(out profileFailure))
                {
                    throw new InvalidOperationException(
                        "Required reflection profile is missing or invalid: " +
                        spec.ProfilePath + " " + profileFailure);
                }
                if (!string.Equals(
                        AssetDatabase.GetAssetPath(profile),
                        spec.ProfilePath,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Reflection profile path drifted: " + spec.ProfilePath);
                }
                for (int previous = 0; previous < i; previous++)
                {
                    if (result[previous] == profile)
                        throw new InvalidOperationException("A reflection profile is duplicated.");
                }
                result[i] = profile;
                if (spec.UsesStartPower)
                    startCount++;
                else
                    administrativeCount++;
            }
            if (startCount != 1 || administrativeCount != 4)
                throw new InvalidOperationException("Reflection room-power routing must be Start=1/Admin=4.");
            return result;
        }

        private static DungeonPortalBakedBasisReflectionDriver CreateReflectionRuntime(
            Transform runtimeRoot,
            Transform legacyReflectionOwnerRoot,
            DungeonPortalBakedBasisConnectionDriver connectionDriver,
            DungeonPortalRoomReflectionProfile[] profiles)
        {
            if (runtimeRoot == null || legacyReflectionOwnerRoot == null ||
                connectionDriver == null ||
                profiles == null || profiles.Length != 5)
            {
                throw new InvalidOperationException(
                    "The isolated DPBB reflection authoring inputs are invalid.");
            }

            DungeonPortalRoomReflectionBlend[] legacyBlends =
                legacyReflectionOwnerRoot.GetComponentsInChildren<DungeonPortalRoomReflectionBlend>(true);
            if (legacyBlends.Length != 5)
            {
                throw new InvalidOperationException(
                    "Expected exactly five inactive legacy PoC reflection templates; found " +
                    legacyBlends.Length + ".");
            }

            GameObject reflectionObject = new GameObject(
                DungeonPortalBakedBasisValidationContract.ReflectionDriverName);
            reflectionObject.transform.SetParent(runtimeRoot, false);
            DungeonPortalBakedBasisReflectionDriver reflectionDriver =
                reflectionObject.AddComponent<DungeonPortalBakedBasisReflectionDriver>();

            var administrativeIds = new string[4];
            var administrativeOwnedProbes = new ReflectionProbe[4];
            var administrativeProfiles = new DungeonPortalRoomReflectionProfile[4];
            ReflectionProbe startOwnedProbe = null;
            DungeonPortalRoomReflectionProfile startProfile = null;
            string startId = null;
            int administrativeIndex = 0;
            var suppression = new List<ReflectionProbe>(10);
            var suppressionSet = new HashSet<ReflectionProbe>();

            DungeonPortalBakedBasisValidationContract.ReflectionSpec[] specs =
                DungeonPortalBakedBasisValidationContract.ReflectionSpecs;
            for (int i = 0; i < specs.Length; i++)
            {
                DungeonPortalBakedBasisValidationContract.ReflectionSpec spec = specs[i];
                DungeonPortalRoomReflectionBlend legacyBlend = RequireLegacyReflectionBlend(
                    legacyBlends, spec.LegacyProbeObjectName);
                SerializedObject serializedBlend = new SerializedObject(legacyBlend);
                SerializedProperty profileProperty = serializedBlend.FindProperty("profile");
                SerializedProperty suppressionProperty = serializedBlend.FindProperty(
                    "productionProbesToSuppress");
                if (profileProperty == null || profileProperty.objectReferenceValue != profiles[i] ||
                    suppressionProperty == null || !suppressionProperty.isArray ||
                    suppressionProperty.arraySize != 2)
                {
                    throw new InvalidOperationException(
                        "Legacy reflection template contract drifted at '" +
                        spec.LegacyProbeObjectName + "'.");
                }

                ReflectionProbe[] sourceProbes = legacyBlend.GetComponents<ReflectionProbe>();
                if (sourceProbes.Length != 1 || sourceProbes[0] == null || legacyBlend.enabled)
                {
                    throw new InvalidOperationException(
                        "Legacy reflection template must contain one disabled blend and one probe: " +
                        spec.LegacyProbeObjectName);
                }
                ReflectionProbe sourceProbe = sourceProbes[0];
                if (sourceProbe.enabled || sourceProbe.mode !=
                    UnityEngine.Rendering.ReflectionProbeMode.Custom ||
                    sourceProbe.customBakedTexture != profiles[i].Power100Cubemap ||
                    Mathf.Abs(sourceProbe.intensity - profiles[i].FixedProbeIntensity) > 0.0001f)
                {
                    throw new InvalidOperationException(
                        "Inactive legacy probe baseline no longer matches its P100 profile: " +
                        spec.LegacyProbeObjectName);
                }

                GameObject ownedObject = new GameObject(spec.LegacyProbeObjectName);
                ownedObject.transform.SetParent(reflectionObject.transform, false);
                ownedObject.transform.SetPositionAndRotation(
                    sourceProbe.transform.position,
                    sourceProbe.transform.rotation);
                ownedObject.transform.localScale = sourceProbe.transform.lossyScale;
                ownedObject.layer = sourceProbe.gameObject.layer;
                ownedObject.tag = sourceProbe.gameObject.tag;
                GameObjectUtility.SetStaticEditorFlags(
                    ownedObject,
                    GameObjectUtility.GetStaticEditorFlags(sourceProbe.gameObject));

                ReflectionProbe ownedProbe = ownedObject.AddComponent<ReflectionProbe>();
                EditorUtility.CopySerialized(sourceProbe, ownedProbe);
                ownedProbe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
                ownedProbe.customBakedTexture = profiles[i].Power100Cubemap;
                ownedProbe.intensity = profiles[i].FixedProbeIntensity;
                ownedProbe.enabled = false;
                ownedObject.SetActive(true);
                AssertReflectionTemplateParity(sourceProbe, ownedProbe);

                if (spec.UsesStartPower)
                {
                    if (startOwnedProbe != null)
                        throw new InvalidOperationException("More than one Start reflection was authored.");
                    startId = spec.StableId;
                    startOwnedProbe = ownedProbe;
                    startProfile = profiles[i];
                }
                else
                {
                    if (administrativeIndex >= administrativeOwnedProbes.Length)
                        throw new InvalidOperationException("Too many Administrative reflections were authored.");
                    administrativeIds[administrativeIndex] = spec.StableId;
                    administrativeOwnedProbes[administrativeIndex] = ownedProbe;
                    administrativeProfiles[administrativeIndex] = profiles[i];
                    administrativeIndex++;
                }

                for (int suppressionIndex = 0;
                     suppressionIndex < suppressionProperty.arraySize;
                     suppressionIndex++)
                {
                    ReflectionProbe productionProbe = suppressionProperty
                        .GetArrayElementAtIndex(suppressionIndex).objectReferenceValue as ReflectionProbe;
                    if (productionProbe == null || !suppressionSet.Add(productionProbe))
                    {
                        throw new InvalidOperationException(
                            "Legacy production-probe suppression mapping is missing or duplicated at '" +
                            spec.LegacyProbeObjectName + "'.");
                    }
                    suppression.Add(productionProbe);
                }

                EditorUtility.SetDirty(ownedObject.transform);
                EditorUtility.SetDirty(ownedProbe);
            }

            if (startOwnedProbe == null || administrativeIndex != 4 || suppression.Count != 10)
                throw new InvalidOperationException("DPBB reflection routing did not resolve 1/4/10 probes.");

            reflectionDriver.ConfigureAuthoring(
                connectionDriver,
                startId,
                startOwnedProbe,
                startProfile,
                administrativeIds,
                administrativeOwnedProbes,
                administrativeProfiles,
                suppression.ToArray());
            if (reflectionObject.GetComponentsInChildren<Renderer>(true).Length != 0)
                throw new InvalidOperationException("DPBB reflection authoring added a Renderer.");
            EditorUtility.SetDirty(reflectionObject.transform);
            EditorUtility.SetDirty(reflectionDriver);
            return reflectionDriver;
        }

        private static DungeonPortalRoomReflectionBlend RequireLegacyReflectionBlend(
            DungeonPortalRoomReflectionBlend[] candidates,
            string objectName)
        {
            DungeonPortalRoomReflectionBlend result = null;
            int count = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] == null || !string.Equals(
                        candidates[i].name, objectName, StringComparison.Ordinal))
                {
                    continue;
                }
                result = candidates[i];
                count++;
            }
            if (count != 1)
                throw new InvalidOperationException(
                    "Expected exactly one legacy reflection template '" + objectName +
                    "'; found " + count + ".");
            return result;
        }

        private static void AssertReflectionTemplateParity(
            ReflectionProbe source,
            ReflectionProbe candidate)
        {
            if (source == null || candidate == null || candidate.enabled ||
                candidate.mode != source.mode ||
                candidate.customBakedTexture != source.customBakedTexture ||
                Mathf.Abs(candidate.intensity - source.intensity) > 0.0001f ||
                candidate.resolution != source.resolution ||
                candidate.size != source.size || candidate.center != source.center ||
                Mathf.Abs(candidate.nearClipPlane - source.nearClipPlane) > 0.0001f ||
                Mathf.Abs(candidate.farClipPlane - source.farClipPlane) > 0.0001f ||
                candidate.cullingMask != source.cullingMask ||
                Mathf.Abs(candidate.blendDistance - source.blendDistance) > 0.0001f ||
                candidate.boxProjection != source.boxProjection ||
                candidate.importance != source.importance ||
                Vector3.Distance(candidate.transform.position, source.transform.position) > 0.0001f ||
                Quaternion.Angle(candidate.transform.rotation, source.transform.rotation) > 0.001f ||
                Vector3.Distance(candidate.transform.lossyScale, source.transform.lossyScale) > 0.0001f ||
                candidate.gameObject.layer != source.gameObject.layer ||
                !string.Equals(candidate.gameObject.tag, source.gameObject.tag,
                    StringComparison.Ordinal) ||
                GameObjectUtility.GetStaticEditorFlags(candidate.gameObject) !=
                GameObjectUtility.GetStaticEditorFlags(source.gameObject))
            {
                throw new InvalidOperationException(
                    "DPBB-owned reflection probe does not match inactive legacy template '" +
                    source.name + "'.");
            }
        }

        private static DungeonPortalBakedRoomBasisData RequireBasis(string path, string expectedRoomId)
        {
            DungeonPortalBakedRoomBasisData basis =
                AssetDatabase.LoadAssetAtPath<DungeonPortalBakedRoomBasisData>(path);
            if (basis == null)
            {
                throw new InvalidOperationException(
                    "Required room basis is missing or invalid: " + path + " asset is missing");
            }
            if (!string.Equals(basis.RoomId, expectedRoomId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Required room basis is missing or invalid: " +
                                                    path + " room id mismatch");
            }
            if (!basis.TryValidateDefinition(out string validationFailure))
            {
                throw new InvalidOperationException("Required room basis is missing or invalid: " +
                                                    path + " " + validationFailure);
            }
            return basis;
        }

        private static void ValidateRuntimeScene(
            KExactBasisV1EditorContract.SceneBindings scene,
            DungeonPortalBakedRoomBasisData startBasis,
            DungeonPortalBakedRoomBasisData administrativeBasis,
            DungeonPortalRoomReflectionProfile[] reflectionProfiles)
        {
            Transform runtimeRoot = DungeonPortalBakedBasisValidationContract.FindUniqueDirectChild(
                scene.Root.transform, DungeonPortalBakedBasisValidationContract.RuntimeRootName);
            if (runtimeRoot.GetComponentsInChildren<Renderer>(true).Length != 0 ||
                scene.OldPocRoot.gameObject.activeSelf)
            {
                throw new InvalidOperationException("DPBB clone runtime-root/old-PoC contract changed.");
            }
            DungeonPortalBakedBasisConnectionDriver driver =
                DungeonPortalBakedBasisValidationContract.RequireDriver(scene.Scene);
            DungeonPortalBakedBasisDoorShDriver doorSh =
                DungeonPortalBakedBasisValidationContract.RequireDoorShDriver(scene.Scene);
            DungeonPortalBakedBasisReflectionDriver reflectionDriver =
                DungeonPortalBakedBasisValidationContract.RequireReflectionDriver(scene.Scene);
            bool driverValid = driver.TryValidateConfiguration(out string driverFailure);
            bool doorShValid = doorSh.TryValidateConfiguration(out string doorShFailure);
            bool reflectionValid = reflectionDriver.TryValidateConfiguration(
                out string reflectionFailure);
            if (!driverValid || !doorShValid || !reflectionValid ||
                driver.StartRoomCompositor.RoomBasis != startBasis ||
                driver.AdministrativeRoomCompositor.RoomBasis != administrativeBasis ||
                reflectionDriver.ConnectionDriver != driver ||
                reflectionDriver.OwnedProbeCount != 5 ||
                reflectionDriver.SuppressedProductionProbeCount != 10)
            {
                throw new InvalidOperationException("DPBB runtime validation failed. Driver=" +
                                                    driverFailure + " DoorSH=" + doorShFailure +
                                                    " Reflection=" + reflectionFailure);
            }
            ValidateReflectionRuntimeTemplates(
                KExactBasisV1EditorContract.FindUniqueRoot(
                    scene.Scene,
                    DungeonPortalBakedBasisValidationContract
                        .LegacyReflectionOwnerRootName).transform,
                reflectionDriver,
                reflectionProfiles);

            DungeonDoorDualSideProbeReceiver[] productionDoorReceivers =
                scene.DoorLeaf.GetComponentsInChildren<DungeonDoorDualSideProbeReceiver>(true);
            if (productionDoorReceivers.Length != 1 || productionDoorReceivers[0].enabled)
            {
                throw new InvalidOperationException(
                    "The conflicting production door probe receiver must be disabled in the clone.");
            }
        }

        private static void ValidateReflectionRuntimeTemplates(
            Transform legacyReflectionOwnerRoot,
            DungeonPortalBakedBasisReflectionDriver reflectionDriver,
            DungeonPortalRoomReflectionProfile[] profiles)
        {
            if (legacyReflectionOwnerRoot == null ||
                reflectionDriver == null || profiles == null || profiles.Length != 5 ||
                reflectionDriver.GetComponentsInChildren<Renderer>(true).Length != 0)
            {
                throw new InvalidOperationException("DPBB reflection runtime isolation changed.");
            }
            ReflectionProbe[] ownedProbes =
                reflectionDriver.GetComponentsInChildren<ReflectionProbe>(true);
            if (ownedProbes.Length != 5)
                throw new InvalidOperationException("DPBB reflection runtime must own five probes.");

            DungeonPortalRoomReflectionBlend[] legacyBlends =
                legacyReflectionOwnerRoot.GetComponentsInChildren<DungeonPortalRoomReflectionBlend>(true);
            DungeonPortalBakedBasisValidationContract.ReflectionSpec[] specs =
                DungeonPortalBakedBasisValidationContract.ReflectionSpecs;
            for (int i = 0; i < specs.Length; i++)
            {
                DungeonPortalBakedBasisValidationContract.ReflectionSpec spec = specs[i];
                DungeonPortalRoomReflectionBlend legacy = RequireLegacyReflectionBlend(
                    legacyBlends, spec.LegacyProbeObjectName);
                Transform ownedTransform =
                    DungeonPortalBakedBasisValidationContract.FindUniqueDirectChild(
                        reflectionDriver.transform, spec.LegacyProbeObjectName);
                ReflectionProbe[] candidates = ownedTransform.GetComponents<ReflectionProbe>();
                ReflectionProbe[] sourceCandidates = legacy.GetComponents<ReflectionProbe>();
                if (candidates.Length != 1 || sourceCandidates.Length != 1 ||
                    candidates[0].enabled ||
                    candidates[0].customBakedTexture != profiles[i].Power100Cubemap)
                {
                    throw new InvalidOperationException(
                        "DPBB reflection template binding changed at '" +
                        spec.LegacyProbeObjectName + "'.");
                }
                AssertReflectionTemplateParity(sourceCandidates[0], candidates[0]);
            }
        }

        private static void AssertLightmapReferenceParity(
            KExactBasisV1EditorContract.SceneBindings source,
            KExactBasisV1EditorContract.SceneBindings candidate)
        {
            Renderer[] left = source.ProductionRooms.GetComponentsInChildren<Renderer>(true);
            Renderer[] right = candidate.ProductionRooms.GetComponentsInChildren<Renderer>(true);
            if (left.Length != right.Length)
                throw new InvalidOperationException("Renderer traversal count changed before DPBB runtime.");
            LightmapData[] sourceMaps = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
            // Candidate is open additively in the same editor session, so its cloned production
            // renderers reference the same global lightmap table. KExact parity already checks
            // index/ST; this additionally verifies every referenced color/dir/shadow texture.
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i].lightmapIndex < 0 || right[i].lightmapIndex < 0)
                    continue;
                if (left[i].lightmapIndex >= sourceMaps.Length ||
                    right[i].lightmapIndex >= sourceMaps.Length)
                {
                    throw new InvalidOperationException("Renderer references an invalid global lightmap slot.");
                }
                LightmapData sourceMap = sourceMaps[left[i].lightmapIndex];
                LightmapData candidateMap = sourceMaps[right[i].lightmapIndex];
                if (sourceMap.lightmapColor != candidateMap.lightmapColor ||
                    sourceMap.lightmapDir != candidateMap.lightmapDir ||
                    sourceMap.shadowMask != candidateMap.shadowMask)
                {
                    throw new InvalidOperationException("Lightmap texture-reference parity failed at traversal " + i + ".");
                }
            }
        }

        private static void RequireOutputDoesNotExist()
        {
            string sceneAbsolute = DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(
                DungeonPortalBakedBasisValidationContract.BuiltScenePath);
            string manifestAbsolute = DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(
                DungeonPortalBakedBasisValidationContract.BuildManifestPath);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    DungeonPortalBakedBasisValidationContract.BuiltScenePath) != null ||
                File.Exists(sceneAbsolute) || File.Exists(manifestAbsolute))
            {
                throw new InvalidOperationException(
                    "DPBB built scene or manifest already exists; this builder never overwrites evidence.");
            }
        }

        private static void RestoreEditorIdentity(
            Scene originalActive,
            KExactBasisV1EditorContract.SelectionSnapshot selection,
            bool originalPlaying)
        {
            if (originalActive.IsValid() && originalActive.isLoaded &&
                SceneManager.GetActiveScene() != originalActive)
            {
                EditorSceneManager.SetActiveScene(originalActive);
            }
            selection.Restore();
            if (EditorApplication.isPlaying != originalPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode != originalPlaying)
            {
                throw new InvalidOperationException("DPBB builder changed Play Mode state.");
            }
        }

        private static float ToPower01(DungeonTileLightmapSwitcher.PowerLevel powerLevel)
        {
            return powerLevel == DungeonTileLightmapSwitcher.PowerLevel.P100 ? 1f : 0f;
        }

        private static void WriteManifest(
            string sceneSha,
            string protectedAssetsSha,
            KExactBasisV1EditorContract.RendererParityReport parity,
            RuntimeReport runtime,
            DungeonPortalBakedRoomBasisData startBasis,
            DungeonPortalBakedRoomBasisData administrativeBasis)
        {
            var builder = new StringBuilder();
            builder.AppendLine("schema=DPBBSceneBuild/v1");
            builder.AppendLine("status=" + Status);
            builder.AppendLine("buildOutcome=COMPLETE");
            builder.AppendLine("visualVerdict=UNREVIEWED");
            builder.AppendLine("visualParityClaimed=false");
            builder.AppendLine("productionAssetsWritten=false");
            builder.AppendLine("sourceScenePath=" + DungeonPortalBakedBasisValidationContract.SourceScenePath);
            builder.AppendLine("sourceSceneSha256=" + DungeonPortalBakedBasisValidationContract.ExpectedSourceSceneSha256);
            builder.AppendLine("builtScenePath=" + DungeonPortalBakedBasisValidationContract.BuiltScenePath);
            builder.AppendLine("builtSceneSha256=" + sceneSha);
            builder.AppendLine("startBasisPath=" + DungeonPortalBakedBasisValidationContract.StartBasisPath);
            builder.AppendLine("administrativeBasisPath=" + DungeonPortalBakedBasisValidationContract.AdministrativeBasisPath);
            builder.AppendLine("startBasisId=" + startBasis.RoomId);
            builder.AppendLine("administrativeBasisId=" + administrativeBasis.RoomId);
            builder.AppendLine("rendererBindingKey=relativePath#occurrence");
            builder.AppendLine("rendererBindingOrder=GetComponentsInChildren<Renderer>(true) bucket traversal");
            builder.AppendLine("startCanonicalRendererBindings=" + runtime.StartBindingCount);
            builder.AppendLine("administrativeCanonicalRendererBindings=" + runtime.AdministrativeBindingCount);
            builder.AppendLine("doorProbeReceiverDisabledInCloneOnly=true");
            builder.AppendLine("doorProbeReceiverCount=" + runtime.DisabledDoorProbeReceiverCount);
            builder.AppendLine("lightmapComposition=runtime-private-RGBAHalf-atlas;no-material-or-renderer-creation");
            builder.AppendLine("d0Policy=zero-incoming;restore-captured-endpoint;switch-production-endpoint;synchronize-base");
            builder.AppendLine("doorShPolicy=CustomProvided-at-open;exact-MPB-and-probe-usage-restore-at-D0");
            builder.AppendLine("reflectionPolicy=P0Residual-to-P100;constant-intensity;room-power-only;aperture-independent");
            builder.AppendLine("reflectionOwnedProbeCount=" + runtime.ReflectionOwnedProbeCount);
            builder.AppendLine("reflectionSuppressedProductionProbeCount=" +
                               runtime.ReflectionSuppressedProductionProbeCount);
            builder.AppendLine("rendererCount=" + parity.RendererCount);
            builder.AppendLine("additionalRendererCount=" + parity.AdditionalRendererCount);
            builder.AppendLine("rendererParityFingerprintSha256=" + parity.FingerprintSha256);
            builder.AppendLine("protectedProductionFingerprintSha256=" + protectedAssetsSha);
            File.WriteAllText(
                DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(
                    DungeonPortalBakedBasisValidationContract.BuildManifestPath),
                builder.ToString(),
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(DungeonPortalBakedBasisValidationContract.BuildManifestPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        private readonly struct RuntimeReport
        {
            internal readonly int StartBindingCount;
            internal readonly int AdministrativeBindingCount;
            internal readonly int DisabledDoorProbeReceiverCount;
            internal readonly int ReflectionOwnedProbeCount;
            internal readonly int ReflectionSuppressedProductionProbeCount;

            internal RuntimeReport(int startBindingCount, int administrativeBindingCount,
                int disabledDoorProbeReceiverCount, int reflectionOwnedProbeCount,
                int reflectionSuppressedProductionProbeCount)
            {
                StartBindingCount = startBindingCount;
                AdministrativeBindingCount = administrativeBindingCount;
                DisabledDoorProbeReceiverCount = disabledDoorProbeReceiverCount;
                ReflectionOwnedProbeCount = reflectionOwnedProbeCount;
                ReflectionSuppressedProductionProbeCount =
                    reflectionSuppressedProductionProbeCount;
            }
        }
    }
}
