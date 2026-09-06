using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Isolated visual comparison for any rotated tile. The reference uses its original rotated bake;
/// the candidate uses R000 color lightmaps plus a derived direction texture.
/// Reflection probes are disabled in both captures so this pass isolates static lightmaps.
/// </summary>
public static class DungeonLightingRotationRenderPoC
{
    private const string RotatedFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated";
    private const string EvidenceAssetFolder = "Assets/Experiments/DungeonLightingRotationPoC/Evidence";
    private const int Width = 640;
    private const int Height = 360;

    [Serializable]
    private sealed class RenderReport
    {
        public bool completed;
        public string failure;
        public string tile;
        public int rotation;
        public string power;
        public int viewCount;
        public float meanRgbAbsoluteError;
        public float meanReferenceLuminance;
        public float meanCandidateLuminance;
        public float referenceNonBlackFraction;
        public float candidateNonBlackFraction;
        public List<ViewReport> views = new List<ViewReport>();
    }

    [Serializable]
    private sealed class ViewReport
    {
        public string name;
        public Vector3 cameraPosition;
        public Vector3 cameraForward;
        public float meanRgbAbsoluteError;
        public float meanReferenceLuminance;
        public float meanCandidateLuminance;
        public float referenceNonBlackFraction;
        public float candidateNonBlackFraction;
        public string referenceImage;
        public string candidateImage;
        public string amplifiedDifferenceImage;
    }

    private sealed class PixelMetrics
    {
        public float mae;
        public float referenceLuminance;
        public float candidateLuminance;
        public float referenceNonBlack;
        public float candidateNonBlack;
    }

    [MenuItem("Tools/Dungeon Lighting/Rotation PoC/Render Maze P100 R090 Comparison")]
    public static void RenderMenu()
    {
        Debug.Log(RunCli());
    }

    public static string RunCli()
    {
        return RunCaseCli("Maze", 90, "P100");
    }

