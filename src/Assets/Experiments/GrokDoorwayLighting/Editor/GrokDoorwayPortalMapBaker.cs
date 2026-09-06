using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrokDoorwayLighting.Editor
{
    public static class GrokDoorwayPortalMapBaker
    {
        public const string GeneratedFolder = GrokDoorwaySceneBuilder.AssetRoot + "/Generated";
        public const string StartPortalPath = GeneratedFolder + "/StartRoom_R000_Door_SM_A_Portal.asset";
        public const string AdminPortalPath = GeneratedFolder + "/AdminstrativeSegregation_R000_Door_SM_A_Portal.asset";

        private const int PortalWidth = 64;
        private const int PortalHeight = 128;
        private const float MaxRayDistance = 4.5f;

        public static string CaptureBothRooms()
        {
            if (Application.isPlaying)
                return "FAIL: Unity is in Play Mode.";
            if (Lightmapping.isRunning)
                return "FAIL: a lightmap bake is already running. Do not interrupt it.";

            try
            {
                GrokDoorwaySceneBuilder.EnsureFolder(GeneratedFolder);
                string start = CaptureRoom(
                    GrokDoorwaySceneBuilder.StartPrefabPath,
                    GrokDoorwaySceneBuilder.PreferredDoorwayPath,
                    StartPortalPath);
                string admin = CaptureRoom(
                    GrokDoorwaySceneBuilder.AdminPrefabPath,
                    GrokDoorwaySceneBuilder.PreferredDoorwayPath,
                    AdminPortalPath);
                return "PASS GrokDoorway portal capture\n" + start + "\n" + admin;
            }
            catch (Exception exception)
            {
                return $"FAIL: {exception}";
            }
        }

        private static string CaptureRoom(string prefabPath, string doorwayPath, string assetPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new InvalidOperationException("Missing prefab " + prefabPath);

            Scene temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, temp);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Transform doorway = instance.transform.Find(doorwayPath);
                if (doorway == null)
                    throw new InvalidOperationException(prefab.name + " missing doorway " + doorwayPath);

                var switcher = instance.GetComponent<DungeonTileLightmapSwitcher>();
                if (switcher == null)
                    throw new InvalidOperationException(prefab.name + " has no DungeonTileLightmapSwitcher.");

                DungeonTileBakeData p100 = switcher.GetBakeData(DungeonTileLightmapSwitcher.PowerLevel.P100);
                DungeonTileBakeData p0 = switcher.GetBakeData(DungeonTileLightmapSwitcher.PowerLevel.P0);
                if (p100 == null || p0 == null)
                    throw new InvalidOperationException(prefab.name + " is missing P100/P0 bake data.");

                Texture2D p100Tex = RasterizePortal(instance.transform, doorway, p100);
                Texture2D p0Tex = RasterizePortal(instance.transform, doorway, p0);
                string p100Path = WriteTexture(assetPath, "P100", p100Tex);
                string p0Path = WriteTexture(assetPath, "P0", p0Tex);

                var map = AssetDatabase.LoadAssetAtPath<GrokDoorwayPortalMap>(assetPath);
                if (map == null)
                {
                    map = ScriptableObject.CreateInstance<GrokDoorwayPortalMap>();
                    AssetDatabase.CreateAsset(map, assetPath);
                }

                map.roomName = prefab.name;
                map.doorwayPath = doorwayPath;
                map.power100 = AssetDatabase.LoadAssetAtPath<Texture2D>(p100Path);
                map.power0 = AssetDatabase.LoadAssetAtPath<Texture2D>(p0Path);
                map.power100Average = Average(p100Tex);
                map.power0Average = Average(p0Tex);
                EditorUtility.SetDirty(map);
                UnityEngine.Object.DestroyImmediate(p100Tex);
                UnityEngine.Object.DestroyImmediate(p0Tex);
                AssetDatabase.SaveAssets();

                return
                    $"{prefab.name}: p100Avg={map.power100Average} p0Avg={map.power0Average} " +
                    $"asset={assetPath}";
            }
            finally
            {
                if (instance != null)
                    UnityEngine.Object.DestroyImmediate(instance);
                if (temp.IsValid() && temp.isLoaded)
                    EditorSceneManager.CloseScene(temp, true);
            }
        }

        private static Texture2D RasterizePortal(Transform root, Transform doorway, DungeonTileBakeData bake)
        {
            var lookup = BuildLookup(root, bake);
            var readable = CacheReadableLightmaps(bake);

            Vector3 inward = -doorway.forward.normalized;
            Vector3 right = doorway.right.normalized;
            Vector3 up = doorway.up.normalized;
            Vector2 size = new Vector2(1f, 2.5f);
            var component = doorway.GetComponent<DunGen.Doorway>();
            if (component != null && component.Socket != null)
                size = component.Socket.Size;

            float halfWidth = size.x * 0.5f;
            float height = size.y;
            var portal = new Texture2D(PortalWidth, PortalHeight, TextureFormat.RGBAHalf, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[PortalWidth * PortalHeight];
            for (int y = 0; y < PortalHeight; y++)
            {
                for (int x = 0; x < PortalWidth; x++)
                {
                    float u = (x + 0.5f) / PortalWidth;
                    float v = (y + 0.5f) / PortalHeight;
                    Vector3 origin = doorway.position
                        + right * ((u - 0.5f) * size.x)
                        + up * (v * height)
                        + inward * 0.08f;

                    pixels[y * PortalWidth + x] = Trace(
                        origin,
                        inward,
                        lookup,
                        readable);
                }
            }

            portal.SetPixels(pixels);
            portal.Apply(false, false);
            DisposeReadable(readable);
            return portal;
        }

        private static Color Trace(
            Vector3 origin,
            Vector3 direction,
            List<Surface> surfaces,
            Dictionary<int, Texture2D> readable)
        {
            float best = MaxRayDistance;
            Color bestColor = Color.black;

            for (int i = 0; i < surfaces.Count; i++)
            {
                Surface surface = surfaces[i];
                if (!surface.bounds.IntersectRay(new Ray(origin, direction), out float boundsDist) ||
                    boundsDist > best)
                    continue;

                if (!TryIntersectMesh(surface, origin, direction, best, out float hitDist, out Vector2 uv2))
                    continue;

                if (!readable.TryGetValue(surface.lightmapIndex, out Texture2D lightmap) || lightmap == null)
                    continue;

                Vector2 atlas = new Vector2(
                    uv2.x * surface.scaleOffset.x + surface.scaleOffset.z,
                    uv2.y * surface.scaleOffset.y + surface.scaleOffset.w);
                Color encoded = lightmap.GetPixelBilinear(atlas.x, atlas.y);
                best = hitDist;
                bestColor = DecodeLightmap(encoded);
            }

            bestColor.a = 1f;
            return bestColor;
        }

        private static bool TryIntersectMesh(
            Surface surface,
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            out float hitDistance,
            out Vector2 uv2)
        {
            hitDistance = maxDistance;
            uv2 = Vector2.zero;
            Vector3[] vertices = surface.vertices;
            Vector2[] uvs = surface.uv2;
            int[] triangles = surface.triangles;
            bool hit = false;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = surface.localToWorld.MultiplyPoint3x4(vertices[triangles[i]]);
                Vector3 b = surface.localToWorld.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                Vector3 c = surface.localToWorld.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                if (!IntersectTriangle(origin, direction, a, b, c, out float t, out Vector3 bary))
                    continue;
                if (t <= 0.001f || t >= hitDistance)
                    continue;

                hitDistance = t;
                uv2 = uvs[triangles[i]] * bary.x + uvs[triangles[i + 1]] * bary.y + uvs[triangles[i + 2]] * bary.z;
                hit = true;
            }

            return hit;
        }

        private static bool IntersectTriangle(
            Vector3 origin,
            Vector3 direction,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            out float t,
            out Vector3 bary)
        {
            t = 0f;
            bary = Vector3.zero;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(direction, edge2);
            float det = Vector3.Dot(edge1, p);
            if (Mathf.Abs(det) < 1e-8f)
                return false;

            float inv = 1f / det;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f)
                return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(direction, q) * inv;
            if (v < 0f || u + v > 1f)
                return false;

            t = Vector3.Dot(edge2, q) * inv;
            bary = new Vector3(1f - u - v, u, v);
            return t > 0f;
        }

        private static List<Surface> BuildLookup(Transform root, DungeonTileBakeData bake)
        {
            var byPath = new Dictionary<string, DungeonTileBakeData.RendererBakeEntry>();
            var entries = bake.rendererEntries ?? Array.Empty<DungeonTileBakeData.RendererBakeEntry>();
            for (int i = 0; i < entries.Length; i++)
            {
                if (!string.IsNullOrEmpty(entries[i].relativePath) && entries[i].lightmapIndex >= 0)
                    byPath[entries[i].relativePath] = entries[i];
            }

            var surfaces = new List<Surface>();
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;

                string path = RelativePath(root, renderer.transform);
                if (!byPath.TryGetValue(path, out DungeonTileBakeData.RendererBakeEntry entry))
                    continue;

                Mesh mesh = filter.sharedMesh;
                if (mesh.uv2 == null || mesh.uv2.Length != mesh.vertexCount)
                    continue;

                surfaces.Add(new Surface
                {
                    localToWorld = renderer.transform.localToWorldMatrix,
                    bounds = renderer.bounds,
                    vertices = mesh.vertices,
                    uv2 = mesh.uv2,
                    triangles = mesh.triangles,
                    lightmapIndex = entry.lightmapIndex,
                    scaleOffset = entry.lightmapScaleOffset
                });
            }

            return surfaces;
        }

        private static Dictionary<int, Texture2D> CacheReadableLightmaps(DungeonTileBakeData bake)
        {
            var map = new Dictionary<int, Texture2D>();
            Texture2D[] colors = bake.lightmapColors ?? Array.Empty<Texture2D>();
            for (int i = 0; i < colors.Length; i++)
            {
                if (colors[i] != null)
                    map[i] = CopyReadable(colors[i]);
            }

            return map;
        }

        private static Texture2D CopyReadable(Texture source)
        {
            int width = source.width;
            int height = source.height;
            RenderTexture rt = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear);
            Graphics.Blit(source, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
            copy.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            copy.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        private static void DisposeReadable(Dictionary<int, Texture2D> readable)
        {
            foreach (Texture2D texture in readable.Values)
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static Color DecodeLightmap(Color encoded)
        {
            if (encoded.a >= 0.999f)
                return new Color(encoded.r, encoded.g, encoded.b, 1f);

            float multiplier = encoded.a * 34.493242f;
            return new Color(encoded.r * multiplier, encoded.g * multiplier, encoded.b * multiplier, 1f);
        }

        private static Color Average(Texture2D texture)
        {
            Color[] pixels = texture.GetPixels();
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].maxColorComponent <= 0.0001f)
                    continue;
                sum += new Vector3(pixels[i].r, pixels[i].g, pixels[i].b);
                count++;
            }

            if (count == 0)
                return Color.black;
            sum /= count;
            return new Color(sum.x, sum.y, sum.z, 1f);
        }

        private static string WriteTexture(string assetPath, string suffix, Texture2D texture)
        {
            string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(assetPath) + "_" + suffix + ".exr";
            string path = directory + "/" + name;
            File.WriteAllBytes(path, texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                importer.sRGBTexture = false;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return path;
        }

        private static string RelativePath(Transform root, Transform target)
        {
            if (root == target)
                return string.Empty;

            var names = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private struct Surface
        {
            public Matrix4x4 localToWorld;
            public Bounds bounds;
            public Vector3[] vertices;
            public Vector2[] uv2;
            public int[] triangles;
            public int lightmapIndex;
            public Vector4 scaleOffset;
        }
    }
}
