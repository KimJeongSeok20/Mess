using System;
using System.Collections.Generic;
using System.IO;
using DunGen;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonAdjacentLightingPoC.Editor
{
    public static class DungeonAdjacentLightmapExtensionAuthoring
    {
        private const string RootFolder = "Assets/Experiments/DungeonAdjacentLightingPoC";
        private const string GeneratedFolder = RootFolder + "/Generated";
        private const string MeshFolder = GeneratedFolder + "/Meshes";
        private const string MaterialPath = GeneratedFolder + "/AdjacentExtensionBakeReceiver.mat";
        private const float DefaultDepth = 3f;
        private const float DefaultLateralCaptureDistance = 1.25f;
        private const float PortalCardInteriorInset = 0.5f;

        [MenuItem("Tools/Dungeon/Lighting/Adjacent Lightmap PoC/Create Connection Extension On Selected Doorway")]
        private static void CreateConnectionExtension()
        {
            Doorway doorway = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<Doorway>()
                : null;
            if (doorway == null)
            {
                EditorUtility.DisplayDialog(
                    "Adjacent Lightmap PoC",
                    "Select a GameObject with a DunGen.Doorway component.",
                    "OK");
                return;
            }

            DungeonAdjacentLightmapExtension extension = CreateConnectionExtensionForDoorway(doorway);
            if (extension != null)
            {
                Selection.activeGameObject = extension.gameObject;
                EditorGUIUtility.PingObject(extension.gameObject);
            }
        }

        public static IReadOnlyList<DungeonAdjacentLightmapExtension> CreateExtensionsForAllDoorways(
            GameObject roomRoot)
        {
            if (roomRoot == null)
                throw new ArgumentNullException(nameof(roomRoot));

            Doorway[] doorways = roomRoot.GetComponentsInChildren<Doorway>(true);
            Array.Sort(
                doorways,
                (left, right) => string.CompareOrdinal(
                    GetStableDoorwayId(left),
                    GetStableDoorwayId(right)));

            var extensions = new List<DungeonAdjacentLightmapExtension>(doorways.Length);
            for (int i = 0; i < doorways.Length; i++)
            {
                DungeonAdjacentLightmapExtension extension =
                    CreateConnectionExtensionForDoorway(doorways[i]);
                if (extension != null)
                    extensions.Add(extension);
            }

            return extensions;
        }

        public static DungeonAdjacentLightmapExtension CreateConnectionExtensionForDoorway(
            Doorway doorway)
        {
            if (doorway == null)
                throw new ArgumentNullException(nameof(doorway));

            string doorwayId = GetStableDoorwayId(doorway);
            Vector2 socketSize = doorway.Socket != null ? doorway.Socket.Size : new Vector2(2f, 2.5f);
            float portalHalfWidth = Mathf.Max(0.05f, socketSize.x * 0.5f);

            EnsureFolder(GeneratedFolder);
            EnsureFolder(MeshFolder);

            Tile tile = doorway.Tile != null ? doorway.Tile : doorway.GetComponentInParent<Tile>(true);
            string roomId = tile != null ? tile.name : doorway.transform.root.name;
            float captureWidth = socketSize.x + DefaultLateralCaptureDistance * 2f;
            float captureHeight = ResolveCaptureHeight(doorway, socketSize.y);
            string meshKey = Hash128.Compute(
                $"V6|{roomId}|{doorwayId}|{captureWidth:F4}|{captureHeight:F4}|{DefaultDepth:F4}")
                .ToString();
            string meshPath =
                $"{MeshFolder}/{Sanitize(roomId)}_{meshKey}_PortalShellExtensionV6.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = CreatePortalShellMesh(captureWidth, captureHeight, DefaultDepth);
                AssetDatabase.CreateAsset(mesh, meshPath);
            }

            Transform existing = doorway.transform.Find("[AdjacentLightmapExtension]");
            if (existing != null)
            {
                DungeonAdjacentLightmapExtension existingExtension =
                    existing.GetComponent<DungeonAdjacentLightmapExtension>();
                MeshFilter existingFilter = existing.GetComponent<MeshFilter>();
                MeshRenderer existingRenderer = existing.GetComponent<MeshRenderer>();
                if (existingExtension != null)
                {
                    if (existingFilter != null)
                        existingFilter.sharedMesh = mesh;
                    existingExtension.ConfigureAuthoring(
                        doorwayId,
                        doorway,
                        existingFilter,
                        existingRenderer,
                        portalHalfWidth);
                    EditorUtility.SetDirty(existingFilter);
                    EditorUtility.SetDirty(existingExtension);
                }
                return existingExtension;
            }

            GameObject extensionObject = new GameObject("[AdjacentLightmapExtension]");
            Undo.RegisterCreatedObjectUndo(extensionObject, "Create adjacent lightmap extension");
            extensionObject.transform.SetParent(doorway.transform, false);

            MeshFilter meshFilter = Undo.AddComponent<MeshFilter>(extensionObject);
            MeshRenderer meshRenderer = Undo.AddComponent<MeshRenderer>(extensionObject);
            DungeonAdjacentLightmapExtension extension =
                Undo.AddComponent<DungeonAdjacentLightmapExtension>(extensionObject);

            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterial = GetOrCreateBakeMaterial();
            meshRenderer.receiveGI = ReceiveGI.Lightmaps;
            meshRenderer.scaleInLightmap = 1f;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;

            GameObjectUtility.SetStaticEditorFlags(
                extensionObject,
                StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic);

            extension.ConfigureAuthoring(
                doorwayId,
                doorway,
                meshFilter,
                meshRenderer,
                portalHalfWidth);
            EditorUtility.SetDirty(extension);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[DungeonAdjacentLightmapPoC] Created connection extension for '{doorwayId}' at {meshPath}. " +
                $"portalWidth={portalHalfWidth * 2f:F2}, captureWidth={captureWidth:F2}, " +
                $"captureHeight={captureHeight:F2}. " +
                "All doorway extensions in this room are captured by the same P100/P0 room bakes.",
                extensionObject);
            return extension;
        }

        // Kept for existing PoC callers and already-authored scenes.
        public static DungeonAdjacentLightmapExtension CreateFloorExtensionForDoorway(Doorway doorway)
        {
            return CreateConnectionExtensionForDoorway(doorway);
        }

        public static string GetStableDoorwayId(Doorway doorway)
        {
            if (doorway == null)
                return string.Empty;

            Tile tile = doorway.Tile != null ? doorway.Tile : doorway.GetComponentInParent<Tile>(true);
            Transform root = tile != null ? tile.transform : doorway.transform.root;
            return DungeonAdjacentDoorwayIdentity.GetStableHierarchyId(doorway.transform, root);
        }

        private static float ResolveCaptureHeight(Doorway doorway, float socketHeight)
        {
            Tile tile = doorway.Tile != null ? doorway.Tile : doorway.GetComponentInParent<Tile>(true);
            Transform roomRoot = tile != null ? tile.transform : doorway.transform.root;
            float maximumY = doorway.transform.position.y + Mathf.Max(0.25f, socketHeight);
            MeshRenderer[] renderers = roomRoot.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer == null ||
                    renderer.GetComponentInParent<DungeonAdjacentLightmapExtension>(true) != null)
                {
                    continue;
                }
                maximumY = Mathf.Max(maximumY, renderer.bounds.max.y);
            }

            float roomHeightAboveDoorway = maximumY - doorway.transform.position.y;
            return Mathf.Clamp(
                Mathf.Max(socketHeight, roomHeightAboveDoorway),
                Mathf.Max(0.25f, socketHeight),
                6f);
        }

        [MenuItem("Tools/Dungeon/Lighting/Adjacent Lightmap PoC/Capture Selected Extension As P100")]
        private static void CaptureP100()
        {
            CaptureSelected(DungeonTileLightmapSwitcher.PowerLevel.P100);
        }

        [MenuItem("Tools/Dungeon/Lighting/Adjacent Lightmap PoC/Capture Selected Extension As P0")]
        private static void CaptureP0()
        {
            CaptureSelected(DungeonTileLightmapSwitcher.PowerLevel.P0);
        }

        private static void CaptureSelected(DungeonTileLightmapSwitcher.PowerLevel powerLevel)
        {
            DungeonAdjacentLightmapExtension extension = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<DungeonAdjacentLightmapExtension>()
                : null;
            if (extension == null || extension.ExtensionRenderer == null)
            {
                EditorUtility.DisplayDialog(
                    "Adjacent Lightmap PoC",
                    "Select a generated adjacent-lightmap extension object.",
                    "OK");
                return;
            }

            MeshRenderer renderer = extension.ExtensionRenderer;
            int lightmapIndex = renderer.lightmapIndex;
            LightmapData[] lightmaps = LightmapSettings.lightmaps;
            if (lightmapIndex < 0 || lightmaps == null || lightmapIndex >= lightmaps.Length ||
                lightmaps[lightmapIndex] == null || lightmaps[lightmapIndex].lightmapColor == null)
            {
                EditorUtility.DisplayDialog(
                    "Adjacent Lightmap PoC",
                    "The selected extension has no valid baked lightmap. Bake the isolated room first.",
                    "OK");
                return;
            }

            Undo.RecordObject(extension, $"Capture adjacent lightmap {powerLevel}");
            LightmapData lightmap = lightmaps[lightmapIndex];
            extension.CaptureState(
                powerLevel,
                lightmap.lightmapColor,
                lightmap.lightmapDir,
                renderer.lightmapScaleOffset);
            EditorUtility.SetDirty(extension);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[DungeonAdjacentLightmapPoC] Captured {powerLevel}: index={lightmapIndex}, " +
                $"scaleOffset={renderer.lightmapScaleOffset}, directional={lightmap.lightmapDir != null}.",
                extension);
        }

        private static Mesh CreatePortalShellMesh(float width, float height, float depth)
        {
            float halfWidth = Mathf.Max(0.1f, width * 0.5f);
            // DunGen doorways in NewPrison are authored with their origin on the floor,
            // not at the vertical socket center. Keep a tiny offset to avoid z-fighting
            // in authoring views; this mesh is disabled at runtime.
            float floorY = 0.02f;
            float ceilingY = Mathf.Max(floorY + 0.25f, height);
            float safeDepth = Mathf.Max(0.25f, depth);

            var mesh = new Mesh { name = "AdjacentLightmapPortalShellExtension" };
            mesh.vertices = new[]
            {
                // Floor, normal up.
                new Vector3(-halfWidth, floorY, 0f),
                new Vector3(-halfWidth, floorY, safeDepth),
                new Vector3(halfWidth, floorY, safeDepth),
                new Vector3(halfWidth, floorY, 0f),

                // Left reveal, normal toward the portal interior.
                new Vector3(-halfWidth, floorY, 0f),
                new Vector3(-halfWidth, ceilingY, 0f),
                new Vector3(-halfWidth, ceilingY, safeDepth),
                new Vector3(-halfWidth, floorY, safeDepth),

                // Right reveal, normal toward the portal interior.
                new Vector3(halfWidth, floorY, 0f),
                new Vector3(halfWidth, floorY, safeDepth),
                new Vector3(halfWidth, ceilingY, safeDepth),
                new Vector3(halfWidth, ceilingY, 0f),

                // Ceiling, normal down.
                new Vector3(-halfWidth, ceilingY, 0f),
                new Vector3(halfWidth, ceilingY, 0f),
                new Vector3(halfWidth, ceilingY, safeDepth),
                new Vector3(-halfWidth, ceilingY, safeDepth),

                // Portal irradiance card, normal into the source room. Doorway
                // forward points out of NewPrison tiles, so the room-facing
                // side is local -Z. The NewPrison doorway wall occupies roughly
                // the first 0.25m behind the socket, so the card sits 0.5m inside
                // the room instead of being embedded in that wall.
                // It does
                // not cast shadows, so it records outgoing doorway light without
                // closing the opening. The extra lateral width lets arbitrary
                // receiver walls, frames, and props use an aligned projection
                // instead of a sparse closest-triangle fallback.
                new Vector3(-halfWidth, floorY, -PortalCardInteriorInset),
                new Vector3(halfWidth, floorY, -PortalCardInteriorInset),
                new Vector3(halfWidth, ceilingY, -PortalCardInteriorInset),
                new Vector3(-halfWidth, ceilingY, -PortalCardInteriorInset)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3,
                4, 5, 6, 4, 6, 7,
                8, 9, 10, 8, 10, 11,
                12, 13, 14, 12, 14, 15,
                16, 18, 17, 16, 19, 18
            };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            var unwrap = new UnwrapParam();
            UnwrapParam.SetDefaults(out unwrap);
            Unwrapping.GenerateSecondaryUVSet(mesh, unwrap);
            return mesh;
        }

        private static Material GetOrCreateBakeMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
                return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new System.InvalidOperationException("URP Lit shader was not found.");

            material = new Material(shader)
            {
                name = "AdjacentExtensionBakeReceiver"
            };
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0f);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
                return;

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string name = Path.GetFileName(assetFolder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new System.InvalidOperationException($"Invalid asset folder '{assetFolder}'.");

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static string Sanitize(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value;
        }
    }
}