    public static string RunCaseCli(string tile, int rotation, string power)
    {
        var report = new RenderReport { tile = tile, rotation = rotation, power = power };
        LightmapData[] previousLightmaps = LightmapSettings.lightmaps;
        LightmapsMode previousMode = LightmapSettings.lightmapsMode;
        Scene previousActiveScene = SceneManager.GetActiveScene();
        Scene testScene = default;
        var generatedDirections = new List<Texture2D>();
        GameObject instance = null;
        GameObject cameraObject = null;

        try
        {
            if (rotation != 90 && rotation != 180 && rotation != 270)
                throw new ArgumentOutOfRangeException(nameof(rotation), "Rotation must be 90, 180, or 270.");
            if (power != "P100" && power != "P0")
                throw new ArgumentException("Power must be P100 or P0.", nameof(power));

            string targetSuffix = $"R{rotation:000}";
            string prefabPath = $"{RotatedFolder}/{tile}_{targetSuffix}.prefab";
            string dataR000Path = $"{RotatedFolder}/BakedData/{tile}_R000/{power}/{tile}_R000_BakeData.asset";
            string dataTargetPath = $"{RotatedFolder}/BakedData/{tile}_{targetSuffix}/{power}/{tile}_{targetSuffix}_BakeData.asset";
            GameObject prefab = LoadRequired<GameObject>(prefabPath);
            DungeonTileBakeData dataR000 = LoadRequired<DungeonTileBakeData>(dataR000Path);
            DungeonTileBakeData dataTarget = LoadRequired<DungeonTileBakeData>(dataTargetPath);
            if (dataR000.lightmapColors.Length != dataTarget.lightmapColors.Length)
                throw new InvalidOperationException($"R000/{targetSuffix} lightmap counts do not match.");

            testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(testScene);
            instance = PrefabUtility.InstantiatePrefab(prefab, testScene) as GameObject;
            if (instance == null)
                throw new InvalidOperationException($"Could not instantiate {tile}_{targetSuffix} in the preview scene.");

            const int isolatedLayer = 31;
            SetLayerRecursively(instance.transform, isolatedLayer);

            foreach (Light lightComponent in instance.GetComponentsInChildren<Light>(true))
                lightComponent.enabled = false;
            foreach (ReflectionProbe probe in instance.GetComponentsInChildren<ReflectionProbe>(true))
                probe.enabled = false;

            cameraObject = new GameObject("DungeonLightingRotationPoC_Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, testScene);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.fieldOfView = 78f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.cullingMask = 1 << isolatedLayer;

            Texture2D[] candidateDirections = BuildRotatedDirectionTextures(dataR000.lightmapDirections, rotation, generatedDirections);
            Vector3 probeCenter = FindCentralProbePosition(dataTarget.lightProbeEntries);
            Vector3[] localForwards =
            {
                Vector3.forward,
                Vector3.right,
                Vector3.back,
                Vector3.left
            };

            string evidenceFolder = Path.GetFullPath(EvidenceAssetFolder);
            Directory.CreateDirectory(evidenceFolder);

            for (int viewIndex = 0; viewIndex < localForwards.Length; viewIndex++)
            {
                string viewName = $"view_{viewIndex}_{DirectionLabel(localForwards[viewIndex])}";
                camera.transform.position = instance.transform.TransformPoint(probeCenter + Vector3.up * 0.15f);
                camera.transform.rotation = Quaternion.LookRotation(
                    instance.transform.TransformDirection(localForwards[viewIndex]),
                    instance.transform.TransformDirection(Vector3.up));

                ApplyLightmaps(instance, dataTarget, dataTarget.lightmapColors, dataTarget.lightmapDirections);
                Texture2D reference = Render(camera);
                ApplyLightmaps(instance, dataR000, dataR000.lightmapColors, candidateDirections);
                Texture2D candidate = Render(camera);
                Texture2D difference = BuildAmplifiedDifference(reference, candidate, 4f);

                try
                {
                    string prefix = $"{tile}_{power}_{targetSuffix}_{viewName}";
                    string referencePath = Path.Combine(evidenceFolder, prefix + "_reference.png");
                    string candidatePath = Path.Combine(evidenceFolder, prefix + "_candidate.png");
                    string differencePath = Path.Combine(evidenceFolder, prefix + "_diff_x4.png");
                    File.WriteAllBytes(referencePath, reference.EncodeToPNG());
                    File.WriteAllBytes(candidatePath, candidate.EncodeToPNG());
                    File.WriteAllBytes(differencePath, difference.EncodeToPNG());

                    PixelMetrics metrics = ComparePixels(reference, candidate);
                    report.views.Add(new ViewReport
                    {
                        name = viewName,
                        cameraPosition = camera.transform.position,
                        cameraForward = camera.transform.forward,
                        meanRgbAbsoluteError = metrics.mae,
                        meanReferenceLuminance = metrics.referenceLuminance,
                        meanCandidateLuminance = metrics.candidateLuminance,
                        referenceNonBlackFraction = metrics.referenceNonBlack,
                        candidateNonBlackFraction = metrics.candidateNonBlack,
                        referenceImage = ToAssetPath(referencePath),
                        candidateImage = ToAssetPath(candidatePath),
                        amplifiedDifferenceImage = ToAssetPath(differencePath)
                    });
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(reference);
                    UnityEngine.Object.DestroyImmediate(candidate);
                    UnityEngine.Object.DestroyImmediate(difference);
                }
            }

            report.viewCount = report.views.Count;
            if (report.viewCount > 0)
            {
                foreach (ViewReport view in report.views)
                {
                    report.meanRgbAbsoluteError += view.meanRgbAbsoluteError;
                    report.meanReferenceLuminance += view.meanReferenceLuminance;
                    report.meanCandidateLuminance += view.meanCandidateLuminance;
                    report.referenceNonBlackFraction += view.referenceNonBlackFraction;
                    report.candidateNonBlackFraction += view.candidateNonBlackFraction;
                }
                report.meanRgbAbsoluteError /= report.viewCount;
                report.meanReferenceLuminance /= report.viewCount;
                report.meanCandidateLuminance /= report.viewCount;
                report.referenceNonBlackFraction /= report.viewCount;
                report.candidateNonBlackFraction /= report.viewCount;
            }

            report.completed = true;
        }
        catch (Exception exception)
        {
            report.completed = false;
            report.failure = exception.ToString();
        }
        finally
        {
            LightmapSettings.lightmaps = previousLightmaps ?? Array.Empty<LightmapData>();
            LightmapSettings.lightmapsMode = previousMode;
            foreach (Texture2D texture in generatedDirections)
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
            if (cameraObject != null)
                UnityEngine.Object.DestroyImmediate(cameraObject);
            if (instance != null)
                UnityEngine.Object.DestroyImmediate(instance);
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                SceneManager.SetActiveScene(previousActiveScene);
            if (testScene.IsValid() && testScene.isLoaded)
                EditorSceneManager.CloseScene(testScene, true);
        }

        string json = JsonUtility.ToJson(report, true);
        try
        {
            string reportPath = Path.GetFullPath($"{EvidenceAssetFolder}/{tile}_{power}_R{rotation:000}_RenderReport.json");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? Path.GetFullPath(EvidenceAssetFolder));
            File.WriteAllText(reportPath, json);
        }
        catch (Exception writeException)
        {
            Debug.LogWarning($"[DungeonLightingRotationPoC] Could not persist render report: {writeException.Message}");
        }

        Debug.Log($"[DungeonLightingRotationPoC] Render comparison\n{json}");
        return json;
    }

