using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

internal static class VitalsFontAssetBuilder
{
    private const string MenuPath = "Tools/StillWorking/UI/Build Vitals Font Assets";
    private const string SourceFolder = "Assets/UI/Fonts/Vitals";
    private const string OutputFolder = "Assets/Resources/UI/Fonts/Vitals";
    private const string NumericCharacters = "0123456789";

    private static readonly string[] FontNames =
    {
        "BarlowCondensed-SemiBold",
        "Rajdhani-SemiBold",
        "Oxanium-SemiBold"
    };

    [MenuItem(MenuPath)]
    private static void Build()
    {
        EnsureAssetFolder(OutputFolder);

        for (int i = 0; i < FontNames.Length; i++)
            BuildFontAsset(FontNames[i]);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Vitals Fonts] Built 3 numeric static SDF font assets.");
    }

    private static void BuildFontAsset(string fontName)
    {
        string sourcePath = $"{SourceFolder}/{fontName}.ttf";
        string outputPath = $"{OutputFolder}/{fontName} SDF.asset";

        var importer = AssetImporter.GetAtPath(sourcePath) as TrueTypeFontImporter;
        if (importer == null)
            throw new FileNotFoundException($"Vitals font source was not imported: {sourcePath}");

        if (!importer.includeFontData)
        {
            importer.includeFontData = true;
            importer.SaveAndReimport();
        }

        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
        if (sourceFont == null)
            throw new FileNotFoundException($"Vitals font source could not be loaded: {sourcePath}");

        if (AssetDatabase.LoadMainAssetAtPath(outputPath) != null)
            AssetDatabase.DeleteAsset(outputPath);

        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            90,
            9,
            GlyphRenderMode.SDFAA,
            512,
            512,
            AtlasPopulationMode.Dynamic,
            false);

        if (fontAsset == null)
            throw new System.InvalidOperationException($"TMP could not create a font asset from {sourcePath}");

        fontAsset.name = $"{fontName} SDF";
        if (!fontAsset.TryAddCharacters(NumericCharacters, out string missingCharacters))
        {
            Object.DestroyImmediate(fontAsset);
            throw new System.InvalidOperationException(
                $"TMP could not add all numeric glyphs for {fontName}. Missing: {missingCharacters}");
        }

        fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
        fontAsset.isMultiAtlasTexturesEnabled = false;

        Texture2D atlas = fontAsset.atlasTextures[0];
        atlas.name = $"{fontName} Atlas";
        fontAsset.material.name = $"{fontName} Atlas Material";

        AssetDatabase.CreateAsset(fontAsset, outputPath);
        AssetDatabase.AddObjectToAsset(atlas, fontAsset);
        AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        EditorUtility.SetDirty(fontAsset);

        Debug.Log($"[Vitals Fonts] Built {outputPath} with numeric glyphs only.", fontAsset);
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        string[] segments = folderPath.Split('/');
        string current = segments[0];

        for (int i = 1; i < segments.Length; i++)
        {
            string next = $"{current}/{segments[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[i]);

            current = next;
        }
    }
}
