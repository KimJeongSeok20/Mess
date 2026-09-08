using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Keeps the lightmap variants used by runtime-generated NewPrison tiles in player builds.
/// The generated tiles do not exist in StartMap while Unity collects shader variants, so their
/// LIGHTMAP_ON variants would otherwise be eligible for stripping.
/// </summary>
public sealed class DungeonLightmapShaderVariantCollector : IPreprocessBuildWithReport
{
    private const string CollectionPath = "Assets/Settings/DungeonRuntimeLightmapVariants.shadervariants";
    private static readonly HashSet<string> ReportedVariantFailures = new(StringComparer.Ordinal);

    public int callbackOrder => -1000;

    [MenuItem("Tools/Dungeon/Refresh Runtime Lightmap Shader Variants")]
    public static void RefreshCollection()
    {
        var collection = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(CollectionPath);
        if (collection == null)
        {
            collection = new ShaderVariantCollection();
            AssetDatabase.CreateAsset(collection, CollectionPath);
        }

        collection.Clear();

        var seenMaterials = new HashSet<Material>();
        int attemptedVariants = 0;
        int addedVariants = 0;

        var materialPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (string guid in AssetDatabase.FindAssets("t:DungeonMapList"))
        {
            var catalog = AssetDatabase.LoadAssetAtPath<DungeonMapList>(AssetDatabase.GUIDToAssetPath(guid));
            if (catalog == null || catalog.UsesDeferredLoading) continue;
            foreach (var entry in catalog.Entries)
            {
                if (entry?.flow == null) continue;
                foreach (string dependency in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(entry.flow), true))
                    if (dependency.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) materialPaths.Add(dependency);
            }
        }
        foreach (string path in materialPaths)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null || !seenMaterials.Add(material)) continue;
            AddLightmapVariants(collection, material, ref attemptedVariants, ref addedVariants);
        }

        EnsurePreloaded(collection);
        EditorUtility.SetDirty(collection);
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[DungeonLightmapShaderVariants] materials={seenMaterials.Count} " +
            $"variants={addedVariants}/{attemptedVariants} collection='{CollectionPath}'.",
            collection);
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        RefreshCollection();
    }

    private static void AddLightmapVariants(
        ShaderVariantCollection collection,
        Material material,
        ref int attemptedVariants,
        ref int addedVariants)
    {
        // Imported materials can retain stale keywords from an older shader (for example,
        // _METALLICGLOSSMAP on the Modular Prison ColorChange materials). Passing an
        // undeclared keyword to ShaderVariantCollection throws and used to silently skip
        // every lightmap variant for that material. Keep only the keywords this shader
        // declares, then add the global lightmap keywords below.
        var declaredKeywords = new HashSet<string>(
            material.shader.keywordSpace.keywords.Select(keyword => keyword.name),
            StringComparer.Ordinal);
        var baseKeywords = new HashSet<string>(
            (material.shaderKeywords ?? Array.Empty<string>())
                .Where(declaredKeywords.Contains),
            StringComparer.Ordinal);

        AddVariant(collection, material.shader, baseKeywords, ref attemptedVariants, ref addedVariants);

        baseKeywords.Add("LIGHTMAP_ON");
        AddVariant(collection, material.shader, baseKeywords, ref attemptedVariants, ref addedVariants);

        baseKeywords.Add("DIRLIGHTMAP_COMBINED");
        AddVariant(collection, material.shader, baseKeywords, ref attemptedVariants, ref addedVariants);
    }

    private static void AddVariant(
        ShaderVariantCollection collection,
        Shader shader,
        HashSet<string> keywords,
        ref int attemptedVariants,
        ref int addedVariants)
    {
        attemptedVariants++;

        try
        {
            var variant = new ShaderVariantCollection.ShaderVariant(
                shader,
                PassType.ScriptableRenderPipeline,
                keywords.OrderBy(keyword => keyword, StringComparer.Ordinal).ToArray());

            if (collection.Add(variant))
                addedVariants++;
        }
        catch (ArgumentException exception)
        {
            string message =
                $"[DungeonLightmapShaderVariants] skipped shader='{shader.name}' " +
                $"path='{AssetDatabase.GetAssetPath(shader)}' " +
                $"keywords='{string.Join(" ", keywords.OrderBy(keyword => keyword, StringComparer.Ordinal))}' " +
                $"reason='{exception.Message}'";

            // One report per distinct failure is enough to diagnose an unsupported pass or
            // stale keyword without flooding the build log for every tile material.
            if (ReportedVariantFailures.Add(message))
                Debug.LogWarning(message, shader);
        }
    }

    private static void EnsurePreloaded(ShaderVariantCollection collection)
    {
        var graphicsSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
        if (graphicsSettings == null)
            throw new InvalidOperationException("Could not load ProjectSettings/GraphicsSettings.asset.");

        var serializedSettings = new SerializedObject(graphicsSettings);
        var preloadedShaders = serializedSettings.FindProperty("m_PreloadedShaders");
        if (preloadedShaders == null)
            throw new InvalidOperationException("Could not find GraphicsSettings.m_PreloadedShaders.");

        for (int i = 0; i < preloadedShaders.arraySize; i++)
        {
            if (preloadedShaders.GetArrayElementAtIndex(i).objectReferenceValue == collection)
                return;
        }

        int index = preloadedShaders.arraySize;
        preloadedShaders.InsertArrayElementAtIndex(index);
        preloadedShaders.GetArrayElementAtIndex(index).objectReferenceValue = collection;
        serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
    }
}
