using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 아이템 프리팹을 일괄 렌더해 규격화된 인벤토리 아이콘을 굽는다.
///
/// 모든 아이콘이 같은 카메라 각도·같은 여백·같은 조명·투명 배경으로 나오므로
/// 그리드에 늘어놓았을 때 크기와 방향이 어긋나 보이지 않는다.
/// 결과는 <see cref="IconFolder"/>에 PNG로 저장하고 ItemDefinition.icon에 연결한다.
///
/// 메뉴: Tools/UI/Inventory: Bake Item Icons
/// </summary>
public static class ItemIconBaker
{
    public const string IconFolder = "Assets/UI/Inventory/Icons";
    private const string ScenePath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";

    // ── 규격 (모든 아이콘이 공유하는 값) ──────────────────────────
    private const int IconSize = 256;

    /// <summary>3/4 부감. 총기처럼 긴 물체도 실루엣이 살아난다.</summary>
    private static readonly Vector3 CameraEuler = new Vector3(22f, 135f, 0f);

    /// <summary>1.0이면 바운딩 구가 화면에 꽉 찬다. 여백을 위해 조금 키운다.</summary>
    private const float FramePadding = 1.22f;

    private const float KeyLightIntensity = 1.6f;
    private const float FillLightIntensity = 0.85f;
    private const float RimLightIntensity = 0.7f;
    private static readonly Vector3 KeyLightEuler = new Vector3(38f, 155f, 0f);
    private static readonly Vector3 FillLightEuler = new Vector3(18f, -30f, 0f);
    private static readonly Vector3 RimLightEuler = new Vector3(-15f, 40f, 0f);

    [MenuItem("Tools/UI/Inventory: Bake Item Icons")]
    public static void BakeRegisteredItems()
    {
        if (!EditorUtility.DisplayDialog(
                "Bake Item Icons",
                $"StartMap에 등록된 아이템 프리팹을 렌더해 {IconFolder} 에 아이콘을 굽습니다.\n\n"
                + "같은 이름의 기존 아이콘은 덮어씁니다. 계속할까요?",
                "굽기",
                "취소"))
        {
            return;
        }

        BakeAll();
    }

    /// <summary>확인 창 없이 굽는다. 자동화/스크립트에서 호출하는 진입점.</summary>
    public static void BakeAll()
    {
        Directory.CreateDirectory(IconFolder);

        List<Item> items = CollectRegisteredItems();
        if (items.Count == 0)
        {
            Debug.LogWarning("[ItemIconBaker] No items found in StartMap allItems.");
            return;
        }

        int baked = 0;
        int skipped = 0;

        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                string label = item != null ? item.ItemName : "?";
                EditorUtility.DisplayProgressBar("Baking item icons", $"{label} ({i + 1}/{items.Count})",
                    (float)i / items.Count);

                if (BakeIcon(item))
                    baked++;
                else
                    skipped++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        int linked = LinkIconsToDefinitions(items);
        AssetDatabase.SaveAssets();

        Debug.Log($"[ItemIconBaker] baked={baked} skipped={skipped} linkedToDefinitions={linked}");
    }

    // ─────────────────────────────────────────────────────────────

    private static List<Item> CollectRegisteredItems()
    {
        var result = new List<Item>();
        var seen = new HashSet<Object>();

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            InventoryManager manager = FindManager(scene);
            if (manager == null)
                return result;

            var serialized = new SerializedObject(manager);
            SerializedProperty allItems = serialized.FindProperty("allItems");

            for (int i = 0; i < allItems.arraySize; i++)
            {
                var item = allItems.GetArrayElementAtIndex(i).objectReferenceValue as Item;
                if (item == null || !seen.Add(item))
                    continue;

                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(item)))
                    continue;

