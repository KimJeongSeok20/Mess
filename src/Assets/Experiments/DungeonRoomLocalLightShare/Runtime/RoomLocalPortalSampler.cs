using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    /// <summary>
    /// Samples an existing tile bake into a doorway portal texture.
    /// Does not call Lightmapping.Bake.
    /// </summary>
    public static class RoomLocalPortalSampler
    {
        private const float MaxRayDistance = 4.5f;

        public static Texture2D Capture(
            Transform tileRoot,
            Transform doorway,
            DungeonTileBakeData bake,
            int width,
            int height)
        {
            if (tileRoot == null || doorway == null || bake == null)
                return null;

            List<Surface> surfaces = BuildLookup(tileRoot, bake);
            if (surfaces.Count == 0)
                return null;

            Dictionary<int, Texture2D> readable = CacheReadableLightmaps(bake);
            try
            {
                Vector3 inward = -doorway.forward.normalized;
                Vector3 right = doorway.right.normalized;
                Vector3 up = doorway.up.normalized;
                Vector2 size = new Vector2(
                    RoomLocalDoorwayFrame.DefaultSocketWidth,
                    RoomLocalDoorwayFrame.DefaultSocketHeight);
                var component = doorway.GetComponent<DunGen.Doorway>();
                if (component != null && component.Socket != null)
                    size = component.Socket.Size;

                var portal = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
                {
                    name = tileRoot.name + "_Outgoing",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };

                Color[] pixels = new Color[width * height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float u = (x + 0.5f) / width;
                        float v = (y + 0.5f) / height;
                        Vector3 origin = doorway.position
                            + right * ((u - 0.5f) * size.x)
                            + up * (v * size.y)
                            + inward * 0.08f;
                        pixels[y * width + x] = Trace(origin, inward, surfaces, readable);
                    }
                }

                portal.SetPixels(pixels);
                portal.Apply(false, false);
                return portal;
            }
            finally
            {
                DisposeReadable(readable);
            }
        }

        public static Color Average(Texture2D texture)
        {
            if (texture == null)
                return Color.black;
            Color[] pixels = texture.GetPixels();
            if (pixels == null || pixels.Length == 0)
                return Color.black;

            double r = 0, g = 0, b = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                r += pixels[i].r;
                g += pixels[i].g;
                b += pixels[i].b;
            }

            float count = pixels.Length;
            return new Color((float)(r / count), (float)(g / count), (float)(b / count), 1f);
        }

        public static float AverageLuminance(Texture2D texture)
        {
            Color average = Average(texture);
            return RoomLocalLightShareMath.Luminance(average);
        }

        public static Texture2D CreateReadableCopy(Texture source)
        {
            return source == null ? null : CopyReadable(source);
        }

        public static Texture2D CreateCookie(Texture2D hdr, out float peak)
        {
            peak = 0f;
            if (hdr == null)
                return null;

            int width = hdr.width;
            int height = hdr.height;
            int size = Mathf.Max(width, height);
            var cookie = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = hdr.name + "_Cookie",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color[] source = hdr.GetPixels();
            peak = 1e-4f;
            for (int i = 0; i < source.Length; i++)
                peak = Mathf.Max(peak, Mathf.Max(source[i].r, Mathf.Max(source[i].g, source[i].b)));

            Color[] dest = new Color[size * size];
            for (int i = 0; i < dest.Length; i++)
                dest[i] = Color.black;
            int x0 = (size - width) / 2;
            int y0 = (size - height) / 2;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color pixel = source[y * width + x];
                    dest[(y0 + y) * size + (x0 + x)] = new Color(
                        Mathf.Clamp01(pixel.r / peak),
                        Mathf.Clamp01(pixel.g / peak),
                        Mathf.Clamp01(pixel.b / peak),
                        1f);
                }
            }

            cookie.SetPixels(dest);
            cookie.Apply(false, false);
            return cookie;
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
                best = hitDist;
                bestColor = DecodeLightmap(lightmap.GetPixelBilinear(atlas.x, atlas.y));
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
                uv2 = uvs[triangles[i]] * bary.x + uvs[triangles[i + 1]] * bary.y +
                      uvs[triangles[i + 2]] * bary.z;
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
                Mesh mesh = filter.sharedMesh;
                if (!mesh.isReadable || mesh.uv2 == null || mesh.uv2.Length != mesh.vertexCount)
                    continue;
                string path = RoomLocalRendererKeys.GetBareRelativePath(root, renderer.transform);
                if (!byPath.TryGetValue(path, out DungeonTileBakeData.RendererBakeEntry entry))
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
            var copy = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            copy.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            copy.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        private static void DisposeReadable(Dictionary<int, Texture2D> readable)
        {
            foreach (Texture2D texture in readable.Values)
                DestroyTexture(texture);
        }

        public static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
        }

        private static Color DecodeLightmap(Color encoded)
        {
            if (encoded.a >= 0.999f)
                return new Color(encoded.r, encoded.g, encoded.b, 1f);
            float multiplier = encoded.a * 34.493242f;
            return new Color(encoded.r * multiplier, encoded.g * multiplier, encoded.b * multiplier, 1f);
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
