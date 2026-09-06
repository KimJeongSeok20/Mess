using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonPortalTransportPoC.Editor
{
    /// <summary>
    /// Idempotently wires the five canonical P0/P100 reflection pairs into the
    /// isolated Start/Admin portal-transport validation scene.
    ///
    /// Production probe variants retain their prefab baseline while the connection is
    /// OFF. The connection-owned blend suppresses and restores them only while active.
    /// PoC-owned probes live outside both production room prefab hierarchies, so this
    /// authoring path never applies an override to a prefab asset.
    /// </summary>
    public static class DungeonPortalReflectionAuthoring
    {
        private const string ValidationScenePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes/Start_Admin_PortalTransportValidation.unity";
        private const string ReflectionDataFolder =
            "Assets/Experiments/DungeonPortalTransportPoC/Data/Reflection";

        private const string StartRoomPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/StartRoom_R000.prefab";
        private const string AdminRoomPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/AdminstrativeSegregation_R000.prefab";
        private const string BakedDataRoot =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/BakedData";

        private const string StartRoomName = "StartRoom_R000_ProductionInstance";
        private const string AdminRoomName =
            "AdminstrativeSegregation_R000_ProductionInstance";
        private const string StartEnvelopeOwnerName =
            "StartRoom_Endpoint_ProfilePending";
        private const string AdminEnvelopeOwnerName =
            "AdminRoom_Endpoint_ProfilePending";
        private const string PoCOwnerRootName = "DungeonPortalTransportPoC";
        private const string ReflectionRootName = "RoomReflections";
        private const string ConnectionOwnerName =
            "A_to_B_and_B_to_A_Connection_ProfileGate_DISABLED";

        private static readonly ReflectionSpec[] Specs =
        {
            new ReflectionSpec(
                "Start",
                StartRoomName,
                StartRoomPrefabPath,
                StartEnvelopeOwnerName,
                "Tile Reflection Probe_P0",
                "Tile Reflection Probe_P100",
                $"{BakedDataRoot}/TileBake_Auto_StartRoom_R000_P0/ReflectionProbe-0.exr",
                $"{BakedDataRoot}/TileBake_Auto_StartRoom_R000_P100/ReflectionProbe-0.exr",
                $"{ReflectionDataFolder}/StartRoom_R000_ReflectionProfile.asset",
                "StartRoom_R000_Reflection"),
            new ReflectionSpec(
                "Admin S00",
                AdminRoomName,
                AdminRoomPrefabPath,
                AdminEnvelopeOwnerName,
                "Tile Reflection Probe_S00_P0",
                "Tile Reflection Probe_S00_P100",
                $"{BakedDataRoot}/TileBake_Auto_AdminstrativeSegregation_R000_P0/ReflectionProbe-0.exr",
                $"{BakedDataRoot}/TileBake_Auto_AdminstrativeSegregation_R000_P100/ReflectionProbe-0.exr",
                $"{ReflectionDataFolder}/AdminstrativeSegregation_R000_S00_ReflectionProfile.asset",
                "AdminstrativeSegregation_R000_S00_Reflection"),
            new ReflectionSpec(
                "Admin S01",
                AdminRoomName,
                AdminRoomPrefabPath,
                AdminEnvelopeOwnerName,
                "Tile Reflection Probe_S01_P0",
                "Tile Reflection Probe_S01_P100",
                $"{BakedDataRoot}/TileBake_Auto_AdminstrativeSegregation_R000_P0/ReflectionProbe-2.exr",
                $"{BakedDataRoot}/TileBake_Auto_AdminstrativeSegregation_R000_P100/ReflectionProbe-2.exr",
                $"{ReflectionDataFolder}/AdminstrativeSegregation_R000_S01_ReflectionProfile.asset",
                "AdminstrativeSegregation_R000_S01_Reflection"),
            new ReflectionSpec(
                "Admin S02",
                AdminRoomName,
                AdminRoomPrefabPath,
                AdminEnvelopeOwnerName,
                "Tile Reflection Probe_S02_P0",
                "Tile Reflection Probe_S02_P100",
                $"{BakedDataRoot}/TileBake_Auto_AdminstrativeSegregation_R000_P0/ReflectionProbe-4.exr",
                $"{BakedDataRoot}/TileBake_Auto_AdminstrativeSegregation_R000_P100/ReflectionProbe-4.exr",
                $"{ReflectionDataFolder}/AdminstrativeSegregation_R000_S02_ReflectionProfile.asset",
                "AdminstrativeSegregation_R000_S02_Reflection"),
            new ReflectionSpec(
                "Admin S03",
                AdminRoomName,
                AdminRoomPrefabPath,
                AdminEnvelopeOwnerName,
                "Tile Reflection Probe_S03_P0",
                "Tile Reflection Probe_S03_P100",
                $"{BakedDataRoot}/TileBake_Auto_AdminstrativeSegregation_R000_P0/ReflectionProbe-6.exr",
                $"{BakedDataRoot}/TileBake_Auto_AdminstrativeSegregation_R000_P100/ReflectionProbe-6.exr",
                $"{ReflectionDataFolder}/AdminstrativeSegregation_R000_S03_ReflectionProfile.asset",
                "AdminstrativeSegregation_R000_S03_Reflection")
        };

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Author Reflections")]
        public static void ApplyFromMenu()
        {
            string result = Apply();
            if (result.StartsWith("PASS", StringComparison.Ordinal))
                Debug.Log(result);
            else
                Debug.LogError(result);
        }

        /// <summary>
        /// Parameterless Unity -executeMethod entry point.
        /// </summary>
        public static void ApplyCli()
        {
            string result = Apply();
            if (!result.StartsWith("PASS", StringComparison.Ordinal))
                throw new InvalidOperationException(result);

            Debug.Log(result);
        }

        public static string Apply()
        {
            if (!TryValidateEditorState(out string stateFailure))
                return $"FAIL: {stateFailure}";

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ValidationScenePath) == null)
            {
                return
                    "FAIL: the isolated validation scene must be built first: " +
                    ValidationScenePath;
            }

            Scene alreadyLoaded = SceneManager.GetSceneByPath(ValidationScenePath);
            if (alreadyLoaded.IsValid() && alreadyLoaded.isLoaded)
            {
                return
                    "FAIL: the isolated validation scene is already loaded. Close it while " +
                    $"clean before authoring reflections: {ValidationScenePath}";
            }

            if (!TryLoadCanonicalCubemaps(out CubemapPair[] cubemapPairs, out string loadFailure))
                return $"FAIL: {loadFailure}";

            if (!TryCaptureProductionPrefabGuards(
                    out PrefabGuard startPrefabGuard,
                    out PrefabGuard adminPrefabGuard,
                    out string guardFailure))
            {
                return $"FAIL: {guardFailure}";
            }

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene validationScene = default;
            try
            {
                validationScene = EditorSceneManager.OpenScene(
                    ValidationScenePath,
                    OpenSceneMode.Additive);
                if (!validationScene.IsValid() || !validationScene.isLoaded)
                    throw new InvalidOperationException("Unity did not load the validation scene.");

                GameObject startRoom = FindUniqueSceneObject(
                    validationScene,
                    StartRoomName);
                GameObject adminRoom = FindUniqueSceneObject(
                    validationScene,
                    AdminRoomName);
                AssertProductionRoomInstance(
                    startRoom,
                    StartRoomPrefabPath,
                    "Start room");
                AssertProductionRoomInstance(
                    adminRoom,
                    AdminRoomPrefabPath,
                    "Admin room");

                int removedLegacyAdditionalLightOverrides =
                    RestoreProductionLightComponentBaseline(
                        startRoom,
                        StartRoomPrefabPath,
                        "Start room") +
                    RestoreProductionLightComponentBaseline(
                        adminRoom,
                        AdminRoomPrefabPath,
                        "Admin room");

                DungeonPortalPowerEnvelope startEnvelope =
                    FindUniqueComponentOnNamedObject<DungeonPortalPowerEnvelope>(
                        validationScene,
                        StartEnvelopeOwnerName);
                DungeonPortalPowerEnvelope adminEnvelope =
                    FindUniqueComponentOnNamedObject<DungeonPortalPowerEnvelope>(
                        validationScene,
                        AdminEnvelopeOwnerName);
                DungeonPortalRoomPowerBridge startPowerBridge =
                    FindUniqueComponentOnNamedObject<DungeonPortalRoomPowerBridge>(
                        validationScene,
                        StartEnvelopeOwnerName);
                DungeonPortalRoomPowerBridge adminPowerBridge =
                    FindUniqueComponentOnNamedObject<DungeonPortalRoomPowerBridge>(
                        validationScene,
                        AdminEnvelopeOwnerName);
                DungeonPortalTransportConnection connection =
                    FindUniqueComponentOnNamedObject<DungeonPortalTransportConnection>(
                        validationScene,
                        ConnectionOwnerName);

                ProbePair[] sourceProbePairs = new ProbePair[Specs.Length];
                for (int i = 0; i < Specs.Length; i++)
                {
                    ReflectionSpec spec = Specs[i];
                    GameObject room = spec.RoomObjectName == StartRoomName
                        ? startRoom
                        : adminRoom;
                    sourceProbePairs[i] = ResolveProductionProbePair(room, spec);
                    RestoreProductionProbeVariantBaseline(
                        sourceProbePairs[i].Power0,
                        validationScene,
                        spec.RoomPrefabPath,
                        spec.Label + " P0");
                    RestoreProductionProbeVariantBaseline(
                        sourceProbePairs[i].Power100,
                        validationScene,
                        spec.RoomPrefabPath,
                        spec.Label + " P100");
                }

                EnsureAssetFolder(ReflectionDataFolder);
                DungeonPortalRoomReflectionProfile[] profiles =
                    new DungeonPortalRoomReflectionProfile[Specs.Length];
                for (int i = 0; i < Specs.Length; i++)
                {
                    profiles[i] = CreateOrUpdateProfile(
                        Specs[i],
                        cubemapPairs[i]);
                }

                Transform reflectionRoot = GetOrCreateReflectionRoot(validationScene);
                var blends = new List<DungeonPortalRoomReflectionBlend>(Specs.Length);
                for (int i = 0; i < Specs.Length; i++)
                {
                    DungeonPortalPowerEnvelope envelope =
                        Specs[i].RoomObjectName == StartRoomName
                            ? startEnvelope
                            : adminEnvelope;
                    blends.Add(CreateOrUpdatePoCProbe(
                        reflectionRoot,
                        Specs[i],
                        sourceProbePairs[i],
                        profiles[i],
                        envelope));
                }

                connection.ConfigureScopedEffects(
                    new[] { startPowerBridge, adminPowerBridge },
                    blends.ToArray());
                connection.enabled = false;
                EditorUtility.SetDirty(connection);

                AssertProductionPrefabUnchanged(startPrefabGuard);
                AssertProductionPrefabUnchanged(adminPrefabGuard);

                EditorSceneManager.MarkSceneDirty(validationScene);
                if (!EditorSceneManager.SaveScene(
                        validationScene,
                        ValidationScenePath,
                        false))
                {
                    throw new InvalidOperationException(
                        $"Unable to save '{ValidationScenePath}'.");
                }

                for (int i = 0; i < profiles.Length; i++)
                    AssetDatabase.SaveAssetIfDirty(profiles[i]);
                AssertProductionPrefabUnchanged(startPrefabGuard);
                AssertProductionPrefabUnchanged(adminPrefabGuard);

                return
                    "PASS DungeonPortalTransportPoC reflection authoring\n" +
                    $"scene={ValidationScenePath}\n" +
                    $"profiles={Specs.Length}\n" +
                    "probeIntensity=1 (P0 residual comes from canonical P0 cubemaps)\n" +
                    $"legacyAdditionalLightOverridesRemoved={removedLegacyAdditionalLightOverrides}\n" +
                    "connectionEffects=serialized disabled and connection-owned\n" +
                    "productionProbeVariants=restored to prefab baseline while OFF\n" +
                    "productionPrefabAssetChanges=0";
            }
            catch (Exception exception)
            {
                return $"FAIL: reflection authoring threw {exception}";
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    EditorSceneManager.SetActiveScene(previousActiveScene);

                if (validationScene.IsValid() && validationScene.isLoaded)
                    EditorSceneManager.CloseScene(validationScene, true);

                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    EditorSceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static bool TryValidateEditorState(out string failure)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                failure =
                    "Unity is in Play Mode or is transitioning Play Mode; no asset or scene was touched.";
                return false;
            }

            if (EditorApplication.isCompiling)
            {
                failure = "Unity is compiling; no asset or scene was touched.";
                return false;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                {
                    failure =
                        $"loaded scene has unsaved changes: '{scene.name}' ({scene.path}). " +
                        "No asset or scene was touched.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private static bool TryLoadCanonicalCubemaps(
            out CubemapPair[] pairs,
            out string failure)
        {
            pairs = new CubemapPair[Specs.Length];
            for (int i = 0; i < Specs.Length; i++)
            {
                ReflectionSpec spec = Specs[i];
                Cubemap power0 = AssetDatabase.LoadAssetAtPath<Cubemap>(
                    spec.Power0CubemapPath);
                Cubemap power100 = AssetDatabase.LoadAssetAtPath<Cubemap>(
                    spec.Power100CubemapPath);
                if (power0 == null || power100 == null)
                {
                    failure =
                        $"{spec.Label} canonical cubemap pair is missing or is not imported " +
                        $"as Cubemap. P0='{spec.Power0CubemapPath}' loaded={power0 != null}; " +
                        $"P100='{spec.Power100CubemapPath}' loaded={power100 != null}.";
                    return false;
                }

                if (power0 == power100)
                {
                    failure = $"{spec.Label} P0 and P100 resolve to the same cubemap asset.";
                    return false;
                }

                pairs[i] = new CubemapPair(power0, power100);
            }

            failure = null;
            return true;
        }

        private static bool TryCaptureProductionPrefabGuards(
            out PrefabGuard startGuard,
            out PrefabGuard adminGuard,
            out string failure)
        {
            if (!PrefabGuard.TryCapture(StartRoomPrefabPath, out startGuard, out failure))
            {
                adminGuard = default;
                return false;
            }

            if (!PrefabGuard.TryCapture(AdminRoomPrefabPath, out adminGuard, out failure))
                return false;

            failure = null;
            return true;
        }

        private static DungeonPortalRoomReflectionProfile CreateOrUpdateProfile(
            ReflectionSpec spec,
            CubemapPair cubemaps)
        {
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(
                spec.ProfileAssetPath);
            DungeonPortalRoomReflectionProfile profile;
            if (existing == null)
            {
                profile = ScriptableObject.CreateInstance<
                    DungeonPortalRoomReflectionProfile>();
                profile.name = System.IO.Path.GetFileNameWithoutExtension(
                    spec.ProfileAssetPath);
                AssetDatabase.CreateAsset(profile, spec.ProfileAssetPath);
            }
            else
            {
                profile = existing as DungeonPortalRoomReflectionProfile;
                if (profile == null)
                {
                    throw new InvalidOperationException(
                        $"Expected a {nameof(DungeonPortalRoomReflectionProfile)} at " +
                        $"'{spec.ProfileAssetPath}', found {existing.GetType().Name}.");
                }
            }

            // The weak P0 response is encoded by the canonical P0 cubemap itself.
            // Intensity remains physically consistent and fixed across the blend.
            profile.Configure(cubemaps.Power0, cubemaps.Power100, 1f);
            if (!profile.TryValidate(out string validationError))
            {
                throw new InvalidOperationException(
                    $"{spec.Label} reflection profile is invalid: {validationError}");
            }

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static ProbePair ResolveProductionProbePair(
            GameObject roomRoot,
            ReflectionSpec spec)
        {
            GameObject power0 = FindUniqueDescendant(roomRoot, spec.Power0ProbeName);
            GameObject power100 = FindUniqueDescendant(roomRoot, spec.Power100ProbeName);
            AssertSingleReflectionProbe(power0, spec.Label + " P0");
            AssertSingleReflectionProbe(power100, spec.Label + " P100");
            return new ProbePair(power0, power100);
        }

        private static DungeonPortalRoomReflectionBlend CreateOrUpdatePoCProbe(
            Transform reflectionRoot,
            ReflectionSpec spec,
            ProbePair sourceProbePair,
            DungeonPortalRoomReflectionProfile profile,
            DungeonPortalPowerEnvelope envelope)
        {
            GameObject power100SourceObject = sourceProbePair.Power100;
            GameObject targetObject = GetOrCreateUniqueDirectChild(
                reflectionRoot,
                spec.PoCProbeObjectName);
            ReflectionProbe sourceProbe =
                power100SourceObject.GetComponent<ReflectionProbe>();

            ReflectionProbe[] targetProbes =
                targetObject.GetComponents<ReflectionProbe>();
            ReflectionProbe targetProbe = targetProbes.Length > 0
                ? targetProbes[0]
                : targetObject.AddComponent<ReflectionProbe>();
            for (int i = 1; i < targetProbes.Length; i++)
                UnityEngine.Object.DestroyImmediate(targetProbes[i]);

            DungeonPortalRoomReflectionBlend[] blendComponents =
                targetObject.GetComponents<DungeonPortalRoomReflectionBlend>();
            DungeonPortalRoomReflectionBlend blend = blendComponents.Length > 0
                ? blendComponents[0]
                : targetObject.AddComponent<DungeonPortalRoomReflectionBlend>();
            for (int i = 1; i < blendComponents.Length; i++)
                UnityEngine.Object.DestroyImmediate(blendComponents[i]);

            targetObject.transform.SetPositionAndRotation(
                power100SourceObject.transform.position,
                power100SourceObject.transform.rotation);
            targetObject.transform.localScale =
                power100SourceObject.transform.lossyScale;
            targetObject.layer = power100SourceObject.layer;
            targetObject.tag = power100SourceObject.tag;
            GameObjectUtility.SetStaticEditorFlags(
                targetObject,
                GameObjectUtility.GetStaticEditorFlags(power100SourceObject));

            // CopySerialized keeps every version-specific ReflectionProbe spatial and
            // capture setting aligned with the production P100 variant. The fields below
            // are then intentionally overridden for the PoC runtime blend owner.
            EditorUtility.CopySerialized(sourceProbe, targetProbe);
            targetProbe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
            targetProbe.customBakedTexture = profile.Power100Cubemap;
            targetProbe.intensity = 1f;
            // The runtime blend component enables this probe only after the first
            // P0/P100 cubemap blend succeeds. Serialized disabled is the fail-closed
            // baseline if allocation or ReflectionProbe.BlendCubemap fails.
            targetProbe.enabled = false;
            blend.Configure(
                profile,
                envelope,
                new[]
                {
                    sourceProbePair.Power0.GetComponent<ReflectionProbe>(),
                    sourceProbePair.Power100.GetComponent<ReflectionProbe>()
                });
            blend.enabled = false;
            targetObject.SetActive(true);

            EditorUtility.SetDirty(targetObject.transform);
            EditorUtility.SetDirty(targetProbe);
            EditorUtility.SetDirty(blend);
            return blend;
        }

        private static Transform GetOrCreateReflectionRoot(Scene scene)
        {
            GameObject owner = FindOptionalUniqueRoot(scene, PoCOwnerRootName);
            if (owner == null)
            {
                owner = new GameObject(PoCOwnerRootName);
                SceneManager.MoveGameObjectToScene(owner, scene);
            }

            owner.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            owner.transform.localScale = Vector3.one;

            GameObject reflectionRoot = GetOrCreateUniqueDirectChild(
                owner.transform,
                ReflectionRootName);
            reflectionRoot.transform.localPosition = Vector3.zero;
            reflectionRoot.transform.localRotation = Quaternion.identity;
            reflectionRoot.transform.localScale = Vector3.one;
            EditorUtility.SetDirty(owner.transform);
            EditorUtility.SetDirty(reflectionRoot.transform);
            return reflectionRoot.transform;
        }

        private static GameObject GetOrCreateUniqueDirectChild(
            Transform parent,
            string name)
        {
            GameObject result = null;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child.name != name)
                    continue;

                if (result == null)
                    result = child.gameObject;
                else
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            if (result != null)
                return result;

            result = new GameObject(name);
            result.transform.SetParent(parent, false);
            return result;
        }

        private static GameObject FindOptionalUniqueRoot(Scene scene, string name)
        {
            GameObject result = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null || roots[i].name != name)
                    continue;

                if (result != null)
                {
                    throw new InvalidOperationException(
                        $"Scene contains multiple root objects named '{name}'.");
                }

                result = roots[i];
            }

            return result;
        }

        private static GameObject FindUniqueSceneObject(Scene scene, string name)
        {
            GameObject result = null;
            int count = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Transform[] transforms =
                    roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (transforms[i].name != name)
                        continue;

                    result = transforms[i].gameObject;
                    count++;
                }
            }

            if (count != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one scene object named '{name}', found {count}.");
            }

            return result;
        }

        private static T FindUniqueComponentOnNamedObject<T>(
            Scene scene,
            string ownerName)
            where T : Component
        {
            GameObject owner = FindUniqueSceneObject(scene, ownerName);
            T[] components = owner.GetComponents<T>();
            if (components.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one {typeof(T).Name} on '{ownerName}', " +
                    $"found {components.Length}.");
            }

            return components[0];
        }

        private static GameObject FindUniqueDescendant(GameObject root, string name)
        {
            GameObject result = null;
            int count = 0;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name != name)
                    continue;

                result = transforms[i].gameObject;
                count++;
            }

            if (count != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one '{name}' below '{root.name}', found {count}.");
            }

            return result;
        }

        private static void AssertSingleReflectionProbe(
            GameObject probeObject,
            string label)
        {
            ReflectionProbe[] probes = probeObject.GetComponents<ReflectionProbe>();
            if (probes.Length != 1)
            {
                throw new InvalidOperationException(
                    $"{label} object '{probeObject.name}' must own exactly one " +
                    $"ReflectionProbe; found {probes.Length}.");
            }
        }

        private static void AssertProductionRoomInstance(
            GameObject room,
            string expectedPrefabPath,
            string label)
        {
            if (room.scene.path != ValidationScenePath)
            {
                throw new InvalidOperationException(
                    $"{label} is not owned by the isolated validation scene.");
            }

            string sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(room);
            if (sourcePath != expectedPrefabPath)
            {
                throw new InvalidOperationException(
                    $"{label} source prefab mismatch. expected='{expectedPrefabPath}' " +
                    $"actual='{sourcePath}'.");
            }
        }

        private static void RestoreProductionProbeVariantBaseline(
            GameObject probeObject,
            Scene validationScene,
            string productionPrefabPath,
            string label)
        {
            if (probeObject.scene != validationScene ||
                PrefabUtility.IsPartOfPrefabAsset(probeObject))
            {
                throw new InvalidOperationException(
                    $"Refusing to restore {label}: target is not an isolated scene instance.");
            }

            GameObject prefabSource =
                PrefabUtility.GetCorrespondingObjectFromSourceAtPath<GameObject>(
                    probeObject,
                    productionPrefabPath);
            if (prefabSource == null || !PrefabUtility.IsPartOfPrefabAsset(prefabSource))
            {
                throw new InvalidOperationException(
                    $"Cannot resolve the production prefab baseline for {label}.");
            }

            probeObject.SetActive(prefabSource.activeSelf);
            EditorUtility.SetDirty(probeObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(probeObject);
        }

        private static int RestoreProductionLightComponentBaseline(
            GameObject room,
            string productionPrefabPath,
            string label)
        {
            GameObject productionPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(productionPrefabPath);
            if (productionPrefab == null)
            {
                throw new InvalidOperationException(
                    $"Cannot load the production prefab baseline for {label}: " +
                    $"'{productionPrefabPath}'.");
            }

            var baselineByKey = new Dictionary<string, Light>(StringComparer.Ordinal);
            var baselineUseCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            Light[] baselineLights =
                productionPrefab.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < baselineLights.Length; i++)
            {
                Light baseline = baselineLights[i];
                string path = AnimationUtility.CalculateTransformPath(
                    baseline.transform,
                    productionPrefab.transform);
                baselineUseCounts.TryGetValue(path, out int bucketIndex);
                baselineUseCounts[path] = bucketIndex + 1;
                baselineByKey.Add(path + "|" + bucketIndex, baseline);
            }

            int removed = 0;
            var roomUseCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            Light[] roomLights = room.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < roomLights.Length; i++)
            {
                Light light = roomLights[i];
                string path = AnimationUtility.CalculateTransformPath(
                    light.transform,
                    room.transform);
                roomUseCounts.TryGetValue(path, out int bucketIndex);
                roomUseCounts[path] = bucketIndex + 1;
                string key = path + "|" + bucketIndex;
                if (!baselineByKey.TryGetValue(key, out Light baseline))
                {
                    throw new InvalidOperationException(
                        $"{label} light '{key}' has no production prefab baseline.");
                }

                var currentAdditional =
                    light.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
                var baselineAdditional =
                    baseline.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
                if (baselineAdditional != null && currentAdditional == null)
                {
                    throw new InvalidOperationException(
                        $"{label} light '{key}' is missing its production " +
                        "UniversalAdditionalLightData component.");
                }

                if (baselineAdditional != null || currentAdditional == null)
                    continue;

                if (!PrefabUtility.IsAddedComponentOverride(currentAdditional))
                {
                    throw new InvalidOperationException(
                        $"Refusing to remove non-override UniversalAdditionalLightData " +
                        $"from {label} light '{key}'.");
                }

                UnityEngine.Object.DestroyImmediate(currentAdditional);
                EditorUtility.SetDirty(light.gameObject);
                removed++;
            }

            return removed;
        }

        private static void AssertProductionPrefabUnchanged(PrefabGuard guard)
        {
            Hash128 currentHash = AssetDatabase.GetAssetDependencyHash(guard.AssetPath);
            bool currentlyDirty = EditorUtility.IsDirty(guard.Asset);
            if (currentHash != guard.DependencyHash ||
                currentlyDirty)
            {
                throw new InvalidOperationException(
                    "Production prefab guard changed during PoC reflection authoring: " +
                    $"path='{guard.AssetPath}' hashBefore={guard.DependencyHash} " +
                    $"hashAfter={currentHash} dirtyBefore=false " +
                    $"dirtyAfter={currentlyDirty}.");
            }
        }

        private static void EnsureAssetFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            int separator = folder.LastIndexOf('/');
            if (separator <= 0 || separator >= folder.Length - 1)
                throw new InvalidOperationException($"Invalid asset folder '{folder}'.");

            string parent = folder.Substring(0, separator);
            string child = folder.Substring(separator + 1);
            EnsureAssetFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, child)))
                throw new InvalidOperationException($"Unable to create asset folder '{folder}'.");
        }

        private sealed class ReflectionSpec
        {
            public ReflectionSpec(
                string label,
                string roomObjectName,
                string roomPrefabPath,
                string envelopeOwnerName,
                string power0ProbeName,
                string power100ProbeName,
                string power0CubemapPath,
                string power100CubemapPath,
                string profileAssetPath,
                string poCProbeObjectName)
            {
                Label = label;
                RoomObjectName = roomObjectName;
                RoomPrefabPath = roomPrefabPath;
                EnvelopeOwnerName = envelopeOwnerName;
                Power0ProbeName = power0ProbeName;
                Power100ProbeName = power100ProbeName;
                Power0CubemapPath = power0CubemapPath;
                Power100CubemapPath = power100CubemapPath;
                ProfileAssetPath = profileAssetPath;
                PoCProbeObjectName = poCProbeObjectName;
            }

            public string Label { get; }
            public string RoomObjectName { get; }
            public string RoomPrefabPath { get; }
            public string EnvelopeOwnerName { get; }
            public string Power0ProbeName { get; }
            public string Power100ProbeName { get; }
            public string Power0CubemapPath { get; }
            public string Power100CubemapPath { get; }
            public string ProfileAssetPath { get; }
            public string PoCProbeObjectName { get; }
        }

        private readonly struct CubemapPair
        {
            public CubemapPair(Cubemap power0, Cubemap power100)
            {
                Power0 = power0;
                Power100 = power100;
            }

            public Cubemap Power0 { get; }
            public Cubemap Power100 { get; }
        }

        private readonly struct ProbePair
        {
            public ProbePair(GameObject power0, GameObject power100)
            {
                Power0 = power0;
                Power100 = power100;
            }

            public GameObject Power0 { get; }
            public GameObject Power100 { get; }
        }

        private readonly struct PrefabGuard
        {
            private PrefabGuard(
                string assetPath,
                GameObject asset,
                Hash128 dependencyHash)
            {
                AssetPath = assetPath;
                Asset = asset;
                DependencyHash = dependencyHash;
            }

            public string AssetPath { get; }
            public GameObject Asset { get; }
            public Hash128 DependencyHash { get; }

            public static bool TryCapture(
                string assetPath,
                out PrefabGuard guard,
                out string failure)
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (asset == null || !PrefabUtility.IsPartOfPrefabAsset(asset))
                {
                    guard = default;
                    failure = $"Production prefab is missing or invalid: '{assetPath}'.";
                    return false;
                }

                if (EditorUtility.IsDirty(asset))
                {
                    guard = default;
                    failure =
                        $"Production prefab is already dirty; refusing a global save side effect: " +
                        $"'{assetPath}'.";
                    return false;
                }

                guard = new PrefabGuard(
                    assetPath,
                    asset,
                    AssetDatabase.GetAssetDependencyHash(assetPath));
                failure = null;
                return true;
            }
        }
    }
}