                result.Add(item);
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }

        return result;
    }

    /// <summary>
    /// 프리뷰 씬에 실제 카메라와 조명을 세워 렌더한다.
    /// PreviewRenderUtility의 내장 조명은 URP에서 메인 라이트로 잡히지 않아
    /// 모델이 검은 실루엣으로 찍히기 때문에 이 방식을 쓴다.
    /// </summary>
    private static bool BakeIcon(Item item)
    {
        if (item == null)
            return false;

        GameObject prefab = item.gameObject;
        Scene previewScene = EditorSceneManager.NewPreviewScene();
        GameObject instance = null;
        RenderTexture target = null;
        Texture2D rendered = null;

        try
        {
            instance = Object.Instantiate(prefab);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = prefab.transform.localScale;
            SceneManager.MoveGameObjectToScene(instance, previewScene);

            StripNonVisualComponents(instance);

            if (!TryGetRenderBounds(instance, out Bounds bounds))
            {
                Debug.LogWarning($"[ItemIconBaker] {item.ItemName}: no renderer bounds, skipped.", prefab);
                return false;
            }

            Camera camera = CreateCamera(previewScene, bounds);
            CreateLights(previewScene);
            Material backdrop = CreateBackdrop(previewScene, camera, bounds);

            target = RenderTexture.GetTemporary(IconSize, IconSize, 24, RenderTextureFormat.ARGB32);
            target.antiAliasing = 8;
            camera.targetTexture = target;

            // URP는 불투명 지오메트리에 알파를 쓰지 않고, 카메라 배경색도 스카이박스에 덮인다.
            // 그래서 물체 뒤에 단색 백드롭을 세우고 검정/흰색으로 두 번 렌더해
            // 두 장의 차이에서 커버리지(알파)를 복원한다.
            Texture2D onBlack = RenderWithBackground(camera, backdrop, target, Color.black);
            Texture2D onWhite = RenderWithBackground(camera, backdrop, target, Color.white);
            camera.targetTexture = null;

            rendered = ComposeAlpha(onBlack, onWhite);
            Object.DestroyImmediate(onBlack);
            Object.DestroyImmediate(onWhite);

            string path = $"{IconFolder}/{Sanitize(item.ItemName)}.png";
            File.WriteAllBytes(path, rendered.EncodeToPNG());
            return true;
        }
        finally
        {
            if (rendered != null)
                Object.DestroyImmediate(rendered);
            if (target != null)
                RenderTexture.ReleaseTemporary(target);

            EditorSceneManager.ClosePreviewScene(previewScene);
        }
    }

    /// <summary>
    /// 카메라 뒤쪽에 프레임을 가득 채우는 단색 판을 세운다.
    /// URP에서 카메라 clearFlags가 스카이박스에 덮이는 경우가 있어, 배경을 지오메트리로 보장한다.
    /// </summary>
    private static Material CreateBackdrop(Scene previewScene, Camera camera, Bounds bounds)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "IconBackdrop";
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        SceneManager.MoveGameObjectToScene(quad, previewScene);

        float radius = Mathf.Max(0.01f, bounds.extents.magnitude);
        Transform cameraTransform = camera.transform;
        quad.transform.position = bounds.center + (cameraTransform.forward * radius * 2.5f);
        quad.transform.rotation = cameraTransform.rotation;
        quad.transform.localScale = Vector3.one * (camera.orthographicSize * 4f);

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        quad.GetComponent<Renderer>().sharedMaterial = material;
        return material;
    }

    private static void SetBackdropColor(Material backdrop, Color color)
    {
        if (backdrop == null)
            return;

        if (backdrop.HasProperty("_BaseColor"))
            backdrop.SetColor("_BaseColor", color);
        if (backdrop.HasProperty("_Color"))
            backdrop.SetColor("_Color", color);
    }

    private static Texture2D RenderWithBackground(Camera camera, Material backdrop, RenderTexture target, Color background)
    {
        camera.backgroundColor = background;
        SetBackdropColor(backdrop, background);
        camera.Render();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;

        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
        texture.Apply();

        RenderTexture.active = previous;
        return texture;
    }

    /// <summary>
    /// 같은 장면을 검정/흰색 배경에 올린 두 장에서 알파를 복원한다.
    /// 완전 불투명 화소는 두 장이 같으므로 alpha=1, 배경은 차이가 1이므로 alpha=0이 된다.
    /// 반투명·안티에일리어싱 경계도 중간값으로 정확히 복원된다.
    /// </summary>
    private static Texture2D ComposeAlpha(Texture2D onBlack, Texture2D onWhite)
    {
        Color[] black = onBlack.GetPixels();
        Color[] white = onWhite.GetPixels();
        var result = new Color[black.Length];

        for (int i = 0; i < black.Length; i++)
        {
            float difference = ((white[i].r - black[i].r)
                + (white[i].g - black[i].g)
                + (white[i].b - black[i].b)) / 3f;
            float alpha = Mathf.Clamp01(1f - difference);

            if (alpha <= 0.003f)
            {
                result[i] = new Color(0f, 0f, 0f, 0f);
                continue;
            }

            // 검정 배경 위 결과는 미리 곱해진 색이므로 알파로 나눠 원래 색을 되돌린다.
            Color straight = black[i] / alpha;
            result[i] = new Color(
                Mathf.Clamp01(straight.r),
                Mathf.Clamp01(straight.g),
                Mathf.Clamp01(straight.b),
                alpha);
        }

        var composed = new Texture2D(onBlack.width, onBlack.height, TextureFormat.RGBA32, false);
        composed.SetPixels(result);
        composed.Apply();
        return composed;
    }

    private static Camera CreateCamera(Scene previewScene, Bounds bounds)
    {
        var cameraObject = new GameObject("IconCamera");
        SceneManager.MoveGameObjectToScene(cameraObject, previewScene);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.scene = previewScene;
        camera.enabled = false;

        float radius = Mathf.Max(0.01f, bounds.extents.magnitude);
        Quaternion rotation = Quaternion.Euler(CameraEuler);
        float distance = radius * 4f;

        camera.transform.SetPositionAndRotation(
            bounds.center - (rotation * Vector3.forward * distance),
            rotation);

        camera.orthographic = true;

        // 바운딩 "구"가 아니라 카메라 공간에 투영한 바운딩 박스로 맞춘다.
        // 구 기준으로 하면 소총처럼 대각선이 긴 물체가 프레임 안에서 작게 찌그러진다.
        camera.orthographicSize = ResolveOrthographicSize(camera.transform, bounds) * FramePadding;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = distance + radius * 4f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.allowHDR = false;
        camera.allowMSAA = true;
        camera.cullingMask = ~0;

        return camera;
    }

    /// <summary>
    /// 바운딩 박스 8개 꼭짓점을 카메라 로컬 공간으로 옮겨 실제 화면 점유 폭/높이를 구한다.
    /// 정사각형 아이콘이므로 둘 중 큰 값이 직교 크기가 된다.
    /// </summary>
    private static float ResolveOrthographicSize(Transform cameraTransform, Bounds bounds)
    {
        Vector3 extents = bounds.extents;
        float halfWidth = 0f;
        float halfHeight = 0f;

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? -extents.x : extents.x,
                (i & 2) == 0 ? -extents.y : extents.y,
                (i & 4) == 0 ? -extents.z : extents.z);

            Vector3 local = cameraTransform.InverseTransformVector(corner);
            halfWidth = Mathf.Max(halfWidth, Mathf.Abs(local.x));
            halfHeight = Mathf.Max(halfHeight, Mathf.Abs(local.y));
        }

        return Mathf.Max(0.01f, Mathf.Max(halfWidth, halfHeight));
    }

    private static void CreateLights(Scene previewScene)
    {
        CreateLight(previewScene, "KeyLight", KeyLightEuler, KeyLightIntensity, Color.white);
        CreateLight(previewScene, "FillLight", FillLightEuler, FillLightIntensity, new Color(0.82f, 0.87f, 1f));
        CreateLight(previewScene, "RimLight", RimLightEuler, RimLightIntensity, new Color(0.75f, 0.82f, 0.95f));
    }

    private static void CreateLight(Scene previewScene, string name, Vector3 euler, float intensity, Color color)
    {
        var lightObject = new GameObject(name);
        SceneManager.MoveGameObjectToScene(lightObject, previewScene);

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
        light.shadows = LightShadows.None;
        lightObject.transform.rotation = Quaternion.Euler(euler);
    }

    /// <summary>
    /// 아이콘에 나오면 안 되는 것들을 제거한다.
    /// 파티클/트레일은 프레임 0에 엉뚱하게 찍히고, UI Canvas는 3D 프레이밍을 망친다.
    /// </summary>
    private static void StripNonVisualComponents(GameObject instance)
    {
        foreach (Canvas canvas in instance.GetComponentsInChildren<Canvas>(true))
            canvas.gameObject.SetActive(false);

        foreach (ParticleSystem particle in instance.GetComponentsInChildren<ParticleSystem>(true))
            particle.gameObject.SetActive(false);

        foreach (TrailRenderer trail in instance.GetComponentsInChildren<TrailRenderer>(true))
            trail.enabled = false;

        foreach (Light light in instance.GetComponentsInChildren<Light>(true))
            light.enabled = false;
    }

    private static bool TryGetRenderBounds(GameObject instance, out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
                continue;
            }

            bounds.Encapsulate(renderer.bounds);
        }

        return found;
    }

    private static int LinkIconsToDefinitions(List<Item> items)
    {
        int linked = 0;

        foreach (Item item in items)
        {
            if (item == null)
                continue;

            string path = $"{IconFolder}/{Sanitize(item.ItemName)}.png";
            ApplySpriteImportSettings(path);

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                continue;

            ItemDefinition definition = item.Definition;
            if (definition == null)
                continue;

            if (definition.icon == sprite)
                continue;

            definition.icon = sprite;
            EditorUtility.SetDirty(definition);
            linked++;
        }

        return linked;
    }

    private static void ApplySpriteImportSettings(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return;

        bool dirty = false;

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            dirty = true;
        }

        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            dirty = true;
        }

        if (!importer.alphaIsTransparency)
        {
            importer.alphaIsTransparency = true;
            dirty = true;
        }

        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            dirty = true;
        }

        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            dirty = true;
        }

        if (dirty)
            importer.SaveAndReimport();
    }

    private static InventoryManager FindManager(Scene scene)
    {
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            var manager = rootObject.GetComponentInChildren<InventoryManager>(true);
            if (manager != null)
                return manager;
        }

        return null;
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Item";

        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value;
    }
}
