using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonAdjacentLightingPoC.Editor
{
    public static class DungeonAdjacentLightingPoCStillRenderer
    {
        private const string RootFolder = "Assets/Experiments/DungeonAdjacentLightingPoC";
        private const string PreviewScenePath = RootFolder + "/Scenes/Start_Admin_WiredRuntimePreview.unity";
        private const string ScreenshotFolder = RootFolder + "/Screenshots";
        private static readonly int AdjacentEnabledId = Shader.PropertyToID("_AdjacentLightmapEnabled");

        [MenuItem("Tools/Dungeon/Lighting/Adjacent Lightmap PoC/Render P100 Baseline And Blend")]
        public static void RenderFromMenu()
        {
            Debug.Log(RenderP100Comparison());
        }

        public static string ReportActiveSceneState()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            return
                $"activeScene={activeScene.path}\n" +
                $"loaded={activeScene.isLoaded}\n" +
                $"dirty={activeScene.isDirty}\n" +
                $"lightmapCount={LightmapSettings.lightmaps.Length}\n" +
                $"lightmapsMode={LightmapSettings.lightmapsMode}";
        }

        public static string OpenPreviewForValidation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "FAIL: exit Play Mode before opening the generated preview.";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PreviewScenePath) == null)
                return $"FAIL: preview scene is missing: {PreviewScenePath}";

            bool hasOtherLoadedScene = false;
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene loadedScene = SceneManager.GetSceneAt(sceneIndex);
                if (loadedScene.path == PreviewScenePath)
                    continue;
                if (loadedScene.isDirty)
                {
                    return
                        $"FAIL: loaded scene has unsaved changes and was not closed: " +
                        $"{loadedScene.path} ({loadedScene.name})";
                }
                hasOtherLoadedScene = true;
            }

            Scene previewScene = SceneManager.GetSceneByPath(PreviewScenePath);
            if (hasOtherLoadedScene)
            {
                previewScene = EditorSceneManager.OpenScene(
                    PreviewScenePath,
                    OpenSceneMode.Single);
            }
            else if (!previewScene.IsValid() || !previewScene.isLoaded)
            {
                previewScene = EditorSceneManager.OpenScene(
                    PreviewScenePath,
                    OpenSceneMode.Single);
            }

            SceneManager.SetActiveScene(previewScene);
            Camera camera = FindPreviewCamera(previewScene);
            if (camera != null)
                Selection.activeGameObject = camera.gameObject;
            return
                $"PASS opened generated preview\n" +
                $"scene={previewScene.path}\n" +
                $"dirty={previewScene.isDirty}\n" +
                $"camera={(camera != null ? camera.name : "MISSING")}";
        }

        public static string RenderP100Comparison()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PreviewScenePath) == null)
                return $"FAIL: preview scene is missing: {PreviewScenePath}";

            DungeonAdjacentLightingPoCEditorState originalState =
                DungeonAdjacentLightingPoCEditorState.Capture();
            Scene previewScene = default;
            try
            {
                previewScene = EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(previewScene);

                DungeonTileLightmapSwitcher[] switchers = FindInScene<DungeonTileLightmapSwitcher>(previewScene);
                for (int i = 0; i < switchers.Length; i++)
                    InvokePrivateAwake(switchers[i]);

                DungeonAdjacentLightmapExtension[] extensions =
                    FindInScene<DungeonAdjacentLightmapExtension>(previewScene);
                for (int i = 0; i < extensions.Length; i++)
                {
                    if (extensions[i].ExtensionRenderer != null)
                        extensions[i].ExtensionRenderer.enabled = false;
                }

                DungeonAdjacentLightmapReceiver[] receivers =
                    FindInScene<DungeonAdjacentLightmapReceiver>(previewScene);
                if (receivers.Length == 0)
                    return "FAIL: no adjacent-lightmap receivers were found.";

                int totalProjected = 0;
                for (int i = 0; i < receivers.Length; i++)
                {
                    receivers[i].RebuildAndApply();
                    totalProjected += receivers[i].LastProjectedVertexCount;
                    if (receivers[i].LastProjectedVertexCount <= 0)
                        return $"FAIL: receiver '{receivers[i].name}' projected no vertices: {receivers[i].LastDiagnostic}";
                }

                Camera camera = FindPreviewCamera(previewScene);
                if (camera == null)
                    return "FAIL: preview camera is missing.";

                EnsureFolder(ScreenshotFolder);
                SetAdjacentEnabled(receivers, false);
                Color32[] baselinePixels = RenderCamera(
                    camera,
                    ScreenshotFolder + "/Start_Admin_P100_Baseline.png");

                SetAdjacentEnabled(receivers, true);
                Color32[] blendedPixels = RenderCamera(
                    camera,
                    ScreenshotFolder + "/Start_Admin_P100_Blended.png");

                DifferenceMetrics metrics = CalculateDifference(baselinePixels, blendedPixels);
                if (metrics.changedPixels == 0)
                    return "FAIL: baseline and adjacent-lightmap render are pixel-identical.";
                string report =
                    "PASS DungeonAdjacentLightingPoC P100 still render\n" +
                    $"previewScene={PreviewScenePath}\n" +
                    $"receiverCount={receivers.Length}\n" +
                    $"projectedVertices={totalProjected}\n" +
                    $"changedPixels={metrics.changedPixels}\n" +
                    $"meanAbsoluteRgbDelta={metrics.meanAbsoluteRgbDelta:F6}\n" +
                    $"maxChannelDelta={metrics.maxChannelDelta}\n" +
                    $"baseline={ScreenshotFolder}/Start_Admin_P100_Baseline.png\n" +
                    $"blended={ScreenshotFolder}/Start_Admin_P100_Blended.png\n";
                File.WriteAllText(RootFolder + "/Generated/P100_StillRenderReport.txt", report);
                AssetDatabase.ImportAsset(RootFolder + "/Generated/P100_StillRenderReport.txt");
                return report;
            }
            catch (Exception exception)
            {
                return $"FAIL: P100 still render threw {exception}";
            }
            finally
            {
                if (previewScene.IsValid() && previewScene.isLoaded)
                    EditorSceneManager.CloseScene(previewScene, true);
                originalState.Restore();
            }
        }

        private static void InvokePrivateAwake(DungeonTileLightmapSwitcher switcher)
        {
            MethodInfo method = typeof(DungeonTileLightmapSwitcher).GetMethod(
                "Awake",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
                throw new MissingMethodException(typeof(DungeonTileLightmapSwitcher).FullName, "Awake");
            method.Invoke(switcher, null);
        }

        private static T[] FindInScene<T>(Scene scene) where T : Component
        {
            var results = new System.Collections.Generic.List<T>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                results.AddRange(roots[i].GetComponentsInChildren<T>(true));
            return results.ToArray();
        }

        private static Camera FindPreviewCamera(Scene scene)
        {
            Camera[] cameras = FindInScene<Camera>(scene);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i].name == "AdjacentLightmapPreviewCamera")
                    return cameras[i];
            }
            return cameras.Length > 0 ? cameras[0] : null;
        }

        private static void SetAdjacentEnabled(
            DungeonAdjacentLightmapReceiver[] receivers,
            bool enabled)
        {
            var propertyBlock = new MaterialPropertyBlock();
            for (int i = 0; i < receivers.Length; i++)
            {
                Renderer renderer = receivers[i].GetComponent<Renderer>();
                if (renderer == null)
                    continue;
                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(AdjacentEnabledId, enabled ? 1f : 0f);
                renderer.SetPropertyBlock(propertyBlock);
                propertyBlock.Clear();
            }
        }

        private static Color32[] RenderCamera(Camera camera, string assetPath)
        {
            const int width = 1280;
            const int height = 720;
            RenderTexture renderTexture = RenderTexture.GetTemporary(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            Texture2D screenshot = null;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                screenshot = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                screenshot.Apply(false, false);

                string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
                File.WriteAllBytes(absolutePath, screenshot.EncodeToPNG());
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                return screenshot.GetPixels32();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
                if (screenshot != null)
                    UnityEngine.Object.DestroyImmediate(screenshot);
            }
        }

        private static DifferenceMetrics CalculateDifference(Color32[] first, Color32[] second)
        {
            if (first == null || second == null || first.Length != second.Length)
                throw new ArgumentException("Rendered image arrays must have matching dimensions.");

            long totalDelta = 0;
            int changedPixels = 0;
            int maxChannelDelta = 0;
            for (int i = 0; i < first.Length; i++)
            {
                int red = Math.Abs(first[i].r - second[i].r);
                int green = Math.Abs(first[i].g - second[i].g);
                int blue = Math.Abs(first[i].b - second[i].b);
                int pixelDelta = red + green + blue;
                if (pixelDelta > 0)
                    changedPixels++;
                totalDelta += pixelDelta;
                maxChannelDelta = Math.Max(maxChannelDelta, Math.Max(red, Math.Max(green, blue)));
            }

            return new DifferenceMetrics
            {
                changedPixels = changedPixels,
                meanAbsoluteRgbDelta = first.Length > 0 ? totalDelta / (first.Length * 3.0 * 255.0) : 0.0,
                maxChannelDelta = maxChannelDelta
            };
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
                return;
            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string name = Path.GetFileName(assetFolder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new InvalidOperationException($"Invalid asset folder '{assetFolder}'.");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private struct DifferenceMetrics
        {
            public int changedPixels;
            public double meanAbsoluteRgbDelta;
            public int maxChannelDelta;
        }
    }
}
