using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DungeonRoomLocalLightShare.Editor
{
    /// <summary>
    /// One-room incoming bounce capture. Never pair-bakes two rooms.
    /// Procedure adapted from DungeonPortalTransportPoC CanonicalBounceBakePlan
    /// with the V2 IgnoreLightControl contract (Start=4, Admin=2).
    /// </summary>
    public static class RoomLocalBounceBaker
    {
        private const string InjectorName = "__RLLS_UnitPortalInjector";

        public static string BakeStartReceiver()
        {
            return BakeReceiver(
                RoomLocalLightShareContract.StartRoomId,
                RoomLocalLightShareContract.StartPrefabPath,
                RoomLocalLightShareContract.StartBouncePath);
        }

        public static string BakeAdministrativeReceiver()
        {
            return BakeReceiver(
                RoomLocalLightShareContract.AdministrativeRoomId,
                RoomLocalLightShareContract.AdministrativePrefabPath,
                RoomLocalLightShareContract.AdministrativeBouncePath);
        }

        public static string BakeBothReceivers()
        {
            string start = BakeStartReceiver();
            if (start.StartsWith("FAIL", StringComparison.Ordinal))
                return start;
            string admin = BakeAdministrativeReceiver();
            if (admin.StartsWith("FAIL", StringComparison.Ordinal))
                return admin;
            return "PASS bounce bake both receivers\n" + start + "\n" + admin;
        }

        private static string BakeReceiver(string roomId, string prefabPath, string bounceAssetPath)
        {
            string idle = RoomLocalEditorUtil.RequireIdleEditor();
            if (idle != null)
                return idle;

            string hash = RoomLocalHashGuard.CaptureOrVerify();
            if (hash.StartsWith("FAIL", StringComparison.Ordinal))
                return hash;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                RoomLocalLightShareContract.DoorPrefabPath);
            if (prefab == null || doorPrefab == null)
                return "FAIL: missing room or door prefab for " + roomId;

            UnityEditor.SceneManagement.SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            Scene workspace = default;
            try
            {
                workspace = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssignLightingSettings(roomId);

                GameObject room = (GameObject)PrefabUtility.InstantiatePrefab(prefab, workspace);
                room.name = roomId;
                room.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Transform doorway = room.transform.Find(RoomLocalLightShareContract.PreferredDoorwayPath);
                if (doorway == null)
                    return "FAIL: doorway missing on " + roomId;

                RoomLocalSceneBuilder.OpenPassage(doorway);
                Doorway doorwayComponent = doorway.GetComponent<Doorway>();
                GameObject door = (GameObject)PrefabUtility.InstantiatePrefab(doorPrefab, workspace);
                door.transform.SetParent(doorway, false);
                if (doorwayComponent != null)
                {
                    door.transform.localPosition = doorwayComponent.DoorPrefabPositionOffset;
                    door.transform.localRotation =
                        Quaternion.Euler(doorwayComponent.DoorPrefabRotationOffset);
                }

                Transform leaf = door.transform.Find(RoomLocalLightShareContract.DoorLeafPath);
                if (leaf == null)
                    return "FAIL: door leaf missing.";

                Quaternion closed = leaf.localRotation;
                var switcher = room.GetComponent<DungeonTileLightmapSwitcher>();
                if (switcher == null)
                    return "FAIL: DungeonTileLightmapSwitcher missing on " + roomId;
                PrepareSwitcherForEditMode(switcher);
                switcher.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P0);
                ApplyIgnoreLightControlContract(room, roomId, door.transform);

                Light injector = CreateUnitInjector(doorway);
                string roomFolder = Path.GetDirectoryName(bounceAssetPath)?.Replace('\\', '/');
                RoomLocalEditorUtil.EnsureFolder(roomFolder);

                // Unity's lightmapper requires the active scene to have a stable asset path.
                // This workspace is created with NewScene, so save an isolated generated scene
                // before the first capture instead of letting Lightmapping.Bake fail on an
                // untitled scene. The canonical room and door prefabs remain untouched.
                string bakeScenePath = roomFolder + "/" + roomId + "_BounceBakeTemp.unity";
                if (!EditorSceneManager.SaveScene(workspace, bakeScenePath))
                    throw new IOException("Could not save bounce bake workspace: " + bakeScenePath);

                var poses = new List<IncomingBounceData.PoseCapture>();
                float[] fractions = RoomLocalLightShareContract.AuthoredOpenFractions;
                for (int i = 0; i < fractions.Length; i++)
                {
                    float open = fractions[i];
                    string poseId = open >= 0.99f ? "D100" : "D050";
                    leaf.localRotation = RoomLocalLightShareMath.DoorLocalRotationForOpenFraction(
                        closed,
                        Vector3.up,
                        90f,
                        open);

                    CaptureState(injector, true, 0f);
                    Texture2D[] directColor = CopyCurrentLightmaps(roomFolder, poseId, "DirectOnly", true);
                    Texture2D[] directDir = CopyCurrentLightmaps(roomFolder, poseId, "DirectOnly", false);

                    CaptureState(injector, true, 1f);
                    Texture2D[] fullColor = CopyCurrentLightmaps(roomFolder, poseId, "Full", true);
                    Texture2D[] fullDir = CopyCurrentLightmaps(roomFolder, poseId, "Full", false);

                    if (directColor.Length != fullColor.Length ||
                        directColor.Length != directDir.Length ||
                        directColor.Length != fullDir.Length)
                    {
                        return "FAIL: atlas layout drifted across bounce captures for " + poseId;
                    }

                    poses.Add(new IncomingBounceData.PoseCapture
                    {
                        poseId = poseId,
                        openFraction = open,
                        fullColor = fullColor,
                        fullDirection = fullDir,
                        directOnlyColor = directColor,
                        directOnlyDirection = directDir
                    });
                }

                IncomingBounceData.RendererBinding[] bindings = BuildBindings(room.transform);
                IncomingBounceData data = AssetDatabase.LoadAssetAtPath<IncomingBounceData>(bounceAssetPath);
                if (data == null)
                {
                    data = ScriptableObject.CreateInstance<IncomingBounceData>();
                    AssetDatabase.CreateAsset(data, bounceAssetPath);
                }

                data.ConfigureAuthoring(
                    roomId,
                    RoomLocalLightShareContract.PreferredDoorwayPath,
                    bindings,
                    poses.ToArray());
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
                return "PASS bounce bake " + roomId + " poses=" + poses.Count +
                       " bindings=" + bindings.Length + " asset=" + bounceAssetPath;
            }
            catch (Exception exception)
            {
                return "FAIL bounce bake " + roomId + ": " + exception;
            }
            finally
            {
                if (setup != null && setup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        private static void PrepareSwitcherForEditMode(DungeonTileLightmapSwitcher switcher)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(DungeonTileLightmapSwitcher);
            type.GetMethod("CacheRenderers", flags)?.Invoke(switcher, null);
            type.GetMethod("CacheReflectionProbes", flags)?.Invoke(switcher, null);
            type.GetMethod("DisableTileLights", flags)?.Invoke(switcher, null);
        }

        private static void AssignLightingSettings(string roomId)
        {
            LightingSettings source = AssetDatabase.LoadAssetAtPath<LightingSettings>(
                RoomLocalLightShareContract.OfficialLightingSettingsPath);
            if (source == null)
                throw new InvalidOperationException(
                    "Missing " + RoomLocalLightShareContract.OfficialLightingSettingsPath);

            string folder = RoomLocalLightShareContract.BounceFolder + "/LightingSettings";
            RoomLocalEditorUtil.EnsureFolder(folder);
            string clonePath = folder + "/" + roomId + "_Bounce.lighting";
            LightingSettings existing = AssetDatabase.LoadAssetAtPath<LightingSettings>(clonePath);
            if (existing != null)
                AssetDatabase.DeleteAsset(clonePath);
            if (!AssetDatabase.CopyAsset(
                    RoomLocalLightShareContract.OfficialLightingSettingsPath, clonePath))
                throw new IOException("Could not clone lighting settings.");
            AssetDatabase.ImportAsset(clonePath);
            Lightmapping.lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(clonePath);
        }

        private static void ApplyIgnoreLightControlContract(
            GameObject room,
            string roomId,
            Transform excludedDoor)
        {
            int enabledIgnored = 0;
            Light[] lights = room.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null || (excludedDoor != null && light.transform.IsChildOf(excludedDoor)))
                    continue;
                IgnoreLightControl marker = light.GetComponentInParent<IgnoreLightControl>(true);
                if (marker != null && marker.enabled)
                {
                    // PrepareSwitcherForEditMode intentionally disables every runtime tile
                    // light before a bake. The marked fixtures are the authored P0 exceptions,
                    // so restore them here before validating the room contract. They are present
                    // in both DirectOnly and Full captures and therefore cancel out of the
                    // Full - DirectOnly response.
                    if (!light.enabled)
                    {
                        light.enabled = true;
                        EditorUtility.SetDirty(light);
                    }
                    enabledIgnored++;
                    continue;
                }

                if (light.enabled)
                {
                    light.enabled = false;
                    EditorUtility.SetDirty(light);
                }
            }

            int expected = RoomLocalLightShareContract.ExpectedIgnoreLightControlCount(roomId);
            if (expected >= 0 && enabledIgnored != expected)
            {
                throw new InvalidOperationException(
                    "P0 IgnoreLightControl contract drifted for " + roomId +
                    ". expected enabled exceptions=" + expected + " actual=" + enabledIgnored + ".");
            }
        }

        private static Light CreateUnitInjector(Transform doorway)
        {
            Vector2 socket = new Vector2(
                RoomLocalDoorwayFrame.DefaultSocketWidth,
                RoomLocalDoorwayFrame.DefaultSocketHeight);
            var component = doorway.GetComponent<Doorway>();
            if (component != null && component.Socket != null)
                socket = component.Socket.Size;
            RoomLocalDoorwayFrame.Frame frame = RoomLocalDoorwayFrame.FromDoorway(doorway, socket);

            var go = new GameObject(InjectorName);
            go.transform.SetPositionAndRotation(
                RoomLocalDoorwayFrame.SpotPosition(frame),
                RoomLocalDoorwayFrame.SpotRotation(frame));
            Light light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = RoomLocalDoorwayFrame.FittedSpotAngle(frame);
            light.innerSpotAngle = Mathf.Max(1f, light.spotAngle * 0.65f);
            light.range = RoomLocalDoorwayFrame.DirectRange;
            light.color = Color.white;
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;
            light.cullingMask = RoomLocalLightShareContract.DungeonCullingMask;
            light.renderingLayerMask = RoomLocalLightShareContract.DungeonRenderingLayerMask;
            light.lightmapBakeType = LightmapBakeType.Baked;
            UniversalAdditionalLightData data = light.GetUniversalAdditionalLightData();
            data.renderingLayers = (uint)RoomLocalLightShareContract.DungeonRenderingLayerMask;
            data.shadowRenderingLayers = (uint)RoomLocalLightShareContract.DungeonRenderingLayerMask;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
            return light;
        }

        private static void CaptureState(
            Light injector,
            bool injectorEnabled,
            float bounceIntensity)
        {
            injector.enabled = injectorEnabled;
            injector.bounceIntensity = bounceIntensity;
            EditorUtility.SetDirty(injector);
            Lightmapping.Clear();
            if (!Lightmapping.Bake())
                throw new InvalidOperationException(
                    "Lightmapping.Bake failed (injectorEnabled=" + injectorEnabled +
                    ", bounce=" + bounceIntensity + ").");
        }

        private static Texture2D[] CopyCurrentLightmaps(
            string roomFolder,
            string poseId,
            string stateId,
            bool color)
        {
            string folder = roomFolder + "/" + poseId + "/" + stateId;
            RoomLocalEditorUtil.EnsureFolder(folder);
            LightmapData[] maps = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
            var result = new Texture2D[maps.Length];
            for (int i = 0; i < maps.Length; i++)
            {
                Texture source = color ? maps[i].lightmapColor : maps[i].lightmapDir;
                if (source == null)
                    throw new InvalidOperationException(
                        "Missing lightmap " + (color ? "color" : "direction") + " at " + i);
                string path = folder + "/LM" + i + (color ? "_Color.exr" : "_Direction.png");
                Texture2D readable = BlitReadable(source);
                if (color)
                    File.WriteAllBytes(path, readable.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                else
                    File.WriteAllBytes(path, readable.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(readable);
                AssetDatabase.ImportAsset(path);
                ConfigureImportedTexture(path);
                result[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            return result;
        }

        private static Texture2D BlitReadable(Texture source)
        {
            RenderTexture rt = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear);
            Graphics.Blit(source, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBAHalf, false, true);
            copy.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
            copy.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        private static void ConfigureImportedTexture(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static IncomingBounceData.RendererBinding[] BuildBindings(Transform roomRoot)
        {
            Dictionary<string, Renderer> keys = RoomLocalRendererKeys.BuildKeyMap(roomRoot);
            var list = new List<IncomingBounceData.RendererBinding>();
            foreach (KeyValuePair<string, Renderer> pair in keys)
            {
                if (pair.Value == null || pair.Value.lightmapIndex < 0)
                    continue;
                list.Add(new IncomingBounceData.RendererBinding
                {
                    canonicalKey = pair.Key,
                    lightmapIndex = pair.Value.lightmapIndex,
                    lightmapScaleOffset = pair.Value.lightmapScaleOffset
                });
            }

            return list.ToArray();
        }
    }
}