    private static void ApplyLightmaps(
        GameObject instance,
        DungeonTileBakeData data,
        Texture2D[] colors,
        Texture2D[] directions)
    {
        var maps = new LightmapData[colors.Length];
        for (int i = 0; i < maps.Length; i++)
        {
            maps[i] = new LightmapData
            {
                lightmapColor = colors[i],
                lightmapDir = directions != null && i < directions.Length ? directions[i] : null
            };
        }
        LightmapSettings.lightmapsMode = LightmapsMode.CombinedDirectional;
        LightmapSettings.lightmaps = maps;

        DungeonTileBakeData.RendererBakeEntry[] entries = data.rendererEntries ?? Array.Empty<DungeonTileBakeData.RendererBakeEntry>();
        var renderersByPath = new Dictionary<string, List<Renderer>>(StringComparer.Ordinal);
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            string path = GetRelativePath(instance.transform, renderer.transform);
            if (!renderersByPath.TryGetValue(path, out List<Renderer> bucket))
            {
                bucket = new List<Renderer>();
                renderersByPath.Add(path, bucket);
            }
            bucket.Add(renderer);
        }

        var useCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < entries.Length; i++)
        {
            DungeonTileBakeData.RendererBakeEntry entry = entries[i];
            if (!renderersByPath.TryGetValue(entry.relativePath, out List<Renderer> bucket))
                throw new InvalidOperationException($"Renderer path missing while applying {data.name}: {entry.relativePath}");
            useCounts.TryGetValue(entry.relativePath, out int useIndex);
            if (useIndex >= bucket.Count)
                throw new InvalidOperationException($"Renderer path exhausted while applying {data.name}: {entry.relativePath}");
            Renderer renderer = bucket[useIndex];
            useCounts[entry.relativePath] = useIndex + 1;
            renderer.lightmapIndex = entry.lightmapIndex;
            renderer.lightmapScaleOffset = entry.lightmapScaleOffset;
        }
    }

    private static Texture2D[] BuildRotatedDirectionTextures(
        Texture2D[] sources,
        float degrees,
        List<Texture2D> lifetime)
    {
        Quaternion rotation = Quaternion.Euler(0f, degrees, 0f);
        var results = new Texture2D[sources.Length];
        for (int textureIndex = 0; textureIndex < sources.Length; textureIndex++)
        {
            Texture2D readable = ReadbackTexture(sources[textureIndex]);
            try
            {
                Color[] pixels = readable.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color encoded = pixels[i];
                    Vector3 direction = new Vector3(encoded.r - 0.5f, encoded.g - 0.5f, encoded.b - 0.5f);
                    direction = rotation * direction;
                    pixels[i] = new Color(direction.x + 0.5f, direction.y + 0.5f, direction.z + 0.5f, encoded.a);
                }

                var derived = new Texture2D(readable.width, readable.height, TextureFormat.RGBAHalf, false, true)
                {
                    name = sources[textureIndex].name + $"_DerivedR{Mathf.RoundToInt(degrees):000}",
                    wrapMode = sources[textureIndex].wrapMode,
                    filterMode = sources[textureIndex].filterMode
                };
                derived.SetPixels(pixels);
                derived.Apply(false, true);
                results[textureIndex] = derived;
                lifetime.Add(derived);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(readable);
            }
        }
        return results;
    }

    private static Texture2D Render(Camera camera)
    {
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            antiAliasing = 1
        };
        target.Create();
        RenderTexture previous = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            image.Apply(false, false);
            return image;
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static Texture2D ReadbackTexture(Texture source)
    {
        RenderTexture temporary = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        RenderTexture previous = RenderTexture.active;
        bool previousSrgbWrite = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = false;
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            var result = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            result.Apply(false, false);
            return result;
        }
        finally
        {
            GL.sRGBWrite = previousSrgbWrite;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }
    }

    private static Texture2D BuildAmplifiedDifference(Texture2D reference, Texture2D candidate, float amplification)
    {
        Color32[] a = reference.GetPixels32();
        Color32[] b = candidate.GetPixels32();
        var output = new Color32[a.Length];
        for (int i = 0; i < output.Length; i++)
        {
            output[i] = new Color32(
                (byte)Mathf.Clamp(Mathf.Abs(a[i].r - b[i].r) * amplification, 0f, 255f),
                (byte)Mathf.Clamp(Mathf.Abs(a[i].g - b[i].g) * amplification, 0f, 255f),
                (byte)Mathf.Clamp(Mathf.Abs(a[i].b - b[i].b) * amplification, 0f, 255f),
                255);
        }
        var result = new Texture2D(reference.width, reference.height, TextureFormat.RGBA32, false, false);
        result.SetPixels32(output);
        result.Apply(false, false);
        return result;
    }

    private static PixelMetrics ComparePixels(Texture2D reference, Texture2D candidate)
    {
        Color32[] a = reference.GetPixels32();
        Color32[] b = candidate.GetPixels32();
        double absolute = 0d;
        double luminanceA = 0d;
        double luminanceB = 0d;
        int nonBlackA = 0;
        int nonBlackB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            absolute += (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b)) / (3d * 255d);
            float la = (0.2126f * a[i].r + 0.7152f * a[i].g + 0.0722f * a[i].b) / 255f;
            float lb = (0.2126f * b[i].r + 0.7152f * b[i].g + 0.0722f * b[i].b) / 255f;
            luminanceA += la;
            luminanceB += lb;
            if (la > 0.01f) nonBlackA++;
            if (lb > 0.01f) nonBlackB++;
        }
        return new PixelMetrics
        {
            mae = (float)(absolute / a.Length),
            referenceLuminance = (float)(luminanceA / a.Length),
            candidateLuminance = (float)(luminanceB / a.Length),
            referenceNonBlack = nonBlackA / (float)a.Length,
            candidateNonBlack = nonBlackB / (float)b.Length
        };
    }

    private static Vector3 FindCentralProbePosition(DungeonTileBakeData.LightProbeBakeEntry[] entries)
    {
        if (entries == null || entries.Length == 0)
            return Vector3.zero;
        Vector3 min = entries[0].localPosition;
        Vector3 max = min;
        for (int i = 1; i < entries.Length; i++)
        {
            min = Vector3.Min(min, entries[i].localPosition);
            max = Vector3.Max(max, entries[i].localPosition);
        }
        Vector3 center = (min + max) * 0.5f;
        Vector3 selected = entries[0].localPosition;
        float selectedDistance = (selected - center).sqrMagnitude;
        for (int i = 1; i < entries.Length; i++)
        {
            float distance = (entries[i].localPosition - center).sqrMagnitude;
            if (distance < selectedDistance)
            {
                selected = entries[i].localPosition;
                selectedDistance = distance;
            }
        }
        return selected;
    }

    private static string DirectionLabel(Vector3 direction)
    {
        if (direction == Vector3.forward) return "forward";
        if (direction == Vector3.back) return "back";
        if (direction == Vector3.right) return "right";
        return "left";
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    private static string GetRelativePath(Transform root, Transform target)
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

    private static string ToAssetPath(string fullPath)
    {
        string normalized = fullPath.Replace('\\', '/');
        string project = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/');
        return normalized.StartsWith(project + "/", StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(project.Length + 1)
            : normalized;
    }

    private static T LoadRequired<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            throw new InvalidOperationException($"Required asset was not found: {path}");
        return asset;
    }
}
