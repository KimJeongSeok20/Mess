using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public static class SmilyBlendPreviewExporter
{
    private const string PrefabPath = "Assets/Monster/smily/smily-horror-monster_cc_attribution/smily_fixed.prefab";
    private const string ControllerPath = "Assets/Monster/smily/smily-horror-monster_cc_attribution/smily_animator.controller";
    private const string OutputDirectory = "Assets/Monster/smily/smily-horror-monster_cc_attribution/DebugPreviews";

    private static readonly float[] DefaultSpeeds = { 0f, 1f };
    private static readonly float[] DefaultNormalizedTimes = { 0f, 0.33f, 0.66f, 0.99f };

    private const int CellWidth = 512;
    private const int CellHeight = 512;
    private const int PreviewLayer = 31;
    private const float CameraFramePadding = 1.04f;

    private static readonly ViewDefinition DefaultThreeQuarterView = new ViewDefinition("ThreeQuarter", new Vector3(1.15f, 0.12f, -2.15f));
    private static readonly ViewDefinition[] DefaultViews =
    {
        new ViewDefinition("Front", new Vector3(0f, 0.08f, -1f)),
        new ViewDefinition("Side", new Vector3(1f, 0.08f, 0f)),
        new ViewDefinition("Back", new Vector3(0f, 0.08f, 1f)),
        DefaultThreeQuarterView
    };

    [MenuItem("Tools/SMILY/Export Speed Compare Images")]
    public static void ExportDefaultComparisonMenu()
    {
        var outputPath = ExportDefaultComparison();
        Debug.Log($"[SmilyBlendPreviewExporter] Exported comparison image to '{outputPath}'. Rows are SpeedMagnitude 0 then 1. Columns are normalized times 0, 0.33, 0.66, 0.99.");
    }

    [MenuItem("Tools/SMILY/Export Multi-Angle Speed Compare Images")]
    public static void ExportDefaultMultiAngleComparisonMenu()
    {
        var outputPaths = ExportDefaultMultiAngleComparison();
        Debug.Log($"[SmilyBlendPreviewExporter] Exported {outputPaths.Length} multi-angle comparison images: {string.Join(", ", outputPaths)}");
    }

    public static string ExportDefaultComparison()
    {
        return ExportComparison(DefaultSpeeds, DefaultNormalizedTimes, "SMILY_FIXED_SpeedCompare_0_1.png", DefaultThreeQuarterView);
    }

    public static string[] ExportDefaultMultiAngleComparison()
    {
        return ExportMultiAngleComparison(DefaultSpeeds, DefaultNormalizedTimes, "SMILY_FIXED_SpeedCompare_0_1");
    }

    public static string[] ExportMultiAngleComparison(float[] speeds, float[] normalizedTimes, string fileNamePrefix)
    {
        var outputPaths = new string[DefaultViews.Length];
        for (var i = 0; i < DefaultViews.Length; i++)
        {
            var view = DefaultViews[i];
            outputPaths[i] = ExportComparison(speeds, normalizedTimes, $"{fileNamePrefix}_{view.Name}.png", view);
        }

        return outputPaths;
    }

    public static string ExportComparison(float[] speeds, float[] normalizedTimes, string fileName, ViewDefinition view)
    {
        if (speeds == null || speeds.Length == 0)
            throw new ArgumentException("At least one speed is required.", nameof(speeds));

        if (normalizedTimes == null || normalizedTimes.Length == 0)
            throw new ArgumentException("At least one normalized time is required.", nameof(normalizedTimes));

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException($"Missing prefab at '{PrefabPath}'.");

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            throw new InvalidOperationException($"Missing controller at '{ControllerPath}'.");

        EnsureDirectoryExists(OutputDirectory);

        Camera previewCamera = null;
        Light keyLight = null;
        Light fillLight = null;
        RenderTexture renderTexture = null;
        Texture2D sheet = null;

        try
        {
            previewCamera = CreatePreviewCamera();
            keyLight = CreatePreviewLight(new Vector3(35f, -140f, 0f), 1.2f);
            fillLight = CreatePreviewLight(new Vector3(340f, 35f, 0f), 0.45f);

            var combinedBounds = CalculateCombinedBounds(prefab, controller, speeds, normalizedTimes);
            ConfigureCamera(previewCamera, combinedBounds, view.Direction);

            renderTexture = new RenderTexture(CellWidth, CellHeight, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };

            sheet = new Texture2D(CellWidth * normalizedTimes.Length, CellHeight * speeds.Length, TextureFormat.RGBA32, false);

            for (var speedIndex = 0; speedIndex < speeds.Length; speedIndex++)
            {
                for (var timeIndex = 0; timeIndex < normalizedTimes.Length; timeIndex++)
                {
                    var instance = InstantiatePreviewPrefab(prefab);
                    try
                    {
                        ApplyControllerPose(instance, controller, speeds[speedIndex], normalizedTimes[timeIndex]);
                        var pixels = RenderInstance(previewCamera, renderTexture, instance);
                        var destX = timeIndex * CellWidth;
                        var destY = (speeds.Length - 1 - speedIndex) * CellHeight;
                        sheet.SetPixels(destX, destY, CellWidth, CellHeight, pixels);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }
            }

            sheet.Apply();

            var outputPath = Path.Combine(OutputDirectory, fileName).Replace('\\', '/');
            File.WriteAllBytes(outputPath, sheet.EncodeToPNG());
            AssetDatabase.Refresh();
            return outputPath;
        }
        finally
        {
            if (sheet != null)
                UnityEngine.Object.DestroyImmediate(sheet);

            if (renderTexture != null)
                UnityEngine.Object.DestroyImmediate(renderTexture);

            if (keyLight != null)
                UnityEngine.Object.DestroyImmediate(keyLight.gameObject);

            if (fillLight != null)
                UnityEngine.Object.DestroyImmediate(fillLight.gameObject);

            if (previewCamera != null)
                UnityEngine.Object.DestroyImmediate(previewCamera.gameObject);
        }
    }

    private static void EnsureDirectoryExists(string assetDirectory)
    {
        var fullPath = Path.GetFullPath(assetDirectory);
        if (!Directory.Exists(fullPath))
            Directory.CreateDirectory(fullPath);
    }

    private static Camera CreatePreviewCamera()
    {
        var cameraObject = new GameObject("[SMILY Preview Camera]");
        cameraObject.hideFlags = HideFlags.HideAndDontSave;
        cameraObject.layer = PreviewLayer;

        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
        camera.fieldOfView = 27f;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 100f;
        camera.allowHDR = false;
        camera.allowMSAA = true;
        camera.cullingMask = 1 << PreviewLayer;
        return camera;
    }

    private static Light CreatePreviewLight(Vector3 eulerAngles, float intensity)
    {
        var lightObject = new GameObject("[SMILY Preview Light]");
        lightObject.hideFlags = HideFlags.HideAndDontSave;
        lightObject.layer = PreviewLayer;

        lightObject.transform.rotation = Quaternion.Euler(eulerAngles);

        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = Color.white;
        light.cullingMask = 1 << PreviewLayer;
        return light;
    }

    private static Bounds CalculateCombinedBounds(GameObject prefab, AnimatorController controller, float[] speeds, float[] normalizedTimes)
    {
        var hasBounds = false;
        var combined = new Bounds(Vector3.zero, Vector3.one);

        for (var speedIndex = 0; speedIndex < speeds.Length; speedIndex++)
        {
            for (var timeIndex = 0; timeIndex < normalizedTimes.Length; timeIndex++)
            {
                var instance = InstantiatePreviewPrefab(prefab);
                try
                {
                    ApplyControllerPose(instance, controller, speeds[speedIndex], normalizedTimes[timeIndex]);
                    var instanceBounds = CalculateRendererBounds(instance);
                    if (!hasBounds)
                    {
                        combined = instanceBounds;
                        hasBounds = true;
                    }
                    else
                    {
                        combined.Encapsulate(instanceBounds.min);
                        combined.Encapsulate(instanceBounds.max);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        if (!hasBounds)
            throw new InvalidOperationException("Could not calculate preview bounds for SMILY_FIXED.");

        return combined;
    }

    private static GameObject InstantiatePreviewPrefab(GameObject prefab)
    {
        var instance = UnityEngine.Object.Instantiate(prefab);
        if (instance == null)
            throw new InvalidOperationException("Failed to instantiate SMILY_FIXED preview prefab.");

        instance.hideFlags = HideFlags.HideAndDontSave;
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        SetLayerRecursively(instance.transform, PreviewLayer);
        SetHideFlagsRecursively(instance.transform, HideFlags.HideAndDontSave);
        return instance;
    }

    private static void ApplyControllerPose(GameObject instance, AnimatorController controller, float speedMagnitude, float normalizedTime)
    {
        var animator = instance.GetComponent<Animator>();
        if (animator == null)
            throw new InvalidOperationException("SMILY_FIXED preview instance is missing Animator.");

        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        animator.Update(0f);

        var graph = PlayableGraph.Create("SMILY_FIXED Preview Graph");
        try
        {
            var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            var playable = AnimatorControllerPlayable.Create(graph, controller);
            output.SetSourcePlayable(playable);

            playable.SetFloat("SpeedMagnitude", speedMagnitude);
            graph.Play();
            graph.Evaluate(GetSampleTime(controller, speedMagnitude, normalizedTime));
        }
        finally
        {
            graph.Destroy();
        }
    }

    private static float GetSampleTime(AnimatorController controller, float speedMagnitude, float normalizedTime)
    {
        normalizedTime = Mathf.Clamp01(normalizedTime);

        var blendTree = AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>().FirstOrDefault();
        if (blendTree == null || blendTree.children.Length < 2)
            throw new InvalidOperationException("Could not resolve SMILY blend tree children for preview sampling.");

        var idleDuration = GetMotionDuration(blendTree.children[0]);
        var moveDuration = GetMotionDuration(blendTree.children[1]);

        if (speedMagnitude <= 0f)
            return idleDuration * normalizedTime;

        if (speedMagnitude >= 1f)
            return moveDuration * normalizedTime;

        return Mathf.Lerp(idleDuration, moveDuration, speedMagnitude) * normalizedTime;
    }

    private static float GetMotionDuration(ChildMotion childMotion)
    {
        if (childMotion.motion == null)
            return 0f;

        var duration = childMotion.motion.averageDuration;
        var timeScale = Mathf.Approximately(childMotion.timeScale, 0f) ? 1f : childMotion.timeScale;
        return duration / timeScale;
    }

    private static Bounds CalculateRendererBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            throw new InvalidOperationException("SMILY_FIXED preview instance has no renderers.");

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (var i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    private static void SetHideFlagsRecursively(Transform root, HideFlags hideFlags)
    {
        root.gameObject.hideFlags = hideFlags;
        for (var i = 0; i < root.childCount; i++)
            SetHideFlagsRecursively(root.GetChild(i), hideFlags);
    }

    private static void ConfigureCamera(Camera camera, Bounds bounds, Vector3 viewDirection)
    {
        var target = bounds.center + new Vector3(0f, bounds.extents.y * 0.08f, 0f);
        var direction = viewDirection.normalized;
        var rotation = Quaternion.LookRotation(direction, Vector3.up);
        var right = rotation * Vector3.right;
        var up = rotation * Vector3.up;
        var verticalHalfFov = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        var horizontalHalfFov = Mathf.Atan(Mathf.Tan(verticalHalfFov) * ((float)CellWidth / CellHeight));
        var corners = GetBoundsCorners(bounds);
        var maxHorizontal = 0f;
        var maxVertical = 0f;

        for (var i = 0; i < corners.Length; i++)
        {
            var offset = corners[i] - target;
            maxHorizontal = Mathf.Max(maxHorizontal, Mathf.Abs(Vector3.Dot(offset, right)));
            maxVertical = Mathf.Max(maxVertical, Mathf.Abs(Vector3.Dot(offset, up)));
        }

        var distanceForHeight = maxVertical / Mathf.Tan(verticalHalfFov);
        var distanceForWidth = maxHorizontal / Mathf.Tan(horizontalHalfFov);
        var distance = Mathf.Max(distanceForHeight, distanceForWidth) * CameraFramePadding;

        camera.transform.position = target - direction * distance;
        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
    }

    private static Vector3[] GetBoundsCorners(Bounds bounds)
    {
        var min = bounds.min;
        var max = bounds.max;

        return new[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, max.y, max.z)
        };
    }

    private static Color[] RenderInstance(Camera camera, RenderTexture renderTexture, GameObject instance)
    {
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;

        try
        {
            camera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;
            camera.Render();

            var texture = new Texture2D(CellWidth, CellHeight, TextureFormat.RGBA32, false);
            try
            {
                texture.ReadPixels(new Rect(0f, 0f, CellWidth, CellHeight), 0, 0);
                texture.Apply();
                return texture.GetPixels();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
        }
    }

    public readonly struct ViewDefinition
    {
        public ViewDefinition(string name, Vector3 direction)
        {
            Name = name;
            Direction = direction;
        }

        public string Name { get; }
        public Vector3 Direction { get; }
    }
}
