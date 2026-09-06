using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Manually synchronizes authored prefab hierarchy and non-baked component settings from
/// NewPrison/Tile_modified to the existing V2 test canonical/output prefabs.
///
/// The tool deliberately does not edit DungeonTileBakeData/LightingSet assets. Components
/// and objects that own baked-lighting references are kept on the target prefab, and baked
/// renderer/light values are restored after the rest of each component is copied.
/// </summary>
public static class DungeonV2NonBakeSettingsSync
{
    private const string TileModifiedRoot = "Assets/Prefabs/map_piece/NewPrison/Tile_modified";
    private const string TestRoot = "Assets/Prefabs/map_piece/NewPrison/test";

    private sealed class SyncStats
    {
        public int createdObjects;
        public int removedObjects;
        public int addedComponents;
        public int removedComponents;
        public int copiedComponents;
        public int preservedBakeObjects;
    }

    private sealed class SyncContext
    {
        public Transform sourceRoot;
        public Transform targetRoot;
        public readonly List<KeyValuePair<Transform, Transform>> transformPairs =
            new List<KeyValuePair<Transform, Transform>>();
        public readonly Dictionary<int, UnityEngine.Object> objectMap =
            new Dictionary<int, UnityEngine.Object>();
        public readonly HashSet<int> protectedTargetTransformIds = new HashSet<int>();
        public readonly HashSet<int> mappedTargetComponentIds = new HashSet<int>();
        public readonly SyncStats stats = new SyncStats();
        public bool isRuntimeV2Output;
    }

    private struct RendererBakeState
    {
        public int lightmapIndex;
        public Vector4 lightmapScaleOffset;
        public int realtimeLightmapIndex;
        public Vector4 realtimeLightmapScaleOffset;
    }

