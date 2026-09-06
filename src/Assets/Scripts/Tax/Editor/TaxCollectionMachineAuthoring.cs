using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds or updates only the tax-machine prefab and its generated materials.
/// It reads StartMap solely to copy an existing interaction layer; it never creates,
/// updates, removes, dirties, or saves a scene instance.
/// </summary>
public static class TaxCollectionMachineAuthoring
{
    public const string StartMapScenePath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
    public const string AssetRoot = "Assets/MyAsset/TaxMachine/CyberpunkATMFromStray";
    public const string ModelPath = AssetRoot + "/Source/model.obj";
    public const string ScreenFolder = AssetRoot + "/Screen";
    public const string ScreenMeshPath = ScreenFolder + "/TaxMachine_DisplaySurface.obj";
    public const string LowerScreenMeshPath = ScreenFolder + "/TaxMachine_LowerDisplayCover.obj";
    public const string MaterialsFolder = ScreenFolder + "/Materials";
    public const string PrefabsFolder = AssetRoot + "/Prefabs";
    public const string PrefabPath = PrefabsFolder + "/TaxCollectionMachine.prefab";
    public const string PrefabRootName = "Tax Collection Machine";

    // The OBJ is about 50 x 72 x 13 source units; this produces a human-sized ATM.
    private const float ImportedModelLocalScale = 0.035f;
    private const float InteractionDistance = 4f;

    // Verified from the imported OBJ: Box004 is the only amarillo/UDIM 1001 group.
    // Every other MeshRenderer was rosa/UDIM 1002. Keep these names together so a future
    // source re-export can be remapped in one obvious location.
    private const string Ud1001RendererName = "Box004";
    private const int Ud1001Tile = 1001;
    private const int Ud1002Tile = 1002;

    private const float FrontZ = -0.34f;

    private const string Days2ScreenTexturePath = ScreenFolder + "/TaxMachine_Screen_Days2.png";
    private const string Days1ScreenTexturePath = ScreenFolder + "/TaxMachine_Screen_Days1.png";
    private const string PayScreenTexturePath = ScreenFolder + "/TaxMachine_Screen_Pay.png";
    private const string FrownScreenTexturePath = ScreenFolder + "/TaxMachine_Screen_Frown.png";
    private const string SmileScreenTexturePath = ScreenFolder + "/TaxMachine_Screen_Smile.png";
    private const string CurrencyIconTexturePath = "Assets/UI/Inventory/Textures/CurrencySalvageWashers.png";
    private const string AmountDisplayShaderName = "StillWorking/Tax Machine Amount Display";
    private const int AmountDisplayEditorPreview = 11111;

    [MenuItem("StillWorking/Tax/Build Or Update Tax Collection Machine Prefab (No Scene Changes)")]
    public static void BuildOrUpdate()
    {
        // Blender produces only these six derivative assets. Import them explicitly instead of
        // refreshing the whole project, which keeps this prefab-only command bounded and avoids
        // touching unrelated user work.
        ImportScreenAsset(ScreenMeshPath);
        ImportScreenAsset(LowerScreenMeshPath);
        ImportScreenAsset(Days2ScreenTexturePath);
        ImportScreenAsset(Days1ScreenTexturePath);
        ImportScreenAsset(PayScreenTexturePath);
        ImportScreenAsset(FrownScreenTexturePath);
        ImportScreenAsset(SmileScreenTexturePath);

        Scene startMap = SceneManager.GetSceneByPath(StartMapScenePath);
        if (!startMap.IsValid() || !startMap.isLoaded)
        {
            throw new InvalidOperationException(
                $"Open {StartMapScenePath} through the official Unity Pipeline so its existing interaction layer can be copied. {nameof(BuildOrUpdate)} will not modify that scene.");
        }

        int interactionLayer = ResolveExistingInteractionLayer(startMap);

        EnsureFolder(ScreenFolder);
        EnsureFolder(MaterialsFolder);
        EnsureFolder(PrefabsFolder);

        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null)
            throw new InvalidOperationException($"Imported ATM model was not found at {ModelPath}.");

