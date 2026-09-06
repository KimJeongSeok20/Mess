using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DungeonPortalTransportPoC.Diagnostics.RenderGraphFrameSpaced
{
    /// <summary>
    /// Isolated diagnostic for Unity 6 URP standard render requests. Each
    /// request is issued on a separate Editor update so a failure cannot be
    /// confused with multiple submissions in the same callback.
    /// </summary>
    public static class DungeonPortalRenderGraphFrameSpacedCanary
    {
        private const string MenuPath =
            "Tools/Dungeon/Lighting/Portal Transport PoC/Diagnostics/" +
            "Run Frame-Spaced RenderGraph Canary";
        private const string StartCameraName = "Start_to_Admin_FixedCamera_DISABLED";
        private const int Width = 128;
        private const int Height = 128;
        private const int UpdatesBetweenRenders = 2;

        private static Scene previewScene;
        private static readonly List<GameObject> ownedObjects = new List<GameObject>();
        private static Camera camera;
        private static Renderer canaryRenderer;
        private static Material unlitMaterial;
        private static Material litMaterial;
        private static Light canaryLight;
        private static RenderTexture renderTarget;
        private static RenderTexture resolveTarget;
        private static RenderTexture originalActive;
        private static bool originalSrgbWrite;
        private static int phase;
        private static int updatesRemaining;
        private static bool running;
        private static readonly List<string> renderErrors = new List<string>();
        private static readonly List<string> samples = new List<string>();

        [MenuItem(MenuPath)]
        public static void RunFromMenu()
        {
            Debug.Log(Start());
        }

        public static string Start()
        {
            if (running)
                return "RENDER_GRAPH_FRAME_SPACED_CANARY|status=ALREADY_RUNNING";
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || Lightmapping.isRunning)
            {
                return "RENDER_GRAPH_FRAME_SPACED_CANARY|status=BLOCKED|reason=editor_not_idle";
            }

            Scene sourceScene = SceneManager.GetActiveScene();
            if (!sourceScene.IsValid() || !sourceScene.isLoaded || sourceScene.isDirty)
                return "RENDER_GRAPH_FRAME_SPACED_CANARY|status=BLOCKED|reason=scene_not_clean";

            try
            {
                Setup(sourceScene);
                running = true;
                phase = 0;
                updatesRemaining = UpdatesBetweenRenders;
                renderErrors.Clear();
                samples.Clear();
                Application.logMessageReceived += OnLogMessage;
                EditorApplication.update += Tick;
                AssemblyReloadEvents.beforeAssemblyReload += CleanupForReload;
                return "RENDER_GRAPH_FRAME_SPACED_CANARY|status=STARTED|scene=" +
                       sourceScene.path;
            }
            catch (Exception exception)
            {
                Cleanup();
                return "RENDER_GRAPH_FRAME_SPACED_CANARY|status=FAIL_SETUP|exception=" +
                       exception;
            }
        }

        private static void Setup(Scene sourceScene)
        {
            Camera sourceCamera = Resources.FindObjectsOfTypeAll<Camera>()
                .Single(candidate =>
                    candidate != null && candidate.gameObject.scene == sourceScene &&
                    string.Equals(candidate.name, StartCameraName, StringComparison.Ordinal));

            originalActive = RenderTexture.active;
            originalSrgbWrite = GL.sRGBWrite;
            previewScene = EditorSceneManager.NewPreviewScene();
            if (!previewScene.IsValid() || !previewScene.isLoaded ||
                !EditorSceneManager.IsPreviewScene(previewScene))
            {
                throw new InvalidOperationException("Unable to create preview scene.");
            }

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ownedObjects.Add(cube);
            cube.name = "__FrameSpacedCanaryCube";
            SceneManager.MoveGameObjectToScene(cube, previewScene);
            cube.transform.position = sourceCamera.transform.position +
                                      sourceCamera.transform.forward * 3f;
            cube.transform.localScale = Vector3.one * 1.5f;
            canaryRenderer = cube.GetComponent<Renderer>();
            canaryRenderer.renderingLayerMask = 2u;

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (unlitShader == null || litShader == null)
                throw new InvalidOperationException("Required URP canary shader is missing.");
            unlitMaterial = new Material(unlitShader) { hideFlags = HideFlags.HideAndDontSave };
            unlitMaterial.SetColor("_BaseColor", Color.red);
            litMaterial = new Material(litShader) { hideFlags = HideFlags.HideAndDontSave };
            litMaterial.SetColor("_BaseColor", Color.white);
            canaryRenderer.sharedMaterial = unlitMaterial;

            GameObject cameraObject = UnityEngine.Object.Instantiate(sourceCamera.gameObject);
            ownedObjects.Add(cameraObject);
            cameraObject.name = "__FrameSpacedCanaryCamera";
            cameraObject.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
            camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.scene = previewScene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.blue;
            camera.allowHDR = true;
            camera.targetTexture = null;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            GameObject lightObject = new GameObject("__FrameSpacedCanaryLight");
            ownedObjects.Add(lightObject);
            SceneManager.MoveGameObjectToScene(lightObject, previewScene);
            lightObject.transform.position = cube.transform.position -
                                             sourceCamera.transform.forward * 2f;
            lightObject.transform.rotation = sourceCamera.transform.rotation;
            canaryLight = lightObject.AddComponent<Light>();
            canaryLight.type = LightType.Spot;
            canaryLight.range = 10f;
            canaryLight.spotAngle = 60f;
            canaryLight.innerSpotAngle = 40f;
            canaryLight.color = Color.white;
            canaryLight.intensity = 1f;
            canaryLight.shadows = LightShadows.None;
            canaryLight.cullingMask = -1;
            canaryLight.renderingLayerMask = 2;
            canaryLight.lightmapBakeType = LightmapBakeType.Realtime;
            UniversalAdditionalLightData additional =
                canaryLight.GetUniversalAdditionalLightData();
            additional.renderingLayers = 2u;
            additional.shadowRenderingLayers = 2u;
            canaryLight.enabled = false;

            renderTarget = CreateTarget("FrameSpacedCanary_Render", 24);
            resolveTarget = CreateTarget("FrameSpacedCanary_Resolve", 0);
        }

        private static RenderTexture CreateTarget(string name, int depth)
        {
            var target = new RenderTexture(
                Width,
                Height,
                depth,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            if (!target.Create())
            {
                UnityEngine.Object.DestroyImmediate(target);
                throw new InvalidOperationException("Unable to create canary render target.");
            }
            return target;
        }

        private static void Tick()
        {
            if (!running)
                return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                Lightmapping.isRunning)
            {
                Finish(false, "editor_state_changed");
                return;
            }
            if (--updatesRemaining > 0)
                return;

            try
            {
                switch (phase)
                {
                    case 0:
                        canaryRenderer.sharedMaterial = unlitMaterial;
                        canaryLight.enabled = false;
                        samples.Add("unlit=" + CaptureSample());
                        break;
                    case 1:
                        canaryRenderer.sharedMaterial = litMaterial;
                        canaryLight.cookie = null;
                        canaryLight.intensity = 1f;
                        canaryLight.enabled = true;
                        samples.Add("litNoCookie=" + CaptureSample());
                        break;
                    case 2:
                        canaryLight.cookie = AssetDatabase.LoadAssetAtPath<Texture>(
                            "Assets/Experiments/DungeonPortalTransportPoC/Generated/" +
                            "EndpointProfiles/StartRoom_R000_PortalCookie.asset");
                        canaryLight.intensity = 0.521972656f;
                        samples.Add("litStartCookie=" + CaptureSample());
                        Finish(renderErrors.Count == 0, renderErrors.Count == 0
                            ? "complete"
                            : "render_error_logged");
                        return;
                    default:
                        Finish(false, "invalid_phase");
                        return;
                }

                phase++;
                updatesRemaining = UpdatesBetweenRenders;
            }
            catch (Exception exception)
            {
                renderErrors.Add(exception.GetType().Name + ": " + exception.Message);
                Finish(false, "exception");
            }
        }

        private static string CaptureSample()
        {
            renderTarget.DiscardContents();
            var request = new RenderPipeline.StandardRequest
            {
                destination = renderTarget,
                mipLevel = 0,
                slice = 0,
                face = CubemapFace.Unknown
            };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new InvalidOperationException(
                    "Active render pipeline does not support StandardRequest.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            GL.sRGBWrite = false;
            Graphics.Blit(renderTarget, resolveTarget);

            Texture2D readback = null;
            try
            {
                RenderTexture.active = resolveTarget;
                readback = new Texture2D(
                    Width,
                    Height,
                    TextureFormat.RGBAHalf,
                    false,
                    true)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                readback.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0, false);
                readback.Apply(false, false);
                Color[] pixels = readback.GetPixels();
                double sum = 0d;
                float maximum = 0f;
                for (int i = 0; i < pixels.Length; i++)
                {
                    float luminance = 0.2126f * pixels[i].r +
                                      0.7152f * pixels[i].g +
                                      0.0722f * pixels[i].b;
                    sum += luminance;
                    maximum = Mathf.Max(maximum, luminance);
                }
                double mean = sum / pixels.Length;
                if (mean <= 0d || maximum <= 0f)
                    throw new InvalidOperationException(
                        "StandardRequest returned a zero-luminance frame.");
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "mean={0:R},max={1:R}",
                    mean,
                    maximum);
            }
            finally
            {
                if (readback != null)
                    UnityEngine.Object.DestroyImmediate(readback);
                camera.targetTexture = null;
                RenderTexture.active = originalActive;
                GL.sRGBWrite = originalSrgbWrite;
            }
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (!running || (type != LogType.Error && type != LogType.Exception &&
                             type != LogType.Assert))
                return;
            if ((condition != null && condition.IndexOf("Render Graph",
                    StringComparison.OrdinalIgnoreCase) >= 0) ||
                (condition != null && condition.IndexOf("ZBinningJob",
                    StringComparison.Ordinal) >= 0) ||
                (stackTrace != null && stackTrace.IndexOf(
                    nameof(DungeonPortalRenderGraphFrameSpacedCanary),
                    StringComparison.Ordinal) >= 0))
            {
                renderErrors.Add((condition ?? "<no condition>").Replace('\n', ' '));
            }
        }

        private static void Finish(bool success, string reason)
        {
            string sampleText = string.Join("|", samples);
            string errorText = renderErrors.Count == 0
                ? "none"
                : string.Join(" || ", renderErrors);
            Cleanup();
            Debug.Log(
                "RENDER_GRAPH_FRAME_SPACED_CANARY|status=" +
                (success ? "PASS" : "FAIL") + "|reason=" + reason + "|" +
                sampleText + "|errors=" + errorText);
        }

        private static void CleanupForReload()
        {
            Cleanup();
        }

        private static void Cleanup()
        {
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= CleanupForReload;
            Application.logMessageReceived -= OnLogMessage;

            RenderTexture.active = originalActive;
            GL.sRGBWrite = originalSrgbWrite;
            for (int i = ownedObjects.Count - 1; i >= 0; i--)
            {
                if (ownedObjects[i] != null)
                    UnityEngine.Object.DestroyImmediate(ownedObjects[i]);
            }
            ownedObjects.Clear();
            DestroyTarget(renderTarget);
            DestroyTarget(resolveTarget);
            renderTarget = null;
            resolveTarget = null;
            if (unlitMaterial != null)
                UnityEngine.Object.DestroyImmediate(unlitMaterial);
            if (litMaterial != null)
                UnityEngine.Object.DestroyImmediate(litMaterial);
            unlitMaterial = null;
            litMaterial = null;
            camera = null;
            canaryRenderer = null;
            canaryLight = null;
            if (previewScene.IsValid() && previewScene.isLoaded)
                EditorSceneManager.ClosePreviewScene(previewScene);
            previewScene = default;
            running = false;
        }

        private static void DestroyTarget(RenderTexture target)
        {
            if (target == null)
                return;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