    private static void SyncSelectedRoomMenu()
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        string result = SyncRoomCli(path);
        Debug.Log(result);
        EditorUtility.DisplayDialog("V2 Non-Bake Sync", result, "OK");
    }

    private static void SyncAllExistingRoomsMenu()
    {
        string result = SyncAllExistingTestRoomsCli();
        Debug.Log(result);
        EditorUtility.DisplayDialog("V2 Non-Bake Sync", result, "OK");
    }

    private static void SyncStartAndAdministrativeMenu()
    {
        string result = SyncStartRoomAndAdministrativeCli();
        Debug.Log(result);
        EditorUtility.DisplayDialog("V2 Non-Bake Sync", result, "OK");
    }

    public static string SyncStartRoomAndAdministrativeCli()
    {
        return SyncRoomsCli(new[]
        {
            TileModifiedRoot + "/StartRoom.prefab",
            TileModifiedRoot + "/AdminstrativeSegregation.prefab",
        });
    }

    public static string SyncAllExistingTestRoomsCli()
    {
        if (EditorApplication.isPlaying)
            return "ERROR: Exit Play Mode before synchronizing V2 prefabs.";

        string[] sourceGuids = AssetDatabase.FindAssets("t:Prefab", new[] { TileModifiedRoot });
        var sourcePaths = new List<string>();
        for (int i = 0; i < sourceGuids.Length; i++)
        {
            string sourcePath = AssetDatabase.GUIDToAssetPath(sourceGuids[i]);
            string roomName = Path.GetFileNameWithoutExtension(sourcePath);
            if (AssetDatabase.IsValidFolder(TestRoot + "/V2_" + roomName))
                sourcePaths.Add(sourcePath);
        }

        sourcePaths.Sort(StringComparer.OrdinalIgnoreCase);
        return SyncRoomsCli(sourcePaths);
    }

    public static string SyncRoomCli(string sourcePrefabPath)
    {
        return SyncRoomsCli(new[] { sourcePrefabPath });
    }

    private static string SyncRoomsCli(IEnumerable<string> sourcePrefabPaths)
    {
        if (EditorApplication.isPlaying)
            return "ERROR: Exit Play Mode before synchronizing V2 prefabs.";
        if (Lightmapping.isRunning)
            return "ERROR: Wait for the current lightmapping bake to finish before synchronizing V2 prefabs.";

        var report = new StringBuilder();
        bool passed = true;
        int roomCount = 0;
        int targetCount = 0;

        foreach (string rawSourcePath in sourcePrefabPaths ?? Array.Empty<string>())
        {
            string sourcePath = NormalizeAssetPath(rawSourcePath);
            if (!IsTileModifiedPrefab(sourcePath))
            {
                passed = false;
                report.AppendLine("FAIL source must be a prefab directly under Tile_modified: " + sourcePath);
                continue;
            }

            GameObject sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (sourceAsset == null)
            {
                passed = false;
                report.AppendLine("FAIL missing source prefab: " + sourcePath);
                continue;
            }

            string roomName = Path.GetFileNameWithoutExtension(sourcePath);
            string roomTestRoot = TestRoot + "/V2_" + roomName;
            string[] targets =
            {
                roomTestRoot + "/Canonical/" + roomName + "_R000.prefab",
                roomTestRoot + "/" + roomName + ".prefab",
            };

            Dictionary<string, string> bakeHashesBefore = CaptureBakeAssetHashes(roomTestRoot);
            bool syncedAnyTarget = false;
            report.AppendLine("ROOM " + roomName);
            for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                string targetPath = targets[targetIndex];
                if (AssetDatabase.LoadAssetAtPath<GameObject>(targetPath) == null)
                {
                    passed = false;
                    report.AppendLine("  FAIL missing target prefab: " + targetPath);
                    continue;
                }

                try
                {
                    SyncStats stats = SyncPrefab(sourcePath, targetPath);
                    syncedAnyTarget = true;
                    targetCount++;
                    report.AppendLine(
                        "  SYNC " + targetPath +
                        " | objects +" + stats.createdObjects + "/-" + stats.removedObjects +
                        ", components +" + stats.addedComponents + "/-" + stats.removedComponents +
                        ", copied=" + stats.copiedComponents +
                        ", keptBakeObjects=" + stats.preservedBakeObjects);
                }
                catch (Exception exception)
                {
                    passed = false;
                    report.AppendLine("  FAIL " + targetPath + ": " + exception);
                }
            }

            if (syncedAnyTarget)
                roomCount++;

            AssetDatabase.SaveAssets();
            Dictionary<string, string> bakeHashesAfter = CaptureBakeAssetHashes(roomTestRoot);
            if (!HashesMatch(bakeHashesBefore, bakeHashesAfter, out string hashDifference))
            {
                passed = false;
                report.AppendLine("  FAIL BakeData/LightingSet asset changed: " + hashDifference);
            }
            else
            {
                report.AppendLine("  BAKED_ASSETS_UNCHANGED count=" + bakeHashesAfter.Count);
            }

            string compatibility = BuildBakeCompatibilityReport(roomTestRoot, targets);
            if (!string.IsNullOrEmpty(compatibility))
                report.Append(compatibility);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        report.Insert(0, (passed ? "PASS" : "FAIL") +
                         ": rooms=" + roomCount + ", targets=" + targetCount + Environment.NewLine);
        return report.ToString();
    }

    private static SyncStats SyncPrefab(string sourcePath, string targetPath)
    {
        GameObject sourceRoot = null;
        GameObject targetRoot = null;
        try
        {
            sourceRoot = PrefabUtility.LoadPrefabContents(sourcePath);
            targetRoot = PrefabUtility.LoadPrefabContents(targetPath);
            if (sourceRoot == null || targetRoot == null)
                throw new InvalidOperationException("Could not load prefab contents.");

            var context = new SyncContext
            {
                sourceRoot = sourceRoot.transform,
                targetRoot = targetRoot.transform,
                isRuntimeV2Output = targetPath.IndexOf("/Canonical/", StringComparison.OrdinalIgnoreCase) < 0,
            };

            CollectBakeProtectedTargetObjects(targetRoot.transform, context);
            RegisterTransformPair(sourceRoot.transform, targetRoot.transform, context);
            CopyGameObjectMetadata(sourceRoot, targetRoot, copyName: false);
            SyncChildren(sourceRoot.transform, targetRoot.transform, context);
            EnsureAndMapComponents(context);
            CopyMappedComponents(context);
            RemoveUnmappedComponents(context);
            DungeonV2NavigationPrefabAuthoring.NormalizeRoomNavigationRoot(targetRoot);

            PrefabUtility.SaveAsPrefabAsset(targetRoot, targetPath, out bool saved);
            if (!saved)
                throw new InvalidOperationException("PrefabUtility.SaveAsPrefabAsset returned false.");

            return context.stats;
        }
        finally
        {
            if (sourceRoot != null)
                PrefabUtility.UnloadPrefabContents(sourceRoot);
            if (targetRoot != null)
                PrefabUtility.UnloadPrefabContents(targetRoot);
        }
    }

    private static void SyncChildren(Transform sourceParent, Transform targetParent, SyncContext context)
    {
        var availableTargets = new List<Transform>();
        for (int i = 0; i < targetParent.childCount; i++)
            availableTargets.Add(targetParent.GetChild(i));

        var usedTargetIds = new HashSet<int>();
        int targetSiblingIndex = 0;
        for (int sourceIndex = 0; sourceIndex < sourceParent.childCount; sourceIndex++)
        {
            Transform sourceChild = sourceParent.GetChild(sourceIndex);
            if (IsSourceBakeOwnedObject(sourceChild))
                continue;

            Transform targetChild = null;
            for (int targetIndex = 0; targetIndex < availableTargets.Count; targetIndex++)
            {
                Transform candidate = availableTargets[targetIndex];
                if (candidate == null || usedTargetIds.Contains(candidate.GetInstanceID()))
                    continue;
                if (context.protectedTargetTransformIds.Contains(candidate.GetInstanceID()))
                    continue;
                if (!string.Equals(candidate.name, sourceChild.name, StringComparison.Ordinal))
                    continue;

                targetChild = candidate;
                break;
            }

            if (targetChild == null)
            {
                var created = new GameObject(sourceChild.name);
                targetChild = created.transform;
                targetChild.SetParent(targetParent, false);
                context.stats.createdObjects++;
            }

            usedTargetIds.Add(targetChild.GetInstanceID());
            targetChild.SetSiblingIndex(Mathf.Min(targetSiblingIndex, targetParent.childCount - 1));
            targetSiblingIndex++;
            CopyTransform(sourceChild, targetChild);
            CopyGameObjectMetadata(sourceChild.gameObject, targetChild.gameObject, copyName: true);
            RegisterTransformPair(sourceChild, targetChild, context);
            SyncChildren(sourceChild, targetChild, context);
        }

        for (int i = availableTargets.Count - 1; i >= 0; i--)
        {
            Transform candidate = availableTargets[i];
            if (candidate == null || usedTargetIds.Contains(candidate.GetInstanceID()))
                continue;

            if (context.protectedTargetTransformIds.Contains(candidate.GetInstanceID()))
            {
                context.stats.preservedBakeObjects++;
                continue;
            }

            UnityEngine.Object.DestroyImmediate(candidate.gameObject);
            context.stats.removedObjects++;
        }
    }

    private static void RegisterTransformPair(Transform source, Transform target, SyncContext context)
    {
        context.transformPairs.Add(new KeyValuePair<Transform, Transform>(source, target));
        context.objectMap[source.GetInstanceID()] = target;
        context.objectMap[source.gameObject.GetInstanceID()] = target.gameObject;
    }

    private static void EnsureAndMapComponents(SyncContext context)
    {
        for (int pairIndex = 0; pairIndex < context.transformPairs.Count; pairIndex++)
        {
            Transform sourceTransform = context.transformPairs[pairIndex].Key;
            Transform targetTransform = context.transformPairs[pairIndex].Value;
            Component[] sourceComponents = sourceTransform.GetComponents<Component>();
            var sourceTypeUseCount = new Dictionary<Type, int>();

            for (int sourceIndex = 0; sourceIndex < sourceComponents.Length; sourceIndex++)
            {
                Component sourceComponent = sourceComponents[sourceIndex];
                if (sourceComponent == null || sourceComponent is Transform || IsPreservedBakeComponent(sourceComponent))
                    continue;

                Type type = sourceComponent.GetType();
                sourceTypeUseCount.TryGetValue(type, out int occurrence);
                sourceTypeUseCount[type] = occurrence + 1;

                Component[] targetComponents = targetTransform.GetComponents(type);
                Component targetComponent = occurrence < targetComponents.Length
                    ? targetComponents[occurrence]
                    : targetTransform.gameObject.AddComponent(type);

                if (targetComponent == null)
                    throw new InvalidOperationException("Could not add component " + type.FullName + " at " + sourceTransform.name);

                if (occurrence >= targetComponents.Length)
                    context.stats.addedComponents++;

                context.objectMap[sourceComponent.GetInstanceID()] = targetComponent;
                context.mappedTargetComponentIds.Add(targetComponent.GetInstanceID());
            }
        }
    }

    private static void CopyMappedComponents(SyncContext context)
    {
        for (int pairIndex = 0; pairIndex < context.transformPairs.Count; pairIndex++)
        {
            Transform sourceTransform = context.transformPairs[pairIndex].Key;
            Component[] sourceComponents = sourceTransform.GetComponents<Component>();
            for (int sourceIndex = 0; sourceIndex < sourceComponents.Length; sourceIndex++)
            {
                Component sourceComponent = sourceComponents[sourceIndex];
                if (sourceComponent == null || sourceComponent is Transform || IsPreservedBakeComponent(sourceComponent))
                    continue;
                if (!context.objectMap.TryGetValue(sourceComponent.GetInstanceID(), out UnityEngine.Object mapped) ||
                    !(mapped is Component targetComponent))
                {
                    continue;
                }

                bool hasRendererBakeState = targetComponent is Renderer;
                RendererBakeState rendererBakeState = default;
                if (hasRendererBakeState)
                    rendererBakeState = CaptureRendererBakeState((Renderer)targetComponent);

                bool hasLightBakeState = targetComponent is Light;
                LightBakingOutput lightBakeState = default;
                if (hasLightBakeState)
                    lightBakeState = ((Light)targetComponent).bakingOutput;

                bool hasV2AllowRotationState = sourceComponent is DunGen.Tile;

                EditorUtility.CopySerialized(sourceComponent, targetComponent);
                RemapSerializedObjectReferences(sourceComponent, targetComponent, context);

                if (hasRendererBakeState)
                    RestoreRendererBakeState((Renderer)targetComponent, rendererBakeState);
                if (hasLightBakeState)
                    ((Light)targetComponent).bakingOutput = lightBakeState;
                if (hasV2AllowRotationState)
                    WriteSerializedBool(targetComponent, "AllowRotation", context.isRuntimeV2Output);

                EditorUtility.SetDirty(targetComponent);
                context.stats.copiedComponents++;
            }
        }
    }

    private static void RemoveUnmappedComponents(SyncContext context)
    {
        for (int pairIndex = 0; pairIndex < context.transformPairs.Count; pairIndex++)
        {
            Transform targetTransform = context.transformPairs[pairIndex].Value;
            Component[] targetComponents = targetTransform.GetComponents<Component>();
            for (int targetIndex = targetComponents.Length - 1; targetIndex >= 0; targetIndex--)
            {
                Component targetComponent = targetComponents[targetIndex];
                if (targetComponent == null || targetComponent is Transform || IsPreservedBakeComponent(targetComponent))
                    continue;
                if (context.mappedTargetComponentIds.Contains(targetComponent.GetInstanceID()))
                    continue;

                UnityEngine.Object.DestroyImmediate(targetComponent);
                context.stats.removedComponents++;
            }
        }
    }

    private static void RemapSerializedObjectReferences(
        Component source,
        Component target,
        SyncContext context)
    {
        var sourceObject = new SerializedObject(source);
        var targetObject = new SerializedObject(target);
        SerializedProperty sourceProperty = sourceObject.GetIterator();
        bool enterChildren = true;
        while (sourceProperty.Next(enterChildren))
        {
            enterChildren = true;
            if (sourceProperty.propertyType != SerializedPropertyType.ObjectReference)
                continue;

            UnityEngine.Object sourceReference = sourceProperty.objectReferenceValue;
            if (sourceReference == null)
                continue;

            SerializedProperty targetProperty = targetObject.FindProperty(sourceProperty.propertyPath);
            if (targetProperty == null || targetProperty.propertyType != SerializedPropertyType.ObjectReference)
                continue;

            if (context.objectMap.TryGetValue(sourceReference.GetInstanceID(), out UnityEngine.Object targetReference))
            {
                targetProperty.objectReferenceValue = targetReference;
            }
            else if (IsObjectInsideRoot(sourceReference, context.sourceRoot))
            {
                targetProperty.objectReferenceValue = null;
            }
        }

        targetObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CollectBakeProtectedTargetObjects(Transform targetRoot, SyncContext context)
    {
        Component[] components = targetRoot.GetComponentsInChildren<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null || !IsPreservedBakeComponent(component))
                continue;

            var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.GetIterator();
            bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = true;
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                UnityEngine.Object reference = property.objectReferenceValue;
                Transform referencedTransform = TransformOf(reference);
                if (referencedTransform != null && referencedTransform.IsChildOf(targetRoot))
                    ProtectTransformAndAncestors(referencedTransform, targetRoot, context.protectedTargetTransformIds);
            }
        }

        Transform[] transforms = targetRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform transform = transforms[i];
            if (transform == targetRoot || transform.GetComponent<ReflectionProbe>() == null)
                continue;
            if (!IsPowerVariantName(transform.name))
                continue;

            ProtectTransformAndAncestors(transform, targetRoot, context.protectedTargetTransformIds);
        }
    }

    private static void ProtectTransformAndAncestors(
        Transform transform,
        Transform root,
        HashSet<int> protectedIds)
    {
        Transform current = transform;
        while (current != null && current != root)
        {
            protectedIds.Add(current.GetInstanceID());
            current = current.parent;
        }
    }

    private static bool IsSourceBakeOwnedObject(Transform transform)
    {
        return transform != null && transform.GetComponent<ReflectionProbe>() != null;
    }

    private static bool IsPreservedBakeComponent(Component component)
    {
        return component is DungeonTilePowerBakeSet ||
               component is DungeonTileLightmapSwitcher ||
               component is DungeonTileRotationSelectorV2;
    }

    private static bool IsPowerVariantName(string objectName)
    {
        return objectName != null &&
               (objectName.EndsWith("_P0", StringComparison.OrdinalIgnoreCase) ||
                objectName.EndsWith("_P100", StringComparison.OrdinalIgnoreCase));
    }

    private static Transform TransformOf(UnityEngine.Object value)
    {
        if (value is GameObject gameObject)
            return gameObject.transform;
        if (value is Component component)
            return component.transform;
        return null;
    }

    private static bool IsObjectInsideRoot(UnityEngine.Object value, Transform root)
    {
        Transform transform = TransformOf(value);
        return transform != null && (transform == root || transform.IsChildOf(root));
    }

    private static void CopyTransform(Transform source, Transform target)
    {
        target.localPosition = source.localPosition;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    private static void CopyGameObjectMetadata(GameObject source, GameObject target, bool copyName)
    {
        if (copyName)
            target.name = source.name;
        target.layer = source.layer;
        target.tag = source.tag;
        target.SetActive(source.activeSelf);
        GameObjectUtility.SetStaticEditorFlags(target, GameObjectUtility.GetStaticEditorFlags(source));
    }

    private static RendererBakeState CaptureRendererBakeState(Renderer renderer)
    {
        return new RendererBakeState
        {
            lightmapIndex = renderer.lightmapIndex,
            lightmapScaleOffset = renderer.lightmapScaleOffset,
            realtimeLightmapIndex = renderer.realtimeLightmapIndex,
            realtimeLightmapScaleOffset = renderer.realtimeLightmapScaleOffset,
        };
    }

    private static void RestoreRendererBakeState(Renderer renderer, RendererBakeState state)
    {
        renderer.lightmapIndex = state.lightmapIndex;
        renderer.lightmapScaleOffset = state.lightmapScaleOffset;
        renderer.realtimeLightmapIndex = state.realtimeLightmapIndex;
        renderer.realtimeLightmapScaleOffset = state.realtimeLightmapScaleOffset;
    }

    private static void WriteSerializedBool(Component component, string propertyName, bool value)
    {
        var serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.Boolean)
            return;

        property.boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Dictionary<string, string> CaptureBakeAssetHashes(string testRoomRoot)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string absoluteRoot = AssetPathToAbsolute(testRoomRoot);
        if (!Directory.Exists(absoluteRoot))
            return result;

        string[] files = Directory.GetFiles(absoluteRoot, "*.asset", SearchOption.AllDirectories);
        using (SHA256 sha = SHA256.Create())
        {
            for (int i = 0; i < files.Length; i++)
            {
                string normalized = files[i].Replace('\\', '/');
                if (normalized.IndexOf("/History/", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                byte[] hash = sha.ComputeHash(File.ReadAllBytes(files[i]));
                result[AbsoluteToAssetPath(files[i])] = BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        return result;
    }

    private static bool HashesMatch(
        Dictionary<string, string> before,
        Dictionary<string, string> after,
        out string difference)
    {
        difference = string.Empty;
        if (before.Count != after.Count)
        {
            difference = "asset count " + before.Count + " -> " + after.Count;
            return false;
        }

        foreach (KeyValuePair<string, string> pair in before)
        {
            if (!after.TryGetValue(pair.Key, out string current))
            {
                difference = "missing " + pair.Key;
                return false;
            }

            if (!string.Equals(pair.Value, current, StringComparison.Ordinal))
            {
                difference = "content changed " + pair.Key;
                return false;
            }
        }

        return true;
    }

    private static string BuildBakeCompatibilityReport(string testRoomRoot, string[] targetPaths)
    {
        string[] bakeGuids = AssetDatabase.FindAssets("t:DungeonTileBakeData", new[] { testRoomRoot });
        var bakePaths = new List<string>();
        for (int i = 0; i < bakeGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(bakeGuids[i]);
            if (path.IndexOf("/History/", StringComparison.OrdinalIgnoreCase) < 0)
                bakePaths.Add(path);
        }

        var report = new StringBuilder();
        for (int targetIndex = 0; targetIndex < targetPaths.Length; targetIndex++)
        {
            GameObject target = AssetDatabase.LoadAssetAtPath<GameObject>(targetPaths[targetIndex]);
            if (target == null)
                continue;

            Dictionary<string, int> rendererCounts = BuildRendererPathCounts(target.transform);
            int worstMissingPathEntries = 0;
            int worstExtraRenderers = 0;
            for (int bakeIndex = 0; bakeIndex < bakePaths.Count; bakeIndex++)
            {
                DungeonTileBakeData data = AssetDatabase.LoadAssetAtPath<DungeonTileBakeData>(bakePaths[bakeIndex]);
                if (data == null)
                    continue;

                Dictionary<string, int> entryCounts = BuildBakeEntryPathCounts(data);
                worstMissingPathEntries = Mathf.Max(
                    worstMissingPathEntries,
                    CountPositiveDifference(entryCounts, rendererCounts));
                worstExtraRenderers = Mathf.Max(
                    worstExtraRenderers,
                    CountPositiveDifference(rendererCounts, entryCounts));
            }

            bool stale = worstMissingPathEntries > 0;
            report.AppendLine(
                "  BAKEDATA_COMPAT " + targetPaths[targetIndex] +
                " | status=" + (stale ? "REBAKE_REQUIRED" : "paths-resolve") +
                ", missingEntryTargets=" + worstMissingPathEntries +
                ", renderersWithoutEntry=" + worstExtraRenderers +
                ", bakeAssets=" + bakePaths.Count);
        }

        return report.ToString();
    }

    private static Dictionary<string, int> BuildRendererPathCounts(Transform root)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            Increment(result, RelativePath(root, renderers[i].transform));
        return result;
    }

    private static Dictionary<string, int> BuildBakeEntryPathCounts(DungeonTileBakeData data)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        DungeonTileBakeData.RendererBakeEntry[] entries =
            data.rendererEntries ?? Array.Empty<DungeonTileBakeData.RendererBakeEntry>();
        for (int i = 0; i < entries.Length; i++)
            Increment(result, entries[i].relativePath ?? string.Empty);
        return result;
    }

    private static void Increment(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out int value);
        counts[key] = value + 1;
    }

    private static int CountPositiveDifference(
        Dictionary<string, int> left,
        Dictionary<string, int> right)
    {
        int count = 0;
        foreach (KeyValuePair<string, int> pair in left)
        {
            right.TryGetValue(pair.Key, out int rightCount);
            if (pair.Value > rightCount)
                count += pair.Value - rightCount;
        }
        return count;
    }

    private static string RelativePath(Transform root, Transform target)
    {
        if (target == root)
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

    private static bool IsTileModifiedPrefab(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            return false;

        string directory = NormalizeAssetPath(Path.GetDirectoryName(path));
        return string.Equals(directory, TileModifiedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAssetPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').Trim();
    }

    private static string AssetPathToAbsolute(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string AbsoluteToAssetPath(string absolutePath)
    {
        string normalizedAbsolute = Path.GetFullPath(absolutePath).Replace('\\', '/');
        string normalizedProject = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/').TrimEnd('/');
        return normalizedAbsolute.StartsWith(normalizedProject + "/", StringComparison.OrdinalIgnoreCase)
            ? normalizedAbsolute.Substring(normalizedProject.Length + 1)
            : normalizedAbsolute;
    }
}

/// <summary>
/// Repairs the StartRoom authoring mistake where two Bench_01 roots each contain one
/// LOD0 mesh and two LOD1 meshes. The result is four Bench_01 roots, each with one
/// aligned Bench_LOD0/Bench_LOD1 pair, a valid LODGroup, and one footprint blocker.
/// </summary>
public static class StartRoomBenchLodNormalizer
{
    private const string StartRoomSourcePath =
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/StartRoom.prefab";

    private static void NormalizeMenu()
    {
        string result = NormalizeStartRoomBenchesCli();
        Debug.Log(result);
        EditorUtility.DisplayDialog("StartRoom Bench Normalizer", result, "OK");
    }

    public static string NormalizeStartRoomBenchesCli()
    {
        if (EditorApplication.isPlaying)
            return "ERROR: Exit Play Mode before normalizing StartRoom benches.";

        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(StartRoomSourcePath);
            List<Transform> benchRoots = FindBenchRoots(root.transform);
            if (benchRoots.Count == 4 && ValidateNormalizedBenches(benchRoots, out string existingReport))
                return "PASS: StartRoom already has four normalized Bench_01 roots.\n" + existingReport;

            if (benchRoots.Count != 2)
                return "FAIL: expected 2 legacy or 4 normalized Bench_01 roots, found " + benchRoots.Count;

            for (int i = 0; i < benchRoots.Count; i++)
            {
                if (!ValidateLegacyBench(benchRoots[i], out string problem))
                    return "FAIL: " + RelativePath(root.transform, benchRoots[i]) + ": " + problem;
            }

            int createdRoots = 0;
            for (int i = 0; i < benchRoots.Count; i++)
            {
                Transform legacyRoot = benchRoots[i];
                Transform lod0 = DirectChildren(legacyRoot, "Bench_LOD0", exactName: true).Single();
                List<Transform> lod1Children = DirectChildren(legacyRoot, "Bench_LOD1", exactName: false);
                Transform baseLod1 = lod1Children
                    .OrderBy(candidate => LocalAlignmentScore(lod0, candidate))
                    .First();
                Transform extraLod1 = lod1Children.First(candidate => candidate != baseLod1);

                GameObject extraRootObject = new GameObject("Bench_01");
                Transform extraRoot = extraRootObject.transform;
                extraRoot.SetParent(legacyRoot.parent, false);
                extraRoot.localPosition = legacyRoot.localPosition;
                extraRoot.localRotation = legacyRoot.localRotation;
                extraRoot.localScale = legacyRoot.localScale;
                extraRoot.SetSiblingIndex(legacyRoot.GetSiblingIndex() + 1);
                CopyGameObjectMetadata(legacyRoot.gameObject, extraRootObject);

                Vector3 extraLocalPosition = extraLod1.localPosition;
                Quaternion extraLocalRotation = extraLod1.localRotation;
                Vector3 extraLocalScale = extraLod1.localScale;

                GameObject extraLod0Object = UnityEngine.Object.Instantiate(lod0.gameObject);
                extraLod0Object.name = "Bench_LOD0";
                Transform extraLod0 = extraLod0Object.transform;
                extraLod0.SetParent(extraRoot, false);
                extraLod0.localPosition = extraLocalPosition;
                extraLod0.localRotation = extraLocalRotation;
                extraLod0.localScale = extraLocalScale;

                extraLod1.SetParent(extraRoot, false);
                extraLod1.localPosition = extraLocalPosition;
                extraLod1.localRotation = extraLocalRotation;
                extraLod1.localScale = extraLocalScale;
                extraLod1.name = "Bench_LOD1";

                lod0.name = "Bench_LOD0";
                baseLod1.name = "Bench_LOD1";
                lod0.SetSiblingIndex(0);
                baseLod1.SetSiblingIndex(1);
                extraLod0.SetSiblingIndex(0);
                extraLod1.SetSiblingIndex(1);

                LODGroup legacyGroup = legacyRoot.GetComponent<LODGroup>();
                LODGroup extraGroup = extraRootObject.AddComponent<LODGroup>();
                if (legacyGroup != null)
                    EditorUtility.CopySerialized(legacyGroup, extraGroup);

                ConfigureLodGroup(legacyRoot.gameObject, lod0, baseLod1, legacyGroup);
                ConfigureLodGroup(extraRootObject, extraLod0, extraLod1, extraGroup);

                DungeonNavMeshFootprintBlocker legacyBlocker =
                    EnsureFootprintBlocker(legacyRoot.gameObject, null);
                EnsureFootprintBlocker(extraRootObject, legacyBlocker);
                createdRoots++;
            }

            List<Transform> normalizedRoots = FindBenchRoots(root.transform);
            if (!ValidateNormalizedBenches(normalizedRoots, out string validationReport))
                return "FAIL after normalization:\n" + validationReport;

            PrefabUtility.SaveAsPrefabAsset(root, StartRoomSourcePath, out bool saved);
            if (!saved)
                return "FAIL: could not save " + StartRoomSourcePath;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return "PASS: created=" + createdRoots + ", Bench_01=4\n" + validationReport;
        }
        catch (Exception exception)
        {
            return "FAIL: " + exception;
        }
        finally
        {
            if (root != null)
                PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static string ValidateStartRoomBenchesCli()
    {
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(StartRoomSourcePath);
            List<Transform> benches = FindBenchRoots(root.transform);
            bool passed = ValidateNormalizedBenches(benches, out string report);
            return (passed ? "PASS" : "FAIL") + ": " + report;
        }
        finally
        {
            if (root != null)
                PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool ValidateLegacyBench(Transform root, out string problem)
    {
        int lod0Count = DirectChildren(root, "Bench_LOD0", exactName: true).Count;
        int lod1Count = DirectChildren(root, "Bench_LOD1", exactName: false).Count;
        if (lod0Count != 1 || lod1Count != 2)
        {
            problem = "expected one Bench_LOD0 and two Bench_LOD1 children, found " +
                      lod0Count + "/" + lod1Count;
            return false;
        }

        problem = string.Empty;
        return true;
    }

    private static bool ValidateNormalizedBenches(List<Transform> roots, out string report)
    {
        var builder = new StringBuilder();
        bool passed = roots.Count == 4;
        builder.AppendLine("Bench_01 roots=" + roots.Count);
        for (int i = 0; i < roots.Count; i++)
        {
            Transform root = roots[i];
            List<Transform> lod0Children = DirectChildren(root, "Bench_LOD0", exactName: true);
            List<Transform> lod1Children = DirectChildren(root, "Bench_LOD1", exactName: true);
            LODGroup group = root.GetComponent<LODGroup>();
            DungeonNavMeshFootprintBlocker blocker = root.GetComponent<DungeonNavMeshFootprintBlocker>();
            bool aligned = lod0Children.Count == 1 && lod1Children.Count == 1 &&
                           ApproximatelySameLocalTransform(lod0Children[0], lod1Children[0]);
            bool lodGroupValid = group != null && group.GetLODs().Length == 2 &&
                                 group.GetLODs()[0].renderers.Length > 0 &&
                                 group.GetLODs()[1].renderers.Length > 0;
            bool itemPassed = lod0Children.Count == 1 && lod1Children.Count == 1 &&
                              aligned && lodGroupValid && blocker != null;
            passed &= itemPassed;
            builder.AppendLine(
                "  [" + i + "] " + RelativePath(roots[0].root, root) +
                " lod0=" + lod0Children.Count +
                " lod1=" + lod1Children.Count +
                " aligned=" + aligned +
                " lodGroup=" + lodGroupValid +
                " footprint=" + (blocker != null));
        }

        report = builder.ToString();
        return passed;
    }

    private static void ConfigureLodGroup(
        GameObject root,
        Transform lod0,
        Transform lod1,
        LODGroup existingGroup)
    {
        LODGroup group = existingGroup != null ? existingGroup : root.AddComponent<LODGroup>();
        LOD[] previous = group.GetLODs();
        float lod0Height = previous.Length > 0 ? previous[0].screenRelativeTransitionHeight : 0.25f;
        float lod1Height = previous.Length > 1 ? previous[1].screenRelativeTransitionHeight : 0.01f;
        float lod0Fade = previous.Length > 0 ? previous[0].fadeTransitionWidth : 0f;
        float lod1Fade = previous.Length > 1 ? previous[1].fadeTransitionWidth : 0f;

        var first = new LOD(lod0Height, lod0.GetComponentsInChildren<Renderer>(true));
        var second = new LOD(lod1Height, lod1.GetComponentsInChildren<Renderer>(true));
        first.fadeTransitionWidth = lod0Fade;
        second.fadeTransitionWidth = lod1Fade;
        group.SetLODs(new[] { first, second });
        group.RecalculateBounds();
        group.enabled = true;
        EditorUtility.SetDirty(group);
    }

    private static DungeonNavMeshFootprintBlocker EnsureFootprintBlocker(
        GameObject target,
        DungeonNavMeshFootprintBlocker copyFrom)
    {
        DungeonNavMeshFootprintBlocker blocker = target.GetComponent<DungeonNavMeshFootprintBlocker>();
        if (blocker == null)
            blocker = target.AddComponent<DungeonNavMeshFootprintBlocker>();
        if (copyFrom != null)
            EditorUtility.CopySerialized(copyFrom, blocker);
        EditorUtility.SetDirty(blocker);
        return blocker;
    }

    private static List<Transform> FindBenchRoots(Transform root)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .Where(transform => string.Equals(transform.name, "Bench_01", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<Transform> DirectChildren(Transform parent, string name, bool exactName)
    {
        var result = new List<Transform>();
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            bool matches = exactName
                ? string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase)
                : child.name.StartsWith(name, StringComparison.OrdinalIgnoreCase);
            if (matches)
                result.Add(child);
        }
        return result;
    }

    private static float LocalAlignmentScore(Transform left, Transform right)
    {
        return (left.localPosition - right.localPosition).sqrMagnitude +
               Quaternion.Angle(left.localRotation, right.localRotation) / 180f +
               (left.localScale - right.localScale).sqrMagnitude;
    }

    private static bool ApproximatelySameLocalTransform(Transform left, Transform right)
    {
        return (left.localPosition - right.localPosition).sqrMagnitude < 0.000001f &&
               Quaternion.Angle(left.localRotation, right.localRotation) < 0.01f &&
               (left.localScale - right.localScale).sqrMagnitude < 0.000001f;
    }

    private static void CopyGameObjectMetadata(GameObject source, GameObject target)
    {
        target.layer = source.layer;
        target.tag = source.tag;
        target.SetActive(source.activeSelf);
        GameObjectUtility.SetStaticEditorFlags(target, GameObjectUtility.GetStaticEditorFlags(source));
    }

    private static string RelativePath(Transform root, Transform target)
    {
        if (target == root)
            return root.name;

        var names = new Stack<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            names.Push(current.name);
            current = current.parent;
        }
        return root.name + "/" + string.Join("/", names);
    }
}