        GameObject screenMeshAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ScreenMeshPath);
        if (screenMeshAsset == null)
            throw new InvalidOperationException(
                $"The extracted upper-screen mesh was not imported at {ScreenMeshPath}. Run the deterministic Blender display build first.");

        GameObject lowerScreenMeshAsset = AssetDatabase.LoadAssetAtPath<GameObject>(LowerScreenMeshPath);
        if (lowerScreenMeshAsset == null)
            throw new InvalidOperationException(
                $"The extracted lower-screen cover was not imported at {LowerScreenMeshPath}. Run the deterministic Blender display build first.");

        Material ud1001Material = CreateOrUpdateImportedLitMaterial(Ud1001Tile, "TaxMachine_ATM_1001_Amarillo");
        Material ud1002Material = CreateOrUpdateImportedLitMaterial(Ud1002Tile, "TaxMachine_ATM_1002_Rosa");
        Material darkMaterial = CreateOrUpdateSolidLitMaterial("TaxMachine_Dark", new Color(0.035f, 0.045f, 0.07f), 0.7f, 0.36f, Color.black);
        Material accentMaterial = CreateOrUpdateSolidLitMaterial("TaxMachine_Accent", new Color(0.44f, 0.08f, 0.34f), 0.7f, 0.52f, new Color(0.28f, 0.015f, 0.16f));
        Material cashMaterial = CreateOrUpdateSolidLitMaterial("TaxMachine_Cash", new Color(0.23f, 0.82f, 0.38f), 0.05f, 0.3f, new Color(0.04f, 0.4f, 0.11f));
        Material lampMaterial = CreateOrUpdateSolidLitMaterial("TaxMachine_StatusLamp", Color.white, 0.15f, 0.45f, Color.white);
        Material amountDisplayMaterial = CreateOrUpdateAmountDisplayMaterial();
        Material days2ScreenMaterial = CreateOrUpdateScreenMaterial("TaxMachine_Screen_Days2", Days2ScreenTexturePath);
        Material days1ScreenMaterial = CreateOrUpdateScreenMaterial("TaxMachine_Screen_Days1", Days1ScreenTexturePath);
        Material payScreenMaterial = CreateOrUpdateScreenMaterial("TaxMachine_Screen_Pay", PayScreenTexturePath);
        Material frownScreenMaterial = CreateOrUpdateScreenMaterial("TaxMachine_Screen_Frown", FrownScreenTexturePath);
        Material smileScreenMaterial = CreateOrUpdateScreenMaterial("TaxMachine_Screen_Smile", SmileScreenTexturePath);

        BuildPrefab(
            modelAsset,
            screenMeshAsset,
            lowerScreenMeshAsset,
            ud1001Material,
            ud1002Material,
            darkMaterial,
            accentMaterial,
            cashMaterial,
            lampMaterial,
            amountDisplayMaterial,
            days2ScreenMaterial,
            days1ScreenMaterial,
            payScreenMaterial,
            frownScreenMaterial,
            smileScreenMaterial,
            interactionLayer);
    }

    private static GameObject BuildPrefab(
        GameObject modelAsset,
        GameObject screenMeshAsset,
        GameObject lowerScreenMeshAsset,
        Material ud1001Material,
        Material ud1002Material,
        Material darkMaterial,
        Material accentMaterial,
        Material cashMaterial,
        Material lampMaterial,
        Material amountDisplayMaterial,
        Material days2ScreenMaterial,
        Material days1ScreenMaterial,
        Material payScreenMaterial,
        Material frownScreenMaterial,
        Material smileScreenMaterial,
        int interactionLayer)
    {
        Scene stagingScene = EditorSceneManager.NewPreviewScene();
        GameObject root = new GameObject(PrefabRootName);
        SceneManager.MoveGameObjectToScene(root, stagingScene);

        try
        {
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            BoxCollider interactionCollider = root.AddComponent<BoxCollider>();
            interactionCollider.center = new Vector3(0f, 1.15f, 0f);
            interactionCollider.size = new Vector3(1.55f, 2.4f, 0.9f);

            AudioSource audioSource = root.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.minDistance = 1f;
            audioSource.maxDistance = 10f;
            // No clip is assigned: source assets did not provide one and the feature must not invent audio.

            GameObject modelVisual = PrefabUtility.InstantiatePrefab(modelAsset, root.scene) as GameObject;
            if (modelVisual == null)
                throw new InvalidOperationException($"Could not instantiate the imported ATM model at {ModelPath}.");

            modelVisual.name = "Cyberpunk ATM Model";
            modelVisual.transform.SetParent(root.transform, false);
            modelVisual.transform.localPosition = Vector3.zero;
            modelVisual.transform.localRotation = Quaternion.identity;
            modelVisual.transform.localScale = Vector3.one * ImportedModelLocalScale;
            CenterModelOnMachineRoot(modelVisual);
            AssignVerifiedUdimMaterials(modelVisual, ud1001Material, ud1002Material);

            Transform sourceScreenParent = FindRequiredChild(modelVisual.transform, Ud1001RendererName);
            GameObject screenSurface = PrefabUtility.InstantiatePrefab(screenMeshAsset, root.scene) as GameObject;
            if (screenSurface == null)
                throw new InvalidOperationException($"Could not instantiate the extracted display mesh at {ScreenMeshPath}.");

            screenSurface.name = "Tax Machine Display Surface";
            screenSurface.transform.SetParent(sourceScreenParent, false);
            screenSurface.transform.localPosition = Vector3.zero;
            screenSurface.transform.localRotation = Quaternion.identity;
            screenSurface.transform.localScale = Vector3.one;

            Renderer screenRenderer = screenSurface.GetComponentInChildren<Renderer>(true);
            if (screenRenderer == null)
                throw new InvalidOperationException("The extracted display mesh did not expose a renderer.");

            screenRenderer.sharedMaterial = frownScreenMaterial;

            GameObject cashTray = CreatePrimitive(
                PrimitiveType.Cube,
                "Cash Tray",
                root.transform,
                new Vector3(0f, 0.74f, FrontZ - 0.12f),
                new Vector3(0.68f, 0.045f, 0.23f),
                darkMaterial);

            GameObject cashSlot = CreatePrimitive(
                PrimitiveType.Cube,
                "Cash Slot",
                root.transform,
                new Vector3(0f, 0.91f, FrontZ - 0.145f),
                new Vector3(0.68f, 0.10f, 0.035f),
                darkMaterial);

            GameObject shutter = CreatePrimitive(
                PrimitiveType.Cube,
                "Cash Shutter",
                root.transform,
                new Vector3(0f, 1.14f, FrontZ - 0.16f),
                new Vector3(0.72f, 0.22f, 0.045f),
                accentMaterial);

            GameObject button = CreatePrimitive(
                PrimitiveType.Sphere,
                "Pay Team Tax Button",
                root.transform,
                new Vector3(0.43f, 0.82f, FrontZ - 0.17f),
                Vector3.one * 0.13f,
                accentMaterial);

            GameObject cashVisual = CreatePrimitive(
                PrimitiveType.Cube,
                "Inserted Cash Visual",
                root.transform,
                new Vector3(0f, 0.80f, FrontZ - 0.36f),
                new Vector3(0.35f, 0.025f, 0.18f),
                cashMaterial);

            GameObject lamp = CreatePrimitive(
                PrimitiveType.Sphere,
                "Tax Status Lamp",
                root.transform,
                new Vector3(-0.43f, 1.72f, FrontZ - 0.17f),
                Vector3.one * 0.14f,
                lampMaterial);
            Light statusLight = lamp.AddComponent<Light>();
            statusLight.type = LightType.Point;
            statusLight.range = 2.2f;
            statusLight.intensity = 1.6f;
            statusLight.color = new Color(0.15f, 1f, 0.55f);
            statusLight.shadows = LightShadows.None;

            Renderer amountDisplayRenderer = CreateMaterialAmountDisplay(
                root.transform,
                sourceScreenParent,
                lowerScreenMeshAsset,
                amountDisplayMaterial,
                out GameObject amountDisplayRoot);
            TaxMachineScreenController screenController = root.AddComponent<TaxMachineScreenController>();
            screenController.ConfigureAuthoring(
                screenRenderer,
                days2ScreenMaterial,
                days1ScreenMaterial,
                payScreenMaterial,
                frownScreenMaterial,
                smileScreenMaterial);

            TaxCollectionMachine machine = root.AddComponent<TaxCollectionMachine>();
            machine.ConfigureAuthoring(
                interactionCollider,
                button.GetComponent<Renderer>(),
                shutter.transform,
                cashVisual.transform,
                amountDisplayRoot,
                amountDisplayRenderer,
                statusLight,
                lamp.GetComponent<Renderer>(),
                audioSource,
                InteractionDistance);
            machine.ConfigureScreenController(screenController);

            cashVisual.SetActive(false);
            SetLayerRecursively(root, interactionLayer);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            if (!saved || prefab == null)
                throw new InvalidOperationException($"Could not save the tax-machine prefab at {PrefabPath}.");

            return prefab;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(stagingScene);
        }
    }

    private static int ResolveExistingInteractionLayer(Scene startMap)
    {
        Mailbox[] mailboxes = UnityEngine.Object.FindObjectsByType<Mailbox>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < mailboxes.Length; i++)
        {
            if (mailboxes[i] != null && mailboxes[i].gameObject.scene == startMap)
                return mailboxes[i].gameObject.layer;
        }

        SkillWebTerminalInteraction[] terminals = UnityEngine.Object.FindObjectsByType<SkillWebTerminalInteraction>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < terminals.Length; i++)
        {
            if (terminals[i] != null && terminals[i].gameObject.scene == startMap)
                return terminals[i].gameObject.layer;
        }

        throw new InvalidOperationException(
            "No Mailbox or SkillWebTerminalInteraction in StartMap could provide the existing interaction layer. The layer was not guessed.");
    }

    private static Material CreateOrUpdateImportedLitMaterial(int udimTile, string materialName)
    {
        Texture2D baseColor = LoadTexture(udimTile, "BaseColor", normalMap: false);
        Texture2D emissive = LoadTexture(udimTile, "Emissive", normalMap: false);
        Texture2D metallic = LoadTexture(udimTile, "Metallic", normalMap: false);
        Texture2D normal = LoadTexture(udimTile, "Normal", normalMap: true);
        // The exported roughness image is retained but intentionally not assigned to _MetallicGlossMap:
        // URP Lit expects smoothness in that map's alpha channel, which this standalone roughness JPG lacks.

        Material material = GetOrCreateMaterial(materialName);
        ConfigureUrpLitMaterial(material, baseColor, normal, emissive, metallic, 0.7f, 0.46f, Color.white * 1.15f);
        return material;
    }

    private static Material CreateOrUpdateSolidLitMaterial(
        string materialName,
        Color baseColor,
        float metallic,
        float smoothness,
        Color emission)
    {
        Material material = GetOrCreateMaterial(materialName);
        ConfigureUrpLitMaterial(material, null, null, null, null, metallic, smoothness, emission);
        material.SetColor("_BaseColor", baseColor);
        MarkAndSaveAsset(material);
        return material;
    }

    private static Material CreateOrUpdateScreenMaterial(string materialName, string texturePath)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
            throw new InvalidOperationException($"Generated tax-machine screen texture was not found at {texturePath}.");

        Material material = GetOrCreateMaterial(materialName);
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            throw new InvalidOperationException("Universal Render Pipeline/Unlit shader was not found.");

        material.shader = shader;
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        MarkAndSaveAsset(material);
        return material;
    }

    private static Material CreateOrUpdateAmountDisplayMaterial()
    {
        Texture2D currencyTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(CurrencyIconTexturePath);
        if (currencyTexture == null)
            throw new InvalidOperationException($"Inventory currency texture was not found at {CurrencyIconTexturePath}.");

        Shader shader = Shader.Find(AmountDisplayShaderName);
        if (shader == null)
            throw new InvalidOperationException($"Tax amount display shader was not found: {AmountDisplayShaderName}.");

        Material material = GetOrCreateMaterial("TaxMachine_AmountDisplay");
        material.shader = shader;
        material.SetTexture("_CurrencyTex", currencyTexture);
        material.SetFloat("_Amount", AmountDisplayEditorPreview);
        material.SetFloat("_DigitCount", 5f);
        material.SetFloat("_ScreenAspect", 2.443397f);
        material.SetColor("_BackgroundColor", new Color(0.002f, 0.003f, 0.004f, 1f));
        material.SetColor("_NumberColor", new Color(0.94f, 0.97f, 1f, 1f));
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        MarkAndSaveAsset(material);
        return material;
    }

    private static Material GetOrCreateMaterial(string materialName)
    {
        string path = MaterialsFolder + "/" + materialName + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
            return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("Universal Render Pipeline/Lit shader was not found.");

        material = new Material(shader)
        {
            name = materialName
        };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void ConfigureUrpLitMaterial(
        Material material,
        Texture2D baseColor,
        Texture2D normal,
        Texture2D emission,
        Texture2D metallicMap,
        float metallic,
        float smoothness,
        Color emissionColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("Universal Render Pipeline/Lit shader was not found.");

        material.shader = shader;
        material.SetFloat("_Metallic", Mathf.Clamp01(metallic));
        material.SetFloat("_Smoothness", Mathf.Clamp01(smoothness));
        material.SetTexture("_BaseMap", baseColor);
        material.SetTexture("_BumpMap", normal);
        material.SetTexture("_EmissionMap", emission);
        material.SetTexture("_MetallicGlossMap", metallicMap);
        material.SetColor("_EmissionColor", emissionColor);

        SetKeyword(material, "_NORMALMAP", normal != null);
        SetKeyword(material, "_METALLICSPECGLOSSMAP", metallicMap != null);
        SetKeyword(material, "_EMISSION", emission != null || emissionColor.maxColorComponent > 0.001f);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        MarkAndSaveAsset(material);
    }

    private static void SetKeyword(Material material, string keyword, bool enabled)
    {
        if (enabled)
            material.EnableKeyword(keyword);
        else
            material.DisableKeyword(keyword);
    }

    private static Texture2D LoadTexture(int udimTile, string suffix, bool normalMap)
    {
        string path = AssetRoot + "/Textures/modelo3_" + udimTile + "_" + suffix + ".jpg";
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture == null)
            throw new InvalidOperationException($"Texture could not be loaded after import: {path}.");

        return texture;
    }

    private static Transform FindRequiredChild(Transform parent, string childName)
    {
        Transform[] descendants = parent.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < descendants.Length; i++)
        {
            if (string.Equals(descendants[i].name, childName, StringComparison.Ordinal))
                return descendants[i];
        }

        throw new InvalidOperationException(
            $"The imported ATM did not contain the verified {childName} transform required for display alignment.");
    }

    private static void AssignVerifiedUdimMaterials(GameObject modelVisual, Material ud1001Material, Material ud1002Material)
    {
        MeshRenderer[] renderers = modelVisual.GetComponentsInChildren<MeshRenderer>(true);
        bool foundUd1001Renderer = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer renderer = renderers[i];
            bool usesUd1001 = string.Equals(renderer.gameObject.name, Ud1001RendererName, StringComparison.Ordinal);
            Material assignedMaterial = usesUd1001 ? ud1001Material : ud1002Material;

            if (usesUd1001)
                foundUd1001Renderer = true;

            Material[] slots = renderer.sharedMaterials;
            if (slots == null || slots.Length == 0)
            {
                renderer.sharedMaterial = assignedMaterial;
                continue;
            }

            for (int slot = 0; slot < slots.Length; slot++)
                slots[slot] = assignedMaterial;

            renderer.sharedMaterials = slots;
        }

        if (!foundUd1001Renderer)
        {
            throw new InvalidOperationException(
                $"The imported ATM did not contain the verified {Ud1001RendererName} renderer; material assignment was not guessed.");
        }
    }

    private static void CenterModelOnMachineRoot(GameObject modelVisual)
    {
        Renderer[] renderers = modelVisual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            throw new InvalidOperationException("The imported ATM model did not expose any renderers.");

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        modelVisual.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
    }

    private static GameObject CreatePrimitive(
        PrimitiveType type,
        string name,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Material material)
    {
        GameObject primitive = GameObject.CreatePrimitive(type);
        primitive.name = name;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = localPosition;
        primitive.transform.localRotation = Quaternion.identity;
        primitive.transform.localScale = localScale;

        Collider collider = primitive.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.DestroyImmediate(collider);

        Renderer renderer = primitive.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;

        return primitive;
    }

    private static Renderer CreateMaterialAmountDisplay(
        Transform parent,
        Transform sourceScreenTransform,
        GameObject lowerScreenMeshAsset,
        Material amountDisplayMaterial,
        out GameObject amountDisplayRoot)
    {
        // Blender-verified source geometry: Box004 polygons 53 + 111 are the complete lower
        // monitor surface (14.677 x 6.006802 source units, aspect 2.443397:1).  The imported OBJ
        // uses a reflected coordinate basis.  Reusing the exact extracted mesh avoids guessed
        // Cube transforms and lets one opaque material cover the entire physical screen.

        amountDisplayRoot = new GameObject("Tax Amount Display");
        amountDisplayRoot.transform.SetParent(sourceScreenTransform, false);
        amountDisplayRoot.transform.localPosition = Vector3.zero;
        amountDisplayRoot.transform.localRotation = Quaternion.identity;
        amountDisplayRoot.transform.localScale = Vector3.one;

        GameObject displaySurface = PrefabUtility.InstantiatePrefab(lowerScreenMeshAsset, parent.gameObject.scene) as GameObject;
        if (displaySurface == null)
            throw new InvalidOperationException($"Could not instantiate the Blender-extracted lower display at {LowerScreenMeshPath}.");

        displaySurface.name = "Tax Amount Material Surface";
        displaySurface.transform.SetParent(amountDisplayRoot.transform, false);
        displaySurface.transform.localPosition = Vector3.zero;
        displaySurface.transform.localRotation = Quaternion.identity;
        displaySurface.transform.localScale = Vector3.one;
        MeshRenderer displayRenderer = displaySurface.GetComponentInChildren<MeshRenderer>(true);
        if (displayRenderer == null)
            throw new InvalidOperationException("The Blender-extracted lower display did not expose a MeshRenderer.");

        MeshFilter displayMeshFilter = displaySurface.GetComponentInChildren<MeshFilter>(true);
        if (displayMeshFilter == null
            || displayMeshFilter.sharedMesh == null
            || displayMeshFilter.sharedMesh.normals.Length == 0)
        {
            throw new InvalidOperationException("The Blender-extracted lower display did not expose a usable mesh normal.");
        }

        // Derive the visible outward direction after Unity has applied the OBJ basis reflection.
        // A 12 mm separation is enough to win depth testing against the original NO SIGNAL plane
        // without reaching the surrounding bezel.
        Vector3 importedFrontNormal = displayRenderer.transform.TransformDirection(
            displayMeshFilter.sharedMesh.normals[0]).normalized;
        displaySurface.transform.position += importedFrontNormal * 0.012f;

        displayRenderer.sharedMaterial = amountDisplayMaterial;
        displayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        displayRenderer.receiveShadows = false;
        displayRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        displayRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return displayRenderer;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        Transform transform = target.transform;
        for (int i = 0; i < transform.childCount; i++)
            SetLayerRecursively(transform.GetChild(i).gameObject, layer);
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        int slash = folderPath.LastIndexOf('/');
        if (slash <= 0)
            throw new InvalidOperationException($"Cannot create invalid asset folder '{folderPath}'.");

        string parent = folderPath.Substring(0, slash);
        string leaf = folderPath.Substring(slash + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    private static void ImportScreenAsset(string assetPath)
    {
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
    }

    private static void MarkAndSaveAsset(UnityEngine.Object asset)
    {
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssetIfDirty(asset);
    }
}
