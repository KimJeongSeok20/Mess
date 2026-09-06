using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
    /// Copies the pinned validation scene into the owned KExactBasisV1 folder, then
    /// authors only the copied scene. The active source scene, selection, production
    /// prefabs, bake data, Flow, TileSets, and MapList are hash-guarded and never saved.
    /// </summary>
    public static class KExactBasisV1SceneBuilder
    {
        public const string Status = "KEXACT_BASIS_V1_SCENE_BUILD_CANDIDATE";

        // R8 keeps the exact selected production-light basis and all production surfaces.
        // R7 proved that adding a wall lobe without reallocating the existing energy budget
        // over-lit the semantic receiver ROI by 1.54x/3.60x. Linear-HDR unit-response fits
        // therefore lower the Start->Administrative wall-only lobe to its RMSE minimum and
        // split the reverse direction between the original R6 floor geometry and a small
        // wall/column lobe. The totals remain capped at four lights per receiver renderer.
        private const float StartToAdministrativeWallBounceIntensity = 0.0049862f;
        private const float AdministrativeToStartFloorBounceIntensity = 0.085f;
        private const float AdministrativeToStartWallBounceIntensity = 0.0095f;
        private static readonly Vector3 WallBounceLocalPosition =
            new Vector3(0f, 1.5f, -1.8f);
        private static readonly Vector3 WallBounceLocalEulerAngles =
            Vector3.zero;
        private const float WallBounceRange = 10f;
        private const float WallBounceSpotAngle = 155f;
        private const float WallBounceInnerSpotAngle = 80f;
        private static readonly Vector3 FloorBounceLocalPosition =
            new Vector3(0f, 1.15f, -0.45f);
        private static readonly Vector3 FloorBounceLocalEulerAngles =
            new Vector3(0f, 180f, 0f);
        private const float FloorBounceRange = 22f;
        private const float FloorBounceSpotAngle = 155f;
        private const float FloorBounceInnerSpotAngle = 100f;
        private const float DoorSurfaceIntensity = 0.035f;
        private const float DoorSurfaceRange = 1.75f;
        private const float DoorSurfaceSpotAngle = 100f;
        private const float DoorSurfaceInnerSpotAngle = 50f;
        private const float P0ResidualReflection = 0.025f;
        private const float P100Reflection = 0.25f;
        private const float ReflectionProbeIntensityAtWeightOne = 0.08f;

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Transport PoC/KExactBasisV1/" +
            "Build Isolated Scene (Edit Mode)")]
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
            Scene sourceScene = KExactBasisV1EditorContract.RequireCleanSourceSceneActive();
            var selection = KExactBasisV1EditorContract.SelectionSnapshot.Capture();
            var protectedAssets =
                KExactBasisV1EditorContract.ProtectedAssetSnapshot.Capture();
            var sourceBindings = KExactBasisV1EditorContract.ResolveSceneBindings(
                sourceScene, true, false);
            Scene originalActive = SceneManager.GetActiveScene();
            bool originalPlaying = EditorApplication.isPlaying;

            KExactBasisV1EditorContract.EnsureOwnedFolders();
            KExactBasisV1EditorContract.AssertImportSafePath(
                KExactBasisV1EditorContract.BuiltScenePath,
                true,
                "canonical KExactBasisV1 scene");
            KExactBasisV1EditorContract.AssertImportSafePath(
                KExactBasisV1EditorContract.BuildManifestPath,
                true,
                "KExactBasisV1 build manifest");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    KExactBasisV1EditorContract.BuiltScenePath) != null ||
                File.Exists(KExactBasisV1EditorContract.AssetPathToAbsolutePath(
                    KExactBasisV1EditorContract.BuiltScenePath)) ||
                File.Exists(KExactBasisV1EditorContract.AssetPathToAbsolutePath(
                    KExactBasisV1EditorContract.BuildManifestPath)))
            {
                throw new InvalidOperationException(
                    "Canonical KExactBasisV1 scene/build manifest already exists. " +
                    "This fail-closed builder never overwrites an evidence-bearing scene.");
            }

            string token = Guid.NewGuid().ToString("N").Substring(0, 12);
            string stagingScenePath =
                KExactBasisV1EditorContract.SceneFolder + "/_S_" + token + ".unity";
            KExactBasisV1EditorContract.AssertImportSafePath(
                stagingScenePath, true, "staging KExactBasisV1 scene");
            Scene stagingScene = default;
            bool stagingOpened = false;
            bool published = false;
            RuntimeBuildReport runtimeReport = null;
            KExactBasisV1EditorContract.RendererParityReport parity = default;

            try
            {
                if (!AssetDatabase.CopyAsset(
                        KExactBasisV1EditorContract.SourceScenePath,
                        stagingScenePath))
                    throw new IOException("AssetDatabase.CopyAsset rejected the staging scene.");
                AssetDatabase.ImportAsset(
                    stagingScenePath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);

                stagingScene = EditorSceneManager.OpenScene(
                    stagingScenePath,
                    OpenSceneMode.Additive);
                stagingOpened = stagingScene.IsValid() && stagingScene.isLoaded;
                if (!stagingOpened)
                    throw new InvalidOperationException("Copied staging scene did not load.");
                if (!EditorSceneManager.SetActiveScene(stagingScene))
                    throw new InvalidOperationException("Could not activate the staging scene.");

                var before = KExactBasisV1EditorContract.ResolveSceneBindings(
                    stagingScene, true, false);
                KExactBasisV1EditorContract.AssertRendererParity(sourceBindings, before);

                before.OldPocRoot.gameObject.SetActive(false);
                GameObject runtimeRootObject = new GameObject(
                    KExactBasisV1EditorContract.KExactRuntimeRootName);
                SceneManager.MoveGameObjectToScene(runtimeRootObject, stagingScene);
                runtimeRootObject.transform.SetParent(before.Root.transform, false);
                runtimeReport = ConfigureRuntime(runtimeRootObject.transform, before);

                var after = KExactBasisV1EditorContract.ResolveSceneBindings(
                    stagingScene, false, true);
                ValidateRuntimeScene(after);
                parity = KExactBasisV1EditorContract.AssertRendererParity(
                    sourceBindings, after);

                EditorSceneManager.MarkSceneDirty(stagingScene);
                if (!EditorSceneManager.SaveScene(stagingScene, stagingScenePath, false))
                    throw new IOException("Could not save the owned staging scene.");
                if (sourceScene.isDirty)
                    throw new InvalidOperationException("Staging authoring dirtied the source scene.");

                if (!EditorSceneManager.CloseScene(stagingScene, true))
                    throw new InvalidOperationException("Could not close the staging scene.");
                stagingOpened = false;
                RestoreEditorIdentity(originalActive, selection, originalPlaying);
                protectedAssets.AssertUnchanged();

                string moveError = AssetDatabase.MoveAsset(
                    stagingScenePath,
                    KExactBasisV1EditorContract.BuiltScenePath);
                if (!string.IsNullOrEmpty(moveError))
                    throw new IOException("Could not publish built scene: " + moveError);
                published = true;
                AssetDatabase.ImportAsset(
                    KExactBasisV1EditorContract.BuiltScenePath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);

                Scene verificationScene = EditorSceneManager.OpenScene(
                    KExactBasisV1EditorContract.BuiltScenePath,
                    OpenSceneMode.Additive);
                try
                {
                    var verification = KExactBasisV1EditorContract.ResolveSceneBindings(
                        verificationScene, false, true);
                    ValidateRuntimeScene(verification);
                    parity = KExactBasisV1EditorContract.AssertRendererParity(
                        sourceBindings, verification);
                    if (verificationScene.isDirty || sourceScene.isDirty)
                        throw new InvalidOperationException(
                            "Read-only post-publish verification dirtied a scene.");
                }
                finally
                {
                    if (verificationScene.IsValid() && verificationScene.isLoaded)
                        EditorSceneManager.CloseScene(verificationScene, true);
                }

                RestoreEditorIdentity(originalActive, selection, originalPlaying);
                protectedAssets.AssertUnchanged();
                string builtSha = KExactBasisV1EditorContract.ComputeFileSha256(
                    KExactBasisV1EditorContract.BuiltScenePath);
                WriteBuildManifest(
                    builtSha,
                    protectedAssets.FingerprintSha256,
                    parity,
                    runtimeReport);
                protectedAssets.AssertUnchanged();
                RestoreEditorIdentity(originalActive, selection, originalPlaying);

                return "PASS " + Status + "\n" +
                       "scene=" + KExactBasisV1EditorContract.BuiltScenePath + "\n" +
                       "sceneSha256=" + builtSha + "\n" +
                       "startK=" + runtimeReport.StartK + "\n" +
                       "administrativeK=" + runtimeReport.AdministrativeK + "\n" +
                       "bounceProxyCount=" + runtimeReport.BounceProxyCount + "\n" +
                       "reflectionBasisCount=" + runtimeReport.ReflectionBasisCount + "\n" +
                       "rendererParityFingerprint=" + parity.FingerprintSha256 + "\n" +
                       "additionalRendererCount=" + parity.AdditionalRendererCount + "\n" +
                       "productionAssetsWritten=0";
            }
            catch (Exception exception)
            {
                TryWriteBuildFailureMarker(token, stagingScenePath, published, exception);
                throw;
            }
            finally
            {
                if (stagingOpened && stagingScene.IsValid() && stagingScene.isLoaded)
                    EditorSceneManager.CloseScene(stagingScene, true);
                RestoreEditorIdentity(originalActive, selection, originalPlaying);
                protectedAssets.AssertUnchanged();
            }
        }

        private static RuntimeBuildReport ConfigureRuntime(
            Transform runtimeRoot,
            KExactBasisV1EditorContract.SceneBindings scene)
        {
            if (runtimeRoot == null || scene.KExactRoot != null ||
                runtimeRoot.GetComponentsInChildren<Renderer>(true).Length != 0)
                throw new InvalidOperationException("Runtime root creation contract failed.");

            GameObject sourcesObject = CreateChild("01_ScalarSources", runtimeRoot);
            KExactScalarSource startPower =
                CreateChild("StartPower_Live", sourcesObject.transform)
                    .AddComponent<KExactScalarSource>();
            KExactScalarSource administrativePower =
                CreateChild("AdministrativePower_Live", sourcesObject.transform)
                    .AddComponent<KExactScalarSource>();
            DungeonPortalDoorAngleSource liveDoorAngle = CreateLiveDoorAngleSource(
                sourcesObject.transform,
                scene.DoorAngleSource,
                scene.DoorLeaf);
            KExactScalarSource doorSource =
                CreateChild("DoorOpenness_Live", sourcesObject.transform)
                    .AddComponent<KExactScalarSource>();
            startPower.ConfigureMember(
                scene.StartSwitcher,
                nameof(DungeonTileLightmapSwitcher.CurrentPowerLevel));
            administrativePower.ConfigureMember(
                scene.AdministrativeSwitcher,
                nameof(DungeonTileLightmapSwitcher.CurrentPowerLevel));
            doorSource.ConfigureMember(
                liveDoorAngle,
                nameof(DungeonPortalDoorAngleSource.ApertureFraction));

            GameObject connectionObject = CreateChild(
                "02_Start_Admin_KExactConnection", runtimeRoot);
            Transform lightRoot = CreateChild(
                "ExactProductionLightClones", connectionObject.transform).transform;
            Light[] startBasis = CloneSelectedLights(
                scene.SelectedStartLights,
                CreateChild("Start_K3", lightRoot).transform,
                "S");
            Light[] administrativeBasis = CloneSelectedLights(
                scene.SelectedAdministrativeLights,
                CreateChild("Administrative_K2", lightRoot).transform,
                "A");

            Renderer[] doorLeafRenderers =
                scene.DoorLeaf.GetComponentsInChildren<Renderer>(true);
            var doorSet = new HashSet<Renderer>(doorLeafRenderers);
            Renderer[] startReceivers = scene.StartRoom
                .GetComponentsInChildren<Renderer>(true)
                .Where(renderer => !doorSet.Contains(renderer))
                .ToArray();
            Renderer[] administrativeReceivers = scene.AdministrativeRoom
                .GetComponentsInChildren<Renderer>(true)
                .Where(renderer => !doorSet.Contains(renderer))
                .ToArray();
            Renderer[] shadowCasters = scene.ProductionRooms
                .GetComponentsInChildren<Renderer>(true);
            if (startReceivers.Length == 0 || administrativeReceivers.Length == 0 ||
                doorLeafRenderers.Length == 0 ||
                shadowCasters.Length != KExactBasisV1EditorContract.ExpectedRendererCount)
                throw new InvalidOperationException("Receiver/door/shadow-caster binding failed.");

            Transform startDoorway = scene.StartRoom.Find("Doorways/Door_SM_A/DoorWayPoint");
            Transform administrativeDoorway =
                scene.AdministrativeRoom.Find("Doorways/Door_SM_A/DoorWayPoint");
            if (startDoorway == null || administrativeDoorway == null)
                throw new InvalidOperationException("Doorway bounce anchors are missing.");

            var aToB = new KExactDirectedTransportBinding();
            aToB.Configure(
                "Start_to_Administrative",
                "Start_Admin_KExactBasisV1",
                startPower,
                startBasis,
                administrativeReceivers,
                KExactBasisV1EditorContract.AdministrativeReceiverLayerBit,
                0f,
                1f,
                P0ResidualReflection,
                P100Reflection);
            KExactBounceProxyDescriptor[] aToBBounce =
                CreateStartToAdministrativeBounceDescriptors(
                    AverageColor(startBasis));
            aToB.ConfigureReceiverBounceProxies(
                administrativeDoorway,
                aToBBounce.Where(proxy =>
                    proxy.Role == KExactProxyRole.ReceiverBounce).ToArray());
            aToB.ConfigureDoorSurfaceProxies(
                startDoorway,
                aToBBounce.Where(proxy =>
                    proxy.Role == KExactProxyRole.DoorSurface).ToArray());

            var bToA = new KExactDirectedTransportBinding();
            bToA.Configure(
                "Administrative_to_Start",
                "Start_Admin_KExactBasisV1",
                administrativePower,
                administrativeBasis,
                startReceivers,
                KExactBasisV1EditorContract.StartReceiverLayerBit,
                0f,
                1f,
                P0ResidualReflection,
                P100Reflection);
            KExactBounceProxyDescriptor[] bToABounce =
                CreateAdministrativeToStartBounceDescriptors(
                    AverageColor(administrativeBasis));
            bToA.ConfigureReceiverBounceProxies(
                startDoorway,
                bToABounce.Where(proxy =>
                    proxy.Role == KExactProxyRole.ReceiverBounce).ToArray());
            bToA.ConfigureDoorSurfaceProxies(
                administrativeDoorway,
                bToABounce.Where(proxy =>
                    proxy.Role == KExactProxyRole.DoorSurface).ToArray());

            CubemapPair startReflection = ResolveNearestReflectionPair(
                scene.StartPowerSet, scene.StartRoom, startDoorway.position, "Start");
            CubemapPair administrativeReflection = ResolveNearestReflectionPair(
                scene.AdministrativePowerSet,
                scene.AdministrativeRoom,
                administrativeDoorway.position,
                "Administrative");
            KExactReflectionProbeBlend aToBReflection = CreateReflectionBasis(
                connectionObject.transform,
                "Reflection_Start_to_Administrative",
                KExactReflectionProbeBlend.TransportDirection.AToB,
                startReflection,
                administrativeReflection.SourceProbe);
            KExactReflectionProbeBlend bToAReflection = CreateReflectionBasis(
                connectionObject.transform,
                "Reflection_Administrative_to_Start",
                KExactReflectionProbeBlend.TransportDirection.BToA,
                administrativeReflection,
                startReflection.SourceProbe);

            KExactPortalConnection connection =
                connectionObject.AddComponent<KExactPortalConnection>();
            connection.Configure(
                "Start_Admin_R000_Door_SM_A_KExactBasisV1",
                aToB,
                bToA,
                shadowCasters,
                doorLeafRenderers,
                doorSource,
                KExactBasisV1EditorContract.TransportCasterLayerBit,
                KExactBasisV1EditorContract.DoorReceiverLayerBit);
            connection.ConfigureReflectionBlends(new[] { aToBReflection, bToAReflection });
            connection.ConfigureSmoothing(0.35f, 0.5f, 0.2f, 0.2f);
            connection.SetActivateOnEnable(false);

            KExactPortalRuntimeManager manager =
                runtimeRoot.gameObject.AddComponent<KExactPortalRuntimeManager>();
            manager.Configure(new[] { connection });

            EditorUtility.SetDirty(startPower);
            EditorUtility.SetDirty(administrativePower);
            EditorUtility.SetDirty(doorSource);
            EditorUtility.SetDirty(liveDoorAngle);
            EditorUtility.SetDirty(aToBReflection);
            EditorUtility.SetDirty(bToAReflection);
            EditorUtility.SetDirty(connection);
            EditorUtility.SetDirty(manager);

            if (runtimeRoot.GetComponentsInChildren<Renderer>(true).Length != 0)
                throw new InvalidOperationException("KExactBasisV1 authored a forbidden Renderer.");
            return new RuntimeBuildReport(
                startBasis.Length,
                administrativeBasis.Length,
                aToBBounce.Length + bToABounce.Length,
                2,
                startReflection,
                administrativeReflection);
        }

        internal static void ValidateRuntimeScene(
            KExactBasisV1EditorContract.SceneBindings scene)
        {
            if (scene.KExactRoot == null || scene.OldPocRoot.gameObject.activeSelf ||
                scene.KExactRoot.GetComponentsInChildren<Renderer>(true).Length != 0)
                throw new InvalidOperationException("KExactBasisV1 clone-only root contract failed.");

            KExactScalarSource[] sources =
                scene.KExactRoot.GetComponentsInChildren<KExactScalarSource>(true);
            DungeonPortalDoorAngleSource[] liveDoorSources =
                scene.KExactRoot.GetComponentsInChildren<DungeonPortalDoorAngleSource>(true);
            KExactPortalConnection[] connections =
                scene.KExactRoot.GetComponentsInChildren<KExactPortalConnection>(true);
            KExactPortalRuntimeManager[] managers =
                scene.KExactRoot.GetComponentsInChildren<KExactPortalRuntimeManager>(true);
            KExactReflectionProbeBlend[] reflection =
                scene.KExactRoot.GetComponentsInChildren<KExactReflectionProbeBlend>(true);
            Light[] basisLights = scene.KExactRoot.GetComponentsInChildren<Light>(true);
            if (sources.Length != 3 || liveDoorSources.Length != 1 ||
                connections.Length != 1 || managers.Length != 1 ||
                reflection.Length != 2 || basisLights.Length != 5)
                throw new InvalidOperationException("KExactBasisV1 component counts changed.");
            KExactScalarSource startSource = sources.SingleOrDefault(source =>
                source.name == "StartPower_Live");
            KExactScalarSource administrativeSource = sources.SingleOrDefault(source =>
                source.name == "AdministrativePower_Live");
            KExactScalarSource doorSource = sources.SingleOrDefault(source =>
                source.name == "DoorOpenness_Live");
            DungeonPortalDoorAngleSource liveDoorSource = liveDoorSources[0];
            if (startSource == null || administrativeSource == null || doorSource == null ||
                startSource.Mode != KExactScalarSource.SourceMode.PublicMember ||
                administrativeSource.Mode != KExactScalarSource.SourceMode.PublicMember ||
                doorSource.Mode != KExactScalarSource.SourceMode.PublicMember ||
                startSource.SourceComponent != scene.StartSwitcher ||
                administrativeSource.SourceComponent != scene.AdministrativeSwitcher ||
                doorSource.SourceComponent != liveDoorSource ||
                startSource.PublicMemberName !=
                    nameof(DungeonTileLightmapSwitcher.CurrentPowerLevel) ||
                administrativeSource.PublicMemberName !=
                    nameof(DungeonTileLightmapSwitcher.CurrentPowerLevel) ||
                doorSource.PublicMemberName !=
                    nameof(DungeonPortalDoorAngleSource.ApertureFraction) ||
                startSource.HasRuntimeOverride || administrativeSource.HasRuntimeOverride ||
                doorSource.HasRuntimeOverride || !liveDoorSource.IsConfigured ||
                liveDoorSource.DoorLeaf != scene.DoorLeaf ||
                !liveDoorSource.gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "Live power/door scalar binding contract changed.");
            }
            if (basisLights.Count(light => light.enabled) != 0 ||
                basisLights.Any(light => light.lightmapBakeType != LightmapBakeType.Realtime ||
                                         !Mathf.Approximately(light.bounceIntensity, 0f)))
                throw new InvalidOperationException("Basis Lights must serialize disabled/realtime/direct-only.");
            KExactPortalConnection connection = connections[0];
            if (connection.IsTransportActive || connection.IsFaultLatched ||
                connection.AToB == null || connection.BToA == null ||
                connection.ConnectionKey !=
                    "Start_Admin_R000_Door_SM_A_KExactBasisV1" ||
                connection.AToB.DirectedKey != "Start_to_Administrative" ||
                connection.BToA.DirectedKey != "Administrative_to_Start" ||
                connection.AToB.LayerGroupKey != "Start_Admin_KExactBasisV1" ||
                connection.BToA.LayerGroupKey != "Start_Admin_KExactBasisV1" ||
                connection.AToB.SelectedProductionLights.Length !=
                KExactBasisV1EditorContract.ExpectedStartK ||
                connection.BToA.SelectedProductionLights.Length !=
                KExactBasisV1EditorContract.ExpectedAdministrativeK ||
                connection.AToB.ReceiverRenderingLayerBit !=
                    KExactBasisV1EditorContract.AdministrativeReceiverLayerBit ||
                connection.BToA.ReceiverRenderingLayerBit !=
                    KExactBasisV1EditorContract.StartReceiverLayerBit ||
                !Mathf.Approximately(connection.AToB.DirectIntensityScaleAtPower0, 0f) ||
                !Mathf.Approximately(connection.AToB.DirectIntensityScaleAtPower100, 1f) ||
                !Mathf.Approximately(connection.BToA.DirectIntensityScaleAtPower0, 0f) ||
                !Mathf.Approximately(connection.BToA.DirectIntensityScaleAtPower100, 1f) ||
                !Mathf.Approximately(connection.AToB.ResidualReflectionAtPower0,
                    P0ResidualReflection) ||
                !Mathf.Approximately(connection.AToB.ReflectionAtPower100,
                    P100Reflection) ||
                !Mathf.Approximately(connection.BToA.ResidualReflectionAtPower0,
                    P0ResidualReflection) ||
                !Mathf.Approximately(connection.BToA.ReflectionAtPower100,
                    P100Reflection) ||
                connection.AToB.ReceiverBounceProxies.Length != 1 ||
                connection.BToA.ReceiverBounceProxies.Length != 2 ||
                connection.AToB.DoorSurfaceProxies.Length != 1 ||
                connection.BToA.DoorSurfaceProxies.Length != 1 ||
                connection.AToB.ReceiverBounceAnchor == null ||
                connection.BToA.ReceiverBounceAnchor == null ||
                connection.AToB.DoorSurfaceAnchor == null ||
                connection.BToA.DoorSurfaceAnchor == null ||
                !IsExpectedWallBounceDescriptor(
                    connection.AToB.ReceiverBounceProxies[0],
                    "S2A_Receiver_Wall_Bounce",
                    StartToAdministrativeWallBounceIntensity) ||
                !connection.BToA.ReceiverBounceProxies.Any(proxy =>
                    IsExpectedFloorBounceDescriptor(proxy)) ||
                !connection.BToA.ReceiverBounceProxies.Any(proxy =>
                    IsExpectedWallBounceDescriptor(
                        proxy,
                        "A2S_Receiver_Wall_Bounce",
                        AdministrativeToStartWallBounceIntensity)) ||
                connection.AToB.DoorSurfaceProxies.Any(proxy =>
                    proxy == null ||
                    proxy.Role != KExactProxyRole.DoorSurface ||
                    !Mathf.Approximately(proxy.IntensityAtPower0, 0f) ||
                    !IsExpectedBounceDescriptor(proxy)) ||
                connection.BToA.DoorSurfaceProxies.Any(proxy =>
                    proxy == null ||
                    proxy.Role != KExactProxyRole.DoorSurface ||
                    !Mathf.Approximately(proxy.IntensityAtPower0, 0f) ||
                    !IsExpectedBounceDescriptor(proxy)) ||
                reflection.Any(blend => blend == null ||
                    !Mathf.Approximately(
                        blend.IntensityAtWeightOne,
                        ReflectionProbeIntensityAtWeightOne)) ||
                connection.CasterRenderingLayerBit !=
                KExactBasisV1EditorContract.TransportCasterLayerBit ||
                connection.DoorReceiverRenderingLayerBit !=
                KExactBasisV1EditorContract.DoorReceiverLayerBit ||
                managers[0].Connections.Length != 1 ||
                managers[0].Connections[0] != connection)
            {
                throw new InvalidOperationException(
                    "Inactive runtime serialized configuration parity failed.");
            }
            if (!connection.TryValidateConfigurationOnly(out string configurationFailure))
                throw new InvalidOperationException(
                    "Inactive runtime configuration gate failed: " + configurationFailure);
            ValidateBasisTransformParity(
                scene.SelectedStartLights,
                connection.AToB.SelectedProductionLights,
                "Start K3");
            ValidateBasisTransformParity(
                scene.SelectedAdministrativeLights,
                connection.BToA.SelectedProductionLights,
                "Administrative K2");
        }

        private static Light[] CloneSelectedLights(
            Light[] sourceLights,
            Transform parent,
            string token)
        {
            var result = new Light[sourceLights.Length];
            for (int i = 0; i < sourceLights.Length; i++)
            {
                Light source = sourceLights[i];
                Vector3 sourceWorldPosition = source.transform.position;
                Quaternion sourceWorldRotation = source.transform.rotation;
                Vector3 sourceWorldScale = source.transform.lossyScale;
                UniversalAdditionalLightData sourceAdditional =
                    source.GetComponent<UniversalAdditionalLightData>();
                string sourceLightJson = EditorJsonUtility.ToJson(source, false);
                string sourceAdditionalJson = sourceAdditional != null
                    ? EditorJsonUtility.ToJson(sourceAdditional, false)
                    : string.Empty;

                if (!ApproximatelyVector3(parent.lossyScale, Vector3.one, 0.0001f))
                    throw new InvalidOperationException(
                        "Exact production Light clone parent must have unit world scale.");
                GameObject clone = Object.Instantiate(
                    source.gameObject,
                    sourceWorldPosition,
                    sourceWorldRotation,
                    parent);
                clone.name = "Basis_" + token + i.ToString("00", CultureInfo.InvariantCulture);
                clone.transform.localScale = sourceWorldScale;
                if (clone.transform.childCount != 0 ||
                    clone.GetComponentsInChildren<Renderer>(true).Length != 0)
                    throw new InvalidOperationException("Selected Light clone contains unexpected children/renderers.");
                Light[] clonedLights = clone.GetComponents<Light>();
                if (clonedLights.Length != 1)
                    throw new InvalidOperationException("Selected Light clone is not one exact Light.");
                Light cloned = clonedLights[0];
                UniversalAdditionalLightData clonedAdditional =
                    cloned.GetComponent<UniversalAdditionalLightData>();
                if (!string.Equals(sourceLightJson, EditorJsonUtility.ToJson(cloned, false),
                        StringComparison.Ordinal) ||
                    (sourceAdditional == null) != (clonedAdditional == null) ||
                    (sourceAdditional != null &&
                     !string.Equals(sourceAdditionalJson,
                         EditorJsonUtility.ToJson(clonedAdditional, false),
                         StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("Exact production Light clone parity failed.");
                }
                if (!WorldTransformMatches(source.transform, clone.transform))
                    throw new InvalidOperationException(
                        "Exact production Light clone world-transform parity failed for '" +
                        source.name + "'.");

                cloned.enabled = false;
                cloned.lightmapBakeType = LightmapBakeType.Realtime;
                cloned.bounceIntensity = 0f;
                result[i] = cloned;
            }
            return result;
        }

        private static void ValidateBasisTransformParity(
            Light[] productionLights,
            Light[] basisLights,
            string label)
        {
            if (productionLights == null || basisLights == null ||
                productionLights.Length != basisLights.Length || productionLights.Length == 0)
                throw new InvalidOperationException(label + " transform arrays are incomplete.");
            for (int i = 0; i < productionLights.Length; i++)
            {
                if (productionLights[i] == null || basisLights[i] == null ||
                    !WorldTransformMatches(
                        productionLights[i].transform,
                        basisLights[i].transform))
                {
                    throw new InvalidOperationException(
                        label + " world-transform parity failed at index " + i + ".");
                }
            }
        }

        private static bool WorldTransformMatches(Transform left, Transform right)
        {
            return left != null && right != null &&
                   Vector3.Distance(left.position, right.position) <= 0.0001f &&
                   Quaternion.Angle(left.rotation, right.rotation) <= 0.001f &&
                   ApproximatelyVector3(left.lossyScale, right.lossyScale, 0.0001f);
        }

        private static bool ApproximatelyVector3(
            Vector3 left,
            Vector3 right,
            float tolerance)
        {
            return Mathf.Abs(left.x - right.x) <= tolerance &&
                   Mathf.Abs(left.y - right.y) <= tolerance &&
                   Mathf.Abs(left.z - right.z) <= tolerance;
        }

        private static DungeonPortalDoorAngleSource CreateLiveDoorAngleSource(
            Transform parent,
            DungeonPortalDoorAngleSource canonicalSource,
            Transform doorLeaf)
        {
            if (parent == null || canonicalSource == null || doorLeaf == null ||
                !canonicalSource.IsConfigured || canonicalSource.DoorLeaf != doorLeaf)
            {
                throw new InvalidOperationException(
                    "Canonical door-angle source is unavailable for live binding.");
            }

            var serialized = new SerializedObject(canonicalSource);
            serialized.UpdateIfRequiredOrScript();
            SerializedProperty closed = serialized.FindProperty("closedLocalRotation");
            SerializedProperty axis = serialized.FindProperty("localHingeAxis");
            SerializedProperty angle = serialized.FindProperty("openAngleDegrees");
            if (closed == null || axis == null || angle == null ||
                axis.vector3Value.sqrMagnitude <= Mathf.Epsilon ||
                !float.IsFinite(angle.floatValue) ||
                Mathf.Abs(angle.floatValue) <= 0.001f)
            {
                throw new InvalidOperationException(
                    "Canonical door-angle configuration cannot be cloned safely.");
            }

            DungeonPortalDoorAngleSource live =
                CreateChild("DoorAngleSource_Live", parent)
                    .AddComponent<DungeonPortalDoorAngleSource>();
            live.Configure(
                doorLeaf,
                closed.quaternionValue,
                axis.vector3Value.normalized,
                angle.floatValue);
            if (!live.IsConfigured || live.DoorLeaf != doorLeaf)
                throw new InvalidOperationException("Live door-angle source configuration failed.");
            return live;
        }

        private static KExactBounceProxyDescriptor[]
            CreateStartToAdministrativeBounceDescriptors(Color color)
        {
            KExactBounceProxyDescriptor wall = CreateReceiverBounceDescriptor(
                "S2A_Receiver_Wall_Bounce",
                color,
                WallBounceLocalPosition,
                WallBounceLocalEulerAngles,
                StartToAdministrativeWallBounceIntensity,
                WallBounceRange,
                WallBounceSpotAngle,
                WallBounceInnerSpotAngle);
            return new[] { wall, CreateDoorSurfaceDescriptor("S2A", color) };
        }

        private static KExactBounceProxyDescriptor[]
            CreateAdministrativeToStartBounceDescriptors(Color color)
        {
            KExactBounceProxyDescriptor floor = CreateReceiverBounceDescriptor(
                "A2S_Receiver_Floor_Bounce",
                color,
                FloorBounceLocalPosition,
                FloorBounceLocalEulerAngles,
                AdministrativeToStartFloorBounceIntensity,
                FloorBounceRange,
                FloorBounceSpotAngle,
                FloorBounceInnerSpotAngle);
            KExactBounceProxyDescriptor wall = CreateReceiverBounceDescriptor(
                "A2S_Receiver_Wall_Bounce",
                color,
                WallBounceLocalPosition,
                WallBounceLocalEulerAngles,
                AdministrativeToStartWallBounceIntensity,
                WallBounceRange,
                WallBounceSpotAngle,
                WallBounceInnerSpotAngle);
            return new[] { floor, wall, CreateDoorSurfaceDescriptor("A2S", color) };
        }

        private static KExactBounceProxyDescriptor CreateReceiverBounceDescriptor(
            string key,
            Color color,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            float intensity,
            float range,
            float spotAngle,
            float innerSpotAngle)
        {
            var descriptor = new KExactBounceProxyDescriptor();
            descriptor.Configure(
                key,
                LightType.Spot,
                localPosition,
                localEulerAngles,
                color,
                0f,
                intensity,
                range,
                spotAngle,
                innerSpotAngle,
                LightShadows.Soft,
                0.7f,
                ~0,
                KExactProxyRole.ReceiverBounce);
            return descriptor;
        }

        private static KExactBounceProxyDescriptor CreateDoorSurfaceDescriptor(
            string prefix,
            Color color)
        {
            var door = new KExactBounceProxyDescriptor();
            door.Configure(
                prefix + "_Door_Surface",
                LightType.Spot,
                new Vector3(0f, 1.15f, -0.65f),
                Vector3.zero,
                color,
                0f,
                DoorSurfaceIntensity,
                DoorSurfaceRange,
                DoorSurfaceSpotAngle,
                DoorSurfaceInnerSpotAngle,
                LightShadows.Soft,
                0.6f,
                ~0,
                KExactProxyRole.DoorSurface);
            return door;
        }

        private static bool IsExpectedBounceDescriptor(
            KExactBounceProxyDescriptor descriptor)
        {
            if (descriptor.Type != LightType.Spot)
                return false;

            bool wall =
                descriptor.Role == KExactProxyRole.ReceiverBounce &&
                descriptor.Key.EndsWith("_Receiver_Wall_Bounce",
                    StringComparison.Ordinal) &&
                Vector3.Distance(descriptor.LocalPosition,
                    WallBounceLocalPosition) <= 0.0001f &&
                Quaternion.Angle(descriptor.LocalRotation,
                    Quaternion.Euler(WallBounceLocalEulerAngles)) <= 0.001f &&
                (Mathf.Approximately(descriptor.IntensityAtPower100,
                     StartToAdministrativeWallBounceIntensity) ||
                 Mathf.Approximately(descriptor.IntensityAtPower100,
                     AdministrativeToStartWallBounceIntensity)) &&
                Mathf.Approximately(descriptor.Range, WallBounceRange) &&
                Mathf.Approximately(descriptor.SpotAngle, WallBounceSpotAngle) &&
                Mathf.Approximately(descriptor.InnerSpotAngle,
                    WallBounceInnerSpotAngle);
            bool floor =
                descriptor.Role == KExactProxyRole.ReceiverBounce &&
                descriptor.Key == "A2S_Receiver_Floor_Bounce" &&
                Vector3.Distance(descriptor.LocalPosition,
                    FloorBounceLocalPosition) <= 0.0001f &&
                Quaternion.Angle(descriptor.LocalRotation,
                    Quaternion.Euler(FloorBounceLocalEulerAngles)) <= 0.001f &&
                Mathf.Approximately(descriptor.IntensityAtPower100,
                    AdministrativeToStartFloorBounceIntensity) &&
                Mathf.Approximately(descriptor.Range, FloorBounceRange) &&
                Mathf.Approximately(descriptor.SpotAngle, FloorBounceSpotAngle) &&
                Mathf.Approximately(descriptor.InnerSpotAngle,
                    FloorBounceInnerSpotAngle);
            bool door =
                descriptor.Role == KExactProxyRole.DoorSurface &&
                Quaternion.Angle(descriptor.LocalRotation, Quaternion.identity) <= 0.001f &&
                Mathf.Approximately(
                    descriptor.IntensityAtPower100,
                    DoorSurfaceIntensity) &&
                Mathf.Approximately(descriptor.Range, DoorSurfaceRange) &&
                Mathf.Approximately(descriptor.SpotAngle, DoorSurfaceSpotAngle) &&
                Mathf.Approximately(
                    descriptor.InnerSpotAngle,
                    DoorSurfaceInnerSpotAngle);
            return wall || floor || door;
        }

        private static bool IsExpectedWallBounceDescriptor(
            KExactBounceProxyDescriptor descriptor,
            string key,
            float intensity)
        {
            return descriptor != null &&
                   descriptor.Key == key &&
                   descriptor.Role == KExactProxyRole.ReceiverBounce &&
                   Mathf.Approximately(descriptor.IntensityAtPower0, 0f) &&
                   Mathf.Approximately(descriptor.IntensityAtPower100, intensity) &&
                   IsExpectedBounceDescriptor(descriptor);
        }

        private static bool IsExpectedFloorBounceDescriptor(
            KExactBounceProxyDescriptor descriptor)
        {
            return descriptor != null &&
                   descriptor.Key == "A2S_Receiver_Floor_Bounce" &&
                   descriptor.Role == KExactProxyRole.ReceiverBounce &&
                   Mathf.Approximately(descriptor.IntensityAtPower0, 0f) &&
                   Mathf.Approximately(
                       descriptor.IntensityAtPower100,
                       AdministrativeToStartFloorBounceIntensity) &&
                   IsExpectedBounceDescriptor(descriptor);
        }

        private static Color AverageColor(Light[] lights)
        {
            if (lights == null || lights.Length == 0)
                throw new InvalidOperationException("Cannot derive bounce color without source Lights.");
            Color total = Color.black;
            float weight = 0f;
            for (int i = 0; i < lights.Length; i++)
            {
                float current = Mathf.Max(0.0001f, lights[i].intensity);
                total += lights[i].color * current;
                weight += current;
            }
            Color result = total / weight;
            result.a = 1f;
            return result;
        }

        private static CubemapPair ResolveNearestReflectionPair(
            DungeonTilePowerBakeSet set,
            Transform room,
            Vector3 portalPosition,
            string label)
        {
            DungeonTileBakeData.ReflectionProbeBakeEntry[] p0 =
                set.Power00Bake.reflectionProbeEntries ??
                Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>();
            DungeonTileBakeData.ReflectionProbeBakeEntry[] p100 =
                set.Power100Bake.reflectionProbeEntries ??
                Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>();
            if (p0.Length == 0 || p0.Length != p100.Length)
                throw new InvalidOperationException(label + " reflection bake entries changed.");

            Dictionary<string, List<ReflectionProbe>> buckets =
                BuildReflectionProbeBuckets(room);
            var useCount = new Dictionary<string, int>(StringComparer.Ordinal);
            int best = -1;
            ReflectionProbe bestProbe = null;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < p100.Length; i++)
            {
                if (!string.Equals(p0[i].relativePath, p100[i].relativePath,
                        StringComparison.Ordinal) ||
                    p0[i].bakedTexture == null || p100[i].bakedTexture == null ||
                    !buckets.TryGetValue(p100[i].relativePath, out List<ReflectionProbe> probes))
                    continue;
                useCount.TryGetValue(p100[i].relativePath, out int bucketIndex);
                useCount[p100[i].relativePath] = bucketIndex + 1;
                if (bucketIndex < 0 || bucketIndex >= probes.Count)
                    continue;
                ReflectionProbe candidate = probes[bucketIndex];
                if (candidate == null || candidate.enabled)
                    continue;
                float distance = candidate.bounds.SqrDistance(portalPosition);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                    bestProbe = candidate;
                }
            }
            if (best < 0 || bestProbe == null)
                throw new InvalidOperationException(
                    label + " nearest disabled production reflection pair is missing.");
            return new CubemapPair(
                p0[best].bakedTexture,
                p100[best].bakedTexture,
                p100[best].relativePath,
                bestProbe);
        }

        private static Dictionary<string, List<ReflectionProbe>> BuildReflectionProbeBuckets(
            Transform room)
        {
            var result = new Dictionary<string, List<ReflectionProbe>>(StringComparer.Ordinal);
            ReflectionProbe[] probes = room.GetComponentsInChildren<ReflectionProbe>(true);
            for (int i = 0; i < probes.Length; i++)
            {
                string path = KExactBasisV1EditorContract.GetHumanRelativePath(
                    room, probes[i].transform);
                if (!result.TryGetValue(path, out List<ReflectionProbe> bucket))
                {
                    bucket = new List<ReflectionProbe>();
                    result.Add(path, bucket);
                }
                bucket.Add(probes[i]);
            }
            return result;
        }

        private static KExactReflectionProbeBlend CreateReflectionBasis(
            Transform parent,
            string name,
            KExactReflectionProbeBlend.TransportDirection direction,
            CubemapPair sourceCubemaps,
            ReflectionProbe receiverDescriptor)
        {
            if (sourceCubemaps.Power0 == null || sourceCubemaps.Power100 == null ||
                receiverDescriptor == null || receiverDescriptor.enabled)
            {
                throw new InvalidOperationException(
                    "Reflection basis requires a source cubemap pair and a disabled " +
                    "receiver-local production probe descriptor.");
            }
            GameObject gameObject = CreateChild(name, parent);
            gameObject.transform.SetPositionAndRotation(
                receiverDescriptor.transform.position,
                receiverDescriptor.transform.rotation);
            gameObject.transform.localScale = receiverDescriptor.transform.lossyScale;
            ReflectionProbe probe = gameObject.AddComponent<ReflectionProbe>();
            probe.enabled = false;
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = null;
            probe.boxProjection = receiverDescriptor.boxProjection;
            probe.center = receiverDescriptor.center;
            probe.size = receiverDescriptor.size;
            probe.blendDistance = receiverDescriptor.blendDistance;
            probe.importance = receiverDescriptor.importance;
            probe.cullingMask = receiverDescriptor.cullingMask;
            probe.clearFlags = receiverDescriptor.clearFlags;
            probe.backgroundColor = receiverDescriptor.backgroundColor;
            probe.hdr = receiverDescriptor.hdr;
            probe.shadowDistance = receiverDescriptor.shadowDistance;
            probe.nearClipPlane = receiverDescriptor.nearClipPlane;
            probe.farClipPlane = receiverDescriptor.farClipPlane;
            probe.resolution = receiverDescriptor.resolution;
            probe.intensity = 0f;
            if (Vector3.Distance(probe.bounds.center, receiverDescriptor.bounds.center) >
                    0.0001f ||
                Vector3.Distance(probe.bounds.size, receiverDescriptor.bounds.size) >
                    0.0001f)
            {
                throw new InvalidOperationException(
                    "Owned reflection basis does not exactly match the receiver-local " +
                    "production probe bounds.");
            }
            KExactReflectionProbeBlend blend =
                gameObject.AddComponent<KExactReflectionProbeBlend>();
            blend.Configure(
                name,
                direction,
                sourceCubemaps.Power0,
                sourceCubemaps.Power100,
                receiverDescriptor.resolution,
                ReflectionProbeIntensityAtWeightOne);
            return blend;
        }

        private static GameObject CreateChild(string name, Transform parent)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void RestoreEditorIdentity(
            Scene originalActive,
            KExactBasisV1EditorContract.SelectionSnapshot selection,
            bool originalPlaying)
        {
            if (originalActive.IsValid() && originalActive.isLoaded &&
                SceneManager.GetActiveScene() != originalActive)
                EditorSceneManager.SetActiveScene(originalActive);
            selection.Restore();
            if (EditorApplication.isPlaying != originalPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode != originalPlaying)
                throw new InvalidOperationException("Builder changed Play Mode state.");
        }

        private static void WriteBuildManifest(
            string builtSceneSha,
            string protectedFingerprint,
            KExactBasisV1EditorContract.RendererParityReport parity,
            RuntimeBuildReport runtime)
        {
            var builder = new StringBuilder();
            builder.AppendLine("schema=KExactBasisV1SceneBuild/v1");
            builder.AppendLine("status=" + Status);
            builder.AppendLine("buildOutcome=COMPLETE");
            builder.AppendLine("visualVerdict=UNREVIEWED");
            builder.AppendLine("visualParityClaimed=false");
            builder.AppendLine("productionAssetsWritten=false");
            builder.AppendLine("sourceScenePath=" +
                               KExactBasisV1EditorContract.SourceScenePath);
            builder.AppendLine("sourceSceneSha256=" +
                               KExactBasisV1EditorContract.ExpectedSourceSceneSha256);
            builder.AppendLine("builtScenePath=" +
                               KExactBasisV1EditorContract.BuiltScenePath);
            builder.AppendLine("builtSceneSha256=" + builtSceneSha);
            builder.AppendLine("referenceManifestPath=" +
                               KExactBasisV1EditorContract.CorrectedRealtimeManifestPath);
            builder.AppendLine("referenceManifestSha256=" +
                               KExactBasisV1EditorContract.ExpectedCorrectedRealtimeManifestSha256);
            builder.AppendLine("kexactBasisManifestPath=" +
                               KExactBasisV1EditorContract.KExactSelectionManifestPath);
            builder.AppendLine("kexactBasisManifestSha256=" +
                               KExactBasisV1EditorContract.ExpectedKExactSelectionManifestSha256);
            builder.AppendLine("selectionFingerprintSha256=" +
                               KExactBasisV1EditorContract.ExpectedSelectionFingerprintSha256);
            builder.AppendLine("strictSelectedStartK=" + runtime.StartK);
            builder.AppendLine("strictSelectedAdministrativeK=" + runtime.AdministrativeK);
            builder.AppendLine("powerScalarBinding=DungeonTileLightmapSwitcher.CurrentPowerLevel");
            builder.AppendLine("doorScalarBinding=DungeonPortalDoorAngleSource.ApertureFraction");
            builder.AppendLine(
                "doorApertureDefinition=projected aperture; 90deg values " +
                "0,0.07612047,0.2928932,0.6173166,1 at angle fractions " +
                "0,0.25,0.5,0.75,1");
            builder.AppendLine(
                "directTransportAperturePolicy=source power multiplied by actual " +
                "projected aperture fraction");
            builder.AppendLine("receiverBounceAperturePolicy=source power times aperture");
            builder.AppendLine("reflectionAperturePolicy=endpoint weight times aperture");
            builder.AppendLine("doorSurfaceAperturePolicy=source power only");
            builder.AppendLine("liveDoorAngleSourceOwnedByKExact=true");
            builder.AppendLine("captureOverrideNonSerialized=true");
            builder.AppendLine("bounceProxyCount=" + runtime.BounceProxyCount);
            builder.AppendLine("receiverBounceProxyCount=3");
            builder.AppendLine("doorSurfaceProxyCount=2");
            builder.AppendLine("startToAdministrativeWallBounceIntensityAtP100=" +
                               StartToAdministrativeWallBounceIntensity.ToString(
                                   "R", CultureInfo.InvariantCulture));
            builder.AppendLine("administrativeToStartFloorBounceIntensityAtP100=" +
                               AdministrativeToStartFloorBounceIntensity.ToString(
                                   "R", CultureInfo.InvariantCulture));
            builder.AppendLine("administrativeToStartWallBounceIntensityAtP100=" +
                               AdministrativeToStartWallBounceIntensity.ToString(
                                   "R", CultureInfo.InvariantCulture));
            builder.AppendLine(
                "basisTransformParityContract=source world position<=0.0001;" +
                "rotationDegrees<=0.001;lossyScale<=0.0001;validated after publish");
            builder.AppendLine("basisTransformParityValidated=true");
            builder.AppendLine(
                "r8Correction=R7 linear-HDR unit-response analysis reallocated, rather " +
                "than added, receiver-bounce energy: Start-to-Administrative uses the " +
                "wall-only RMSE fit and Administrative-to-Start splits the R6 floor " +
                "geometry with a low wall lobe; exact direct, reflection, door-surface, " +
                "and production surfaces remain unchanged");
            builder.AppendLine("receiverBounceType=Spot");
            builder.AppendLine("wallBounceLocalPosition=" + WallBounceLocalPosition);
            builder.AppendLine("wallBounceLocalEulerAngles=" +
                               WallBounceLocalEulerAngles);
            builder.AppendLine("wallBounceRange=" +
                               WallBounceRange.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("wallBounceSpotAngle=" +
                               WallBounceSpotAngle.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("wallBounceInnerSpotAngle=" +
                               WallBounceInnerSpotAngle.ToString(
                                   "R", CultureInfo.InvariantCulture));
            builder.AppendLine("floorBounceLocalPosition=" + FloorBounceLocalPosition);
            builder.AppendLine("floorBounceLocalEulerAngles=" +
                               FloorBounceLocalEulerAngles);
            builder.AppendLine("floorBounceRange=" +
                               FloorBounceRange.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("floorBounceSpotAngle=" +
                               FloorBounceSpotAngle.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("floorBounceInnerSpotAngle=" +
                               FloorBounceInnerSpotAngle.ToString(
                                   "R", CultureInfo.InvariantCulture));
            builder.AppendLine("doorSurfaceIntensityAtP100=" +
                               DoorSurfaceIntensity.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("doorSurfaceRange=" +
                               DoorSurfaceRange.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("doorSurfaceType=Spot");
            builder.AppendLine("doorSurfaceLocalEulerAngles=(0,0,0)");
            builder.AppendLine("doorSurfaceSpotAngle=" +
                               DoorSurfaceSpotAngle.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("doorSurfaceInnerSpotAngle=" +
                               DoorSurfaceInnerSpotAngle.ToString(
                                   "R", CultureInfo.InvariantCulture));
            builder.AppendLine("doorReceiverRenderingLayerBit=0x" +
                               KExactBasisV1EditorContract.DoorReceiverLayerBit.ToString("X8"));
            builder.AppendLine("maxAdditionalLightsPerRoomRenderer=4");
            builder.AppendLine("maxAdditionalLightsPerDoorRenderer=2");
            builder.AppendLine("reflectionBasisCount=" + runtime.ReflectionBasisCount);
            builder.AppendLine("p0ResidualReflectionWeight=" +
                               P0ResidualReflection.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("p100ReflectionWeight=" +
                               P100Reflection.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("reflectionProbeIntensityAtWeightOne=" +
                               ReflectionProbeIntensityAtWeightOne.ToString(
                                   "R", CultureInfo.InvariantCulture));
            builder.AppendLine("rendererCount=" + parity.RendererCount);
            builder.AppendLine("additionalRendererCount=" + parity.AdditionalRendererCount);
            builder.AppendLine("rendererParityFingerprintSha256=" +
                               parity.FingerprintSha256);
            builder.AppendLine("rendererParityContract=mesh;materialArray;shader;keywords;" +
                               "renderQueue;lightmapIndexST;reflectionSettings;" +
                               "MaterialPropertyBlockFingerprint;additionalRendererCount");
            builder.AppendLine("oldPocRootDisabledInCloneOnly=true");
            builder.AppendLine("p0ResidualReflection=true");
            builder.AppendLine("pairOrCrossTermUsed=false");
            builder.AppendLine("reflectionProbeDescriptorContract=source-room P0/P100 cubemaps; " +
                               "nearest disabled receiver-room production probe " +
                               "center/size/blendDistance/boxProjection/importance and capture " +
                               "settings cloned onto owned receiver-side probe");
            builder.AppendLine("protectedProductionFingerprintSha256=" +
                               protectedFingerprint);
            builder.AppendLine("startReflectionP0=" +
                               KExactBasisV1EditorContract.GetObjectIdentity(
                                   runtime.StartReflection.Power0));
            builder.AppendLine("startReflectionP100=" +
                               KExactBasisV1EditorContract.GetObjectIdentity(
                                   runtime.StartReflection.Power100));
            builder.AppendLine("administrativeReflectionP0=" +
                               KExactBasisV1EditorContract.GetObjectIdentity(
                                   runtime.AdministrativeReflection.Power0));
            builder.AppendLine("administrativeReflectionP100=" +
                               KExactBasisV1EditorContract.GetObjectIdentity(
                                   runtime.AdministrativeReflection.Power100));

            string absolute = KExactBasisV1EditorContract.AssetPathToAbsolutePath(
                KExactBasisV1EditorContract.BuildManifestPath);
            File.WriteAllText(absolute, builder.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                KExactBasisV1EditorContract.BuildManifestPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            if (!File.Exists(absolute))
                throw new IOException("Build manifest write/import failed.");
        }

        private static void TryWriteBuildFailureMarker(
            string token,
            string stagingScenePath,
            bool published,
            Exception exception)
        {
            try
            {
                string path = KExactBasisV1EditorContract.DataFolder +
                              "/FAILED_BUILD_" + token + ".txt";
                string text = "status=FAILED\n" +
                              "published=" + published + "\n" +
                              "stagingScene=" + stagingScenePath + "\n" +
                              "reason=" + KExactBasisV1EditorContract.Sanitize(
                                  exception.ToString()) + "\n";
                File.WriteAllText(
                    KExactBasisV1EditorContract.AssetPathToAbsolutePath(path),
                    text,
                    new UTF8Encoding(false));
                AssetDatabase.ImportAsset(
                    path,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
            }
            catch
            {
                // Preserve the original failure and any staging scene for diagnosis.
            }
        }

        private sealed class RuntimeBuildReport
        {
            internal readonly int StartK;
            internal readonly int AdministrativeK;
            internal readonly int BounceProxyCount;
            internal readonly int ReflectionBasisCount;
            internal readonly CubemapPair StartReflection;
            internal readonly CubemapPair AdministrativeReflection;

            internal RuntimeBuildReport(
                int startK,
                int administrativeK,
                int bounceProxyCount,
                int reflectionBasisCount,
                CubemapPair startReflection,
                CubemapPair administrativeReflection)
            {
                StartK = startK;
                AdministrativeK = administrativeK;
                BounceProxyCount = bounceProxyCount;
                ReflectionBasisCount = reflectionBasisCount;
                StartReflection = startReflection;
                AdministrativeReflection = administrativeReflection;
            }
        }

        private readonly struct CubemapPair
        {
            internal readonly Cubemap Power0;
            internal readonly Cubemap Power100;
            internal readonly string RelativePath;
            internal readonly ReflectionProbe SourceProbe;

            internal CubemapPair(
                Cubemap power0,
                Cubemap power100,
                string relativePath,
                ReflectionProbe sourceProbe)
            {
                Power0 = power0;
                Power100 = power100;
                RelativePath = relativePath;
                SourceProbe = sourceProbe;
            }
        }
    }

    /// <summary>Read-only verifier for the published isolated scene.</summary>
    public static class KExactBasisV1SceneValidator
    {
        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Transport PoC/KExactBasisV1/" +
            "Validate Isolated Scene (Read Only)")]
        public static void ValidateFromMenu()
        {
            string result = Validate();
            if (result.StartsWith("PASS", StringComparison.Ordinal))
                Debug.Log(result);
            else
                Debug.LogError(result);
        }

        public static void ValidateCli()
        {
            string result = Validate();
            if (!result.StartsWith("PASS", StringComparison.Ordinal))
                throw new InvalidOperationException(result);
            Debug.Log(result);
        }

        public static string Validate()
        {
            try
            {
                KExactBasisV1EditorContract.RequireStableEditMode();
                KExactBasisV1EditorContract.ValidatePinnedInputs();
                if (KExactBasisV1EditorContract.CountLoadedNonPreviewScenes() != 1 ||
                    SceneManager.sceneCount != 1)
                    throw new InvalidOperationException("Exactly one clean scene is required.");
                Scene active = SceneManager.GetActiveScene();
                if (!active.IsValid() || !active.isLoaded || active.isDirty)
                    throw new InvalidOperationException("Active scene is invalid or dirty.");
                string activePath = KExactBasisV1EditorContract.NormalizePath(active.path);
                bool sourceIsActive = string.Equals(
                    activePath,
                    KExactBasisV1EditorContract.SourceScenePath,
                    StringComparison.Ordinal);
                bool candidateIsActive = string.Equals(
                    activePath,
                    KExactBasisV1EditorContract.BuiltScenePath,
                    StringComparison.Ordinal);
                if (!sourceIsActive && !candidateIsActive)
                    throw new InvalidOperationException(
                        "Open either the pinned source scene or built KExactBasisV1 scene.");

                var selection = KExactBasisV1EditorContract.SelectionSnapshot.Capture();
                var protectedAssets =
                    KExactBasisV1EditorContract.ProtectedAssetSnapshot.Capture();
                Scene added = default;
                try
                {
                    string addedPath = sourceIsActive
                        ? KExactBasisV1EditorContract.BuiltScenePath
                        : KExactBasisV1EditorContract.SourceScenePath;
                    if (!File.Exists(KExactBasisV1EditorContract.AssetPathToAbsolutePath(addedPath)))
                        throw new FileNotFoundException("Validation counterpart scene is missing.");
                    added = EditorSceneManager.OpenScene(addedPath, OpenSceneMode.Additive);
                    Scene sourceScene = sourceIsActive ? active : added;
                    Scene candidateScene = sourceIsActive ? added : active;
                    var source = KExactBasisV1EditorContract.ResolveSceneBindings(
                        sourceScene, true, false);
                    var candidate = KExactBasisV1EditorContract.ResolveSceneBindings(
                        candidateScene, false, true);
                    KExactBasisV1SceneBuilder.ValidateRuntimeScene(candidate);
                    KExactBasisV1EditorContract.RendererParityReport parity =
                        KExactBasisV1EditorContract.AssertRendererParity(source, candidate);
                    if (sourceScene.isDirty || candidateScene.isDirty)
                        throw new InvalidOperationException("Read-only validation dirtied a scene.");
                    return "PASS KEXACT_BASIS_V1_SCENE_VALIDATION\n" +
                           "rendererCount=" + parity.RendererCount + "\n" +
                           "additionalRendererCount=" + parity.AdditionalRendererCount + "\n" +
                           "rendererParityFingerprint=" + parity.FingerprintSha256 + "\n" +
                           "sourceSceneSha256=" +
                           KExactBasisV1EditorContract.ComputeFileSha256(
                               KExactBasisV1EditorContract.SourceScenePath) + "\n" +
                           "builtSceneSha256=" +
                           KExactBasisV1EditorContract.ComputeFileSha256(
                               KExactBasisV1EditorContract.BuiltScenePath) + "\n" +
                           "productionAssetsWritten=0";
                }
                finally
                {
                    if (added.IsValid() && added.isLoaded)
                        EditorSceneManager.CloseScene(added, true);
                    if (active.IsValid() && active.isLoaded &&
                        SceneManager.GetActiveScene() != active)
                        EditorSceneManager.SetActiveScene(active);
                    selection.Restore();
                    selection.AssertRestored();
                    protectedAssets.AssertUnchanged();
                    if (active.isDirty)
                        throw new InvalidOperationException("Active scene became dirty.");
                }
            }
            catch (Exception exception)
            {
                return "FAIL KEXACT_BASIS_V1_SCENE_VALIDATION: " + exception;
            }
        }
    }
}
