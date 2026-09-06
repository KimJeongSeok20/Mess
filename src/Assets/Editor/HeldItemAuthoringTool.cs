using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HeldItemAuthoringTool
{
    public const int CurrentLayoutVersion = 3;
    public const string StartMapPath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
    public const string DungeonSpawnListPath = "Assets/Scripts/Dungeon 1/item/ItemSpawnList.asset";

    private const string PlayerPrefabPath = "Assets/FPS/Cyber_Generic.prefab";
    private const string RootFolder = "Assets/FPS/HeldItems";
    private const string VisualFolder = RootFolder + "/Visuals";
    private const string CatalogPath = RootFolder + "/HeldItemVisualCatalog.asset";
    private const string GroupStartMap = "StartMap";
    private const string GroupDungeon = "Dungeon";

    private static readonly Dictionary<string, string> WorldVisualOverrides = new(StringComparer.Ordinal)
    {
        ["GiftBox1"] = "Assets/Scripts/Currency/Prefab/GiftBox1.prefab",
        ["GiftBox2"] = "Assets/Scripts/Currency/Prefab/GiftBox2.prefab",
        ["GiftBox3"] = "Assets/Scripts/Currency/Prefab/GiftBox3.prefab",
        ["GiftBox4"] = "Assets/Scripts/Currency/Prefab/GiftBox4.prefab"
    };

    private sealed class SourceSpec
    {
        public SourceSpec(string itemName, string itemPrefabPath, string visualPrefabPath, string group)
        {
            ItemName = itemName;
            ItemPrefabPath = itemPrefabPath;
            VisualPrefabPath = visualPrefabPath;
            Group = group;
        }

        public string ItemName { get; }
        public string ItemPrefabPath { get; }
        public string VisualPrefabPath { get; }
        public string Group { get; }
    }

    [MenuItem("Tools/Inventory/Rebuild Held Item Visuals")]
    public static string Rebuild()
    {
        EnsureFolder(RootFolder);
        EnsureFolder(VisualFolder);

        List<SourceSpec> sources = CollectSources();
        if (sources.Count == 0)
            throw new InvalidOperationException("No held-item sources were found in StartMap or the dungeon spawn list.");

        HeldItemVisualCatalog catalog = AssetDatabase.LoadAssetAtPath<HeldItemVisualCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<HeldItemVisualCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        SerializedObject catalogObject = new(catalog);
        catalogObject.Update();
        int layoutVersion = catalogObject.FindProperty("layoutVersion").intValue;
        var previousTuning = new Dictionary<string, (Vector3 position, Vector3 euler, Vector3 scale)>(StringComparer.Ordinal);

        foreach (HeldItemVisualCatalog.Entry existingEntry in catalog.Entries)
        {
            if (existingEntry == null || string.IsNullOrWhiteSpace(existingEntry.itemName))
                continue;

            previousTuning[existingEntry.itemName] =
                (existingEntry.localPosition, existingEntry.localEulerAngles, existingEntry.localScale);
        }

        bool migratingToPivotLayout = layoutVersion < CurrentLayoutVersion;
        SerializedProperty entriesProperty = catalogObject.FindProperty("entries");
        entriesProperty.arraySize = sources.Count;

        for (int i = 0; i < sources.Count; i++)
        {
            SourceSpec source = sources[i];
            GameObject itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(source.ItemPrefabPath);
            GameObject visualSourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(source.VisualPrefabPath);
            if (itemPrefab == null)
                throw new InvalidOperationException($"Missing held-item prefab: {source.ItemPrefabPath}");
            if (visualSourcePrefab == null)
                throw new InvalidOperationException($"Missing held-item visual source: {source.VisualPrefabPath}");

            Item sourceItem = itemPrefab.GetComponentInChildren<Item>(true);
            if (sourceItem == null || !string.Equals(sourceItem.ItemName, source.ItemName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Held-item source identity mismatch at {source.ItemPrefabPath}. Expected '{source.ItemName}', found '{sourceItem?.ItemName ?? "NULL"}'.");
            }

            string visualPath = $"{VisualFolder}/{SanitizeFileName(source.ItemName)}_HeldVisual.prefab";
            GameObject visualPrefab = RebuildVisualPrefab(visualSourcePrefab, source.ItemName, visualPath);

            SerializedProperty entry = entriesProperty.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("itemName").stringValue = source.ItemName;
            entry.FindPropertyRelative("visualPrefab").objectReferenceValue = visualPrefab;
            entry.FindPropertyRelative("group").stringValue = source.Group;

            bool hasPreviousTuning = previousTuning.TryGetValue(source.ItemName, out var tuning);
            entry.FindPropertyRelative("localPosition").vector3Value =
                hasPreviousTuning && !migratingToPivotLayout ? tuning.position : Vector3.zero;
            entry.FindPropertyRelative("localEulerAngles").vector3Value =
                hasPreviousTuning ? tuning.euler : Vector3.zero;

            Vector3 pivotScale = Vector3.one;
            if (hasPreviousTuning && !migratingToPivotLayout)
                pivotScale = HeldItemVisualPlacement.SanitizeScale(tuning.scale);

            bool isGiftBox = WorldVisualOverrides.ContainsKey(source.ItemName);
            bool isDefaultScale = Mathf.Abs(pivotScale.x - 1f) < 0.001f
                && Mathf.Abs(pivotScale.y - 1f) < 0.001f
                && Mathf.Abs(pivotScale.z - 1f) < 0.001f;
            if (!isGiftBox && (migratingToPivotLayout || isDefaultScale)
                && TryComputeAutoFitScale(visualPrefab, out Vector3 fitted))
            {
                pivotScale = fitted;
            }

            entry.FindPropertyRelative("localScale").vector3Value = pivotScale;
        }

        catalogObject.FindProperty("layoutVersion").intValue = CurrentLayoutVersion;
        catalogObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);
        WirePlayerPrefab(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        string validation = Validate();
        Debug.Log(validation);
        return validation;
    }

    [MenuItem("Tools/Inventory/Validate Held Item Visuals")]
    public static string Validate()
    {
        var errors = new List<string>();
        List<SourceSpec> sources = CollectSources();
        HeldItemVisualCatalog catalog = AssetDatabase.LoadAssetAtPath<HeldItemVisualCatalog>(CatalogPath);
        if (catalog == null)
        {
            errors.Add("Catalog asset is missing.");
        }
        else
        {
            if (catalog.Entries.Count != sources.Count)
                errors.Add($"Catalog count is {catalog.Entries.Count}, expected {sources.Count}.");

            var expectedNames = new HashSet<string>(sources.Select(x => x.ItemName), StringComparer.Ordinal);
            var actualNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (HeldItemVisualCatalog.Entry entry in catalog.Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.itemName))
                {
                    errors.Add("Catalog contains an empty entry.");
                    continue;
                }

                if (!actualNames.Add(entry.itemName))
                    errors.Add($"Duplicate catalog entry: {entry.itemName}");

                if (entry.visualPrefab == null)
                {
                    errors.Add($"Visual prefab missing: {entry.itemName}");
                    continue;
                }

                foreach (Component component in entry.visualPrefab.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || IsAllowedVisualComponent(component))
                        continue;

                    errors.Add($"Forbidden component on {entry.itemName}: {component.GetType().FullName}");
                }
            }

            foreach (string expectedName in expectedNames)
            {
                if (!actualNames.Contains(expectedName))
                    errors.Add($"Catalog entry missing: {expectedName}");
            }
        }

        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        HeldItemPresenter presenter = player != null ? player.GetComponent<HeldItemPresenter>() : null;
        WeaponInventoryBridge bridge = player != null ? player.GetComponent<WeaponInventoryBridge>() : null;

        if (player == null)
            errors.Add("Cyber_Generic prefab is missing.");
        if (presenter == null)
            errors.Add("Cyber_Generic is missing HeldItemPresenter.");
        if (bridge == null)
            errors.Add("Cyber_Generic is missing WeaponInventoryBridge.");

        if (presenter != null)
        {
            SerializedObject presenterObject = new(presenter);
            if (presenterObject.FindProperty("heldItemAnchor").objectReferenceValue == null)
                errors.Add("HeldItemPresenter anchor is not assigned.");
            if (presenterObject.FindProperty("catalog").objectReferenceValue != catalog)
                errors.Add("HeldItemPresenter catalog is not assigned.");

            Transform anchor = presenterObject.FindProperty("heldItemAnchor").objectReferenceValue as Transform;
            if (anchor != null && (anchor.parent == null || anchor.parent.name != "middle_01_l"))
                errors.Add("HeldItemPresenter anchor is not parented to middle_01_l.");
        }

        if (bridge != null)
        {
            SerializedObject bridgeObject = new(bridge);
            if (bridgeObject.FindProperty("heldItemPresenter").objectReferenceValue != presenter)
                errors.Add("WeaponInventoryBridge presenter is not assigned.");
        }

        return errors.Count == 0
            ? $"HELD_ITEM_VALIDATION_PASS entries={sources.Count} dungeon={sources.Count(x => x.Group == GroupDungeon)} startMap={sources.Count(x => x.Group == GroupStartMap)} forbiddenComponents=0 playerWired=True"
            : "HELD_ITEM_VALIDATION_FAIL\n" + string.Join("\n", errors);
    }

    public static Dictionary<string, float> CollectStartMapWorldMaxExtents()
    {
        var sizes = new Dictionary<string, float>(StringComparer.Ordinal);
        Scene scene = default;
        bool alreadyOpen = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene candidate = SceneManager.GetSceneAt(i);
            if (candidate.path == StartMapPath)
            {
                scene = candidate;
                alreadyOpen = true;
                break;
            }
        }

        if (!alreadyOpen)
            scene = EditorSceneManager.OpenScene(StartMapPath, OpenSceneMode.Additive);

        try
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform == null || !WorldVisualOverrides.ContainsKey(transform.name))
                        continue;
                    if (!HeldItemVisualPlacement.TryGetWorldRendererBounds(transform, out Bounds bounds))
                        continue;

                    float maxExtent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    if (maxExtent <= 0.0001f)
                        continue;

                    if (!sizes.TryGetValue(transform.name, out float existing) || maxExtent > existing)
                        sizes[transform.name] = maxExtent;
                }

                foreach (Item item in root.GetComponentsInChildren<Item>(true))
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.ItemName))
                        continue;
                    if (!HeldItemVisualPlacement.TryGetWorldRendererBounds(item.transform, out Bounds bounds))
                        continue;

                    float maxExtent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    if (maxExtent <= 0.0001f)
                        continue;

                    if (!sizes.TryGetValue(item.ItemName, out float existing) || maxExtent > existing)
                        sizes[item.ItemName] = maxExtent;
                }
            }
        }
        finally
        {
            if (!alreadyOpen && scene.IsValid())
                EditorSceneManager.CloseScene(scene, true);
        }

        return sizes;
    }

    public static IReadOnlyList<string> GetExpectedHeldItemNames()
    {
        return CollectSources().Select(source => source.ItemName).ToList();
    }

    private static List<SourceSpec> CollectSources()
    {
        var sources = new List<SourceSpec>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void TryAdd(Item item, string group)
        {
            if (item == null || item is WeaponItem || string.IsNullOrWhiteSpace(item.ItemName))
                return;

            string itemPath = AssetDatabase.GetAssetPath(item);
            if (string.IsNullOrWhiteSpace(itemPath) || !seen.Add(item.ItemName))
                return;

            string visualPath = itemPath;
            if (WorldVisualOverrides.TryGetValue(item.ItemName, out string overridePath)
                && AssetDatabase.LoadAssetAtPath<GameObject>(overridePath) != null)
            {
                visualPath = overridePath;
            }

            sources.Add(new SourceSpec(item.ItemName, itemPath, visualPath, group));
        }

        foreach (Item item in CollectStartMapItems())
            TryAdd(item, GroupStartMap);

        ItemSpawnlist spawnList = AssetDatabase.LoadAssetAtPath<ItemSpawnlist>(DungeonSpawnListPath);
        if (spawnList != null)
        {
            foreach (ItemSpawnlist.ItemEntry entry in spawnList.Items)
            {
                if (entry == null || entry.prefab == null)
                    continue;

                Item item = entry.prefab.GetComponentInChildren<Item>(true);
                TryAdd(item, GroupDungeon);
            }
        }

        return sources;
    }

    private static List<Item> CollectStartMapItems()
    {
        var result = new List<Item>();
        Scene scene = default;
        bool alreadyOpen = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene candidate = SceneManager.GetSceneAt(i);
            if (candidate.path == StartMapPath)
            {
                scene = candidate;
                alreadyOpen = true;
                break;
            }
        }

        if (!alreadyOpen)
            scene = EditorSceneManager.OpenScene(StartMapPath, OpenSceneMode.Additive);

        try
        {
            InventoryManager manager = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                manager = root.GetComponentInChildren<InventoryManager>(true);
                if (manager != null)
                    break;
            }

            if (manager == null)
                return result;

            SerializedObject serialized = new(manager);
            SerializedProperty allItems = serialized.FindProperty("allItems");
            var seen = new HashSet<UnityEngine.Object>();
            for (int i = 0; i < allItems.arraySize; i++)
            {
                Item item = allItems.GetArrayElementAtIndex(i).objectReferenceValue as Item;
                if (item == null || !seen.Add(item))
                    continue;

                result.Add(item);
            }
        }
        finally
        {
            if (!alreadyOpen && scene.IsValid())
                EditorSceneManager.CloseScene(scene, true);
        }

        return result;
    }

    private static bool TryComputeAutoFitScale(GameObject visualPrefab, out Vector3 scale)
    {
        scale = Vector3.one;
        if (visualPrefab == null)
            return false;

        GameObject contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(visualPrefab));
        try
        {
            if (!HeldItemVisualPlacement.TryGetWorldRendererBounds(contents.transform, out Bounds bounds))
                return false;

            if (!HeldItemVisualPlacement.ShouldAutoFit(bounds))
                return false;

            scale = HeldItemVisualPlacement.FitPivotScaleToHand(contents.transform);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static GameObject RebuildVisualPrefab(GameObject sourcePrefab, string itemName, string visualPath)
    {
        GameObject clone = UnityEngine.Object.Instantiate(sourcePrefab);
        clone.name = SanitizeFileName(itemName) + "_HeldVisual";
        clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        clone.transform.localScale = sourcePrefab.transform.localScale;

        Component[] components = clone.GetComponentsInChildren<Component>(true);
        foreach (Component component in components
                     .Where(x => x != null && !IsAllowedVisualComponent(x))
                     .OrderBy(RemovalPriority))
        {
            UnityEngine.Object.DestroyImmediate(component);
        }

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(clone, visualPath);
        UnityEngine.Object.DestroyImmediate(clone);

        if (saved == null)
            throw new InvalidOperationException($"Failed to save held visual: {visualPath}");

        return saved;
    }

    private static int RemovalPriority(Component component)
    {
        if (component is Joint) return 0;
        if (component is Collider) return 1;
        if (component is MonoBehaviour) return 2;
        if (component is Rigidbody) return 3;
        return 4;
    }

    private static bool IsAllowedVisualComponent(Component component)
    {
        return component is Transform
            || component is MeshFilter
            || component is MeshRenderer
            || component is SkinnedMeshRenderer;
    }

    private static void WirePlayerPrefab(HeldItemVisualCatalog catalog)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Transform middleFingerBone = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(x => x.name == "middle_01_l"
                    && x.parent != null && x.parent.name == "middle_metacarpal_l");

            if (middleFingerBone == null)
                throw new InvalidOperationException("Cyber_Generic middle_01_l bone was not found.");

            HeldItemPresenter presenter = root.GetComponent<HeldItemPresenter>();
            if (presenter == null)
                presenter = root.AddComponent<HeldItemPresenter>();

            SerializedObject presenterObject = new(presenter);
            Transform anchor = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(x => x.name == "HeldItemAnchor");

            if (anchor == null)
                anchor = new GameObject("HeldItemAnchor").transform;

            anchor.SetParent(middleFingerBone, false);
            anchor.localPosition = Vector3.zero;
            anchor.localRotation = Quaternion.identity;
            anchor.localScale = Vector3.one;

            presenterObject.FindProperty("heldItemAnchor").objectReferenceValue = anchor;
            presenterObject.FindProperty("catalog").objectReferenceValue = catalog;
            presenterObject.FindProperty("transitionDuration").floatValue = 0.15f;
            presenterObject.FindProperty("loweredLocalOffset").vector3Value = new Vector3(0f, -0.15f, 0f);
            presenterObject.ApplyModifiedPropertiesWithoutUndo();

            WeaponInventoryBridge bridge = root.GetComponent<WeaponInventoryBridge>();
            if (bridge == null)
                throw new InvalidOperationException("Cyber_Generic WeaponInventoryBridge was not found.");

            SerializedObject bridgeObject = new(bridge);
            bridgeObject.FindProperty("heldItemPresenter").objectReferenceValue = presenter;
            bridgeObject.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string parent = folderPath.Substring(0, folderPath.LastIndexOf('/'));
        string folderName = folderPath.Substring(folderPath.LastIndexOf('/') + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static string SanitizeFileName(string itemName)
    {
        foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
            itemName = itemName.Replace(invalid, '_');

        return itemName.Replace(' ', '_').Replace('+', '_');
    }
}
