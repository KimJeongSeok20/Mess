using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DungeonSHSamplingTest.Editor
{
    [InitializeOnLoad]
    public static class SHSamplingTestBuilder
    {
        public const string RootPath = "Assets/Experiments/DungeonSHSamplingTest";
        public const string ScenePath = RootPath + "/Scenes/SHSamplingTest.unity";
        public const string OutputPath = "Artifacts/SHSamplingTest";
        private const string RoomPath = "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/StartRoom.prefab";
        private const string ModelPath = "Assets/FPS/Cyber_Generic_mesh.fbx";
        private const string DoorPath = "Doorways/Door_LG_A/DoorwayPoint";
        private const string RestoreKey = "SHSamplingTest.PreviousPlayScene";
        private const string RunningKey = "SHSamplingTest.RestorePlayScene";

        static SHSamplingTestBuilder()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Tools/Lighting/SH Sampling TEST/Build isolated test scene")]
        public static void BuildMenu() => Debug.Log(Build());

        // The currently open scenes are left loaded, unsaved and unchanged. Only the new
        // additive scene is saved; its temporary global lightmap registration is restored.
        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build the SH test in Edit Mode.");
            var original = SceneManager.GetActiveScene();
            var originalRoots = original.GetRootGameObjects().Select(g => g.GetInstanceID()).ToArray();
            bool originalDirty = original.isDirty;
            var originalMaps = LightmapSettings.lightmaps;
            var originalMode = LightmapSettings.lightmapsMode;
            EnsureFolder(RootPath + "/Scenes");
            EnsureFolder(RootPath + "/Materials");
            Directory.CreateDirectory(OutputPath);
            Scene test = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(test);
                var root = new GameObject("SH SAMPLING TEST - isolated, original bakes");
                var rig = root.AddComponent<SHSamplingTestRig>();
                GameObject a = MakeRoom("Room A - StartRoom R000", root.transform, test);
                GameObject b = MakeRoom("Room B - StartRoom R180", root.transform, test);
                b.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                Doorway doorA = a.transform.Find(DoorPath).GetComponent<Doorway>();
                Doorway doorB = b.transform.Find(DoorPath).GetComponent<Doorway>();
                b.transform.position += doorA.transform.position - doorB.transform.position;
                OpenConnection(doorA);
                OpenConnection(doorB);
                a.GetComponent<DungeonTileRotationSelectorV2>().ApplyForCurrentRotation(false);
                b.GetComponent<DungeonTileRotationSelectorV2>().ApplyForCurrentRotation(false);
                rig.roomA = a.transform;
                rig.roomB = b.transform;
                rig.switcherA = a.GetComponent<DungeonTileLightmapSwitcher>();
                rig.switcherB = b.GetComponent<DungeonTileLightmapSwitcher>();
                rig.localBoundsA = LocalBounds(a);
                rig.localBoundsB = LocalBounds(b);

                rig.portalFrame = new GameObject("TEST portal - A to B, floor origin").transform;
                rig.portalFrame.SetParent(root.transform, false);
                rig.portalFrame.SetPositionAndRotation(doorA.transform.position, doorA.transform.rotation);
                // Use the actual source blocker extents when available, not the socket's
                // nominal size: the large frame is wider than its default 1 x 2 socket.
                Vector2 opening = OpeningSize(doorA);
                rig.portalWidth = opening.x;
                rig.portalHeight = opening.y;
                rig.doorLeaf = new GameObject("TEST door hinge - occlusion only").transform;
                rig.doorLeaf.SetParent(rig.portalFrame, false);
                rig.doorLeaf.localPosition = new Vector3(-opening.x * .5f, 0f, 0f);
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = "TEST door slab";
                slab.transform.SetParent(rig.doorLeaf, false);
                slab.transform.localPosition = new Vector3(opening.x * .5f, opening.y * .5f, 0f);
                slab.transform.localScale = new Vector3(opening.x, opening.y, .12f);
                slab.GetComponent<Renderer>().sharedMaterial = MaterialAsset("TestDoor", "Universal Render Pipeline/Unlit", new Color(.16f, .22f, .25f));

                var blockers = a.GetComponentsInChildren<Collider>(true)
                    .Concat(b.GetComponentsInChildren<Collider>(true))
                    .Where(IsStructuralBlocker).ToList();
                blockers.Add(slab.GetComponent<Collider>());
                rig.blockers = blockers.ToArray();

                rig.actor = new GameObject("TEST actor - same player visual, no gameplay").transform;
                rig.actor.SetParent(root.transform, false);
                var model = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), test) as GameObject;
                if (model == null) throw new InvalidOperationException("Player visual FBX could not be instantiated.");
                model.transform.SetParent(rig.actor, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                foreach (Animator animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                rig.sampleAnchor = new GameObject("Body sample anchor - 1 m above root").transform;
                rig.sampleAnchor.SetParent(rig.actor, false);
                rig.sampleAnchor.localPosition = Vector3.up;

                var witness = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                witness.name = "Neutral diffuse SH witness";
                witness.transform.SetParent(rig.actor, false);
                witness.transform.localPosition = new Vector3(-.7f, 1f, 0f);
                witness.transform.localScale = Vector3.one * .36f;
                Object.DestroyImmediate(witness.GetComponent<Collider>());
                witness.GetComponent<Renderer>().sharedMaterial = MaterialAsset("NeutralWitness", "Universal Render Pipeline/Lit", new Color(.65f, .65f, .65f));
                rig.actorRenderers = rig.actor.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in rig.actorRenderers)
                {
                    renderer.lightmapIndex = -1;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }

                var cameraObject = new GameObject("SH TEST Camera", typeof(Camera), typeof(AudioListener));
                cameraObject.transform.SetParent(root.transform, false);
                var camera = cameraObject.GetComponent<Camera>();
                camera.tag = "MainCamera";
                camera.fieldOfView = 55f;
                camera.nearClipPlane = .05f;
                camera.farClipPlane = 100f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.025f, .025f, .025f);
                camera.allowHDR = true;
                var cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.renderPostProcessing = false;
                rig.viewCamera = camera;

                Vector3 portal = rig.portalFrame.position;
                Vector3 forward = rig.portalFrame.forward;
                rig.stations = new[] {
                    portal - forward * .7f,
                    portal + forward * .7f,
                    a.transform.TransformPoint(new Vector3(7f, 0f, 10f)),
                    a.transform.TransformPoint(new Vector3(6f, 0f, 2.5f)),
                    portal - forward * .25f,
                    portal + forward * .25f
                };
                rig.stationNames = new[] { "A doorway", "B doorway", "A interior", "A partition", "A open threshold", "B open threshold" };
                rig.stationCameraOffsets = new[] { Vector3.zero, Vector3.zero, Vector3.zero, new Vector3(-1f, 1.6f, -1.8f), Vector3.zero, Vector3.zero };
                rig.actor.position = rig.stations[0];
                camera.transform.position = rig.actor.position - forward * 3f + rig.portalFrame.right * 1.6f + Vector3.up * 1.7f;
                camera.transform.LookAt(rig.actor.position + Vector3.up);
                rig.improved = true;
                rig.useBodyAnchor = true;
                rig.powerA = true;
                rig.powerB = false;
                rig.doorOpen = false;

                RenderSettings.skybox = null;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientIntensity = 0f;
                RenderSettings.reflectionIntensity = 0f;
                RenderSettings.fog = false;
                if (!EditorSceneManager.SaveScene(test, ScenePath)) throw new IOException("Could not save isolated SH test scene.");
                string report = Newtonsoft.Json.JsonConvert.SerializeObject(new {
                    scene = ScenePath, sourceRoom = RoomPath, playerVisual = ModelPath,
                    roomCount = 2, separateTestDoor = true, usesExistingBakes = true,
                    originalDoorwaySystemsEnabled = false, reflectionsOnActor = false,
                    baselineIsTestEmulation = true,
                    blockers = rig.blockers.Length, openingWidth = opening.x, openingHeight = opening.y,
                    originalScene = original.path, originalDirty, visualVerified = false,
                    scope = "Sampler/anchor A-B experiment; no new GI, no production adoption, no rebake."
                }, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(OutputPath + "/build.json", report);
                return report;
            }
            finally
            {
                if (test.IsValid() && test.isLoaded) EditorSceneManager.CloseScene(test, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                LightmapSettings.lightmapsMode = originalMode;
                LightmapSettings.lightmaps = originalMaps;
                if (original.isDirty != originalDirty || !originalRoots.SequenceEqual(original.GetRootGameObjects().Select(g => g.GetInstanceID())))
                    throw new InvalidOperationException("Original scene preservation check failed.");
            }
        }

        [MenuItem("Tools/Lighting/SH Sampling TEST/Play isolated test (restore on Stop)")]
        public static void Play()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing.");
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (scene == null) throw new InvalidOperationException("Build the SH test scene first.");
            SessionState.SetString(RestoreKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(RunningKey, true);
            EditorSceneManager.playModeStartScene = scene;
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(RunningKey, false)) return;
            string previous = SessionState.GetString(RestoreKey, "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
            SessionState.SetBool(RunningKey, false);
        }

        public static string Capture(string name)
        {
            var rig = Object.FindFirstObjectByType<SHSamplingTestRig>();
            if (rig == null || rig.viewCamera == null) throw new InvalidOperationException("SH test is not running.");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("Invalid capture name.");
            Directory.CreateDirectory(OutputPath);
            rig.RefreshSample();
            var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var priorTarget = rig.viewCamera.targetTexture;
            var priorActive = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                rig.viewCamera.targetTexture = rt;
                rig.viewCamera.Render();
                RenderTexture.active = rt;
                texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                texture.Apply();
                string path = OutputPath + "/" + name + ".png";
                File.WriteAllBytes(path, texture.EncodeToPNG());
                File.WriteAllText(OutputPath + "/" + name + ".json", rig.GetReportJson());
                return Path.GetFullPath(path);
            }
            finally
            {
                rig.viewCamera.targetTexture = priorTarget;
                RenderTexture.active = priorActive;
                if (texture != null) Object.DestroyImmediate(texture);
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static GameObject MakeRoom(string name, Transform parent, Scene scene)
        {
            var room = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RoomPath), scene) as GameObject;
            if (room == null) throw new InvalidOperationException("StartRoom source is missing.");
            PrefabUtility.UnpackPrefabInstance(room, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            room.name = name;
            room.transform.SetParent(parent, false);
            room.transform.localPosition = Vector3.zero;
            room.transform.localRotation = Quaternion.identity;
            foreach (MonoBehaviour behaviour in room.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour is DungeonTileLightmapSwitcher || behaviour is DungeonTilePowerBakeSet ||
                    behaviour is DungeonTileRotationSelectorV2 || behaviour is DungeonTileLightingPolicy || behaviour is Tile || behaviour is Doorway) continue;
                behaviour.enabled = false;
            }
            foreach (Light light in room.GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (AudioSource audio in room.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
            foreach (Camera camera in room.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (Rigidbody body in room.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            return room;
        }

        private static void OpenConnection(Doorway doorway)
        {
            foreach (GameObject blocker in doorway.BlockerSceneObjects) if (blocker != null) blocker.SetActive(false);
            foreach (GameObject connector in doorway.ConnectorSceneObjects) if (connector != null) connector.SetActive(true);
        }

        private static Bounds LocalBounds(GameObject room)
        {
            var tile = room.GetComponent<Tile>();
            tile.RecalculateBounds();
            Bounds world = tile.Bounds;
            return new Bounds(room.transform.InverseTransformPoint(world.center), world.size);
        }

        private static bool IsStructuralBlocker(Collider collider)
        {
            if (collider == null || collider.isTrigger || !collider.enabled) return false;
            for (Transform t = collider.transform; t != null; t = t.parent)
            {
                string n = t.name;
                if (n.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Doorways", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Pillar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Reception", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static Vector2 OpeningSize(Doorway doorway)
        {
            foreach (GameObject blocker in doorway.BlockerSceneObjects)
            {
                if (blocker == null) continue;
                var renderer = blocker.GetComponentInChildren<Renderer>(true);
                if (renderer == null) continue;
                Bounds bounds = renderer.bounds;
                Vector3 right = doorway.transform.right;
                float width = Mathf.Abs(right.x) * bounds.size.x + Mathf.Abs(right.z) * bounds.size.z;
                if (width > .5f && bounds.size.y > 1f) return new Vector2(Mathf.Min(width, 3.5f), Mathf.Min(bounds.size.y, 3.5f));
            }
            return doorway.Socket.Size;
        }

        private static Material MaterialAsset(string name, string shaderName, Color color)
        {
            string path = RootPath + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("Missing shader " + shaderName);
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
