using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class NineSliceBaker
{
    public const string GeneratedFolder = "Assets/UI/Inventory/Generated";

    private readonly struct SpriteDefinition
    {
        public readonly string FileName;
        public readonly int Width;
        public readonly int Height;
        public readonly int Radius;
        public readonly int StrokeWidth;
        public readonly Vector4 Border;

        public SpriteDefinition(
            string fileName,
            int width,
            int height,
            int radius,
            int strokeWidth,
            Vector4 border)
        {
            FileName = fileName;
            Width = width;
            Height = height;
            Radius = radius;
            StrokeWidth = strokeWidth;
            Border = border;
        }
    }

    private static readonly SpriteDefinition[] Definitions =
    {
        new SpriteDefinition("ui_panel_r10.png", 48, 48, 10, 0, new Vector4(12f, 12f, 12f, 12f)),
        new SpriteDefinition("ui_panel_outline_r10.png", 48, 48, 10, 1, new Vector4(12f, 12f, 12f, 12f)),
        new SpriteDefinition("ui_slot_r6.png", 32, 32, 6, 0, new Vector4(8f, 8f, 8f, 8f)),
        new SpriteDefinition("ui_slot_outline_r6.png", 32, 32, 6, 1, new Vector4(8f, 8f, 8f, 8f)),
        new SpriteDefinition("ui_button_r6.png", 32, 32, 6, 0, new Vector4(8f, 8f, 8f, 8f)),
        new SpriteDefinition("ui_divider_1px.png", 4, 4, 0, 0, Vector4.zero)
    };

    [MenuItem("Tools/UI/Bake Inventory Sprites")]
    public static void Bake()
    {
        bool createdFolder = !Directory.Exists(GeneratedFolder);
        Directory.CreateDirectory(GeneratedFolder);
        if (createdFolder)
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        foreach (SpriteDefinition definition in Definitions)
            BakeSprite(definition);

        AssetDatabase.SaveAssets();
    }

    private static void BakeSprite(SpriteDefinition definition)
    {
        string assetPath = $"{GeneratedFolder}/{definition.FileName}";
        byte[] pngBytes = BuildPng(definition);
        bool pngChanged = !File.Exists(assetPath) || !BytesEqual(File.ReadAllBytes(assetPath), pngBytes);

        if (pngChanged)
            File.WriteAllBytes(assetPath, pngBytes);

        if (pngChanged || AssetImporter.GetAtPath(assetPath) == null)
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

        ConfigureImporter(assetPath, definition.Border);
    }

    private static byte[] BuildPng(SpriteDefinition definition)
    {
        var texture = new Texture2D(
            definition.Width,
            definition.Height,
            TextureFormat.RGBA32,
            false,
            false)
        {
            name = Path.GetFileNameWithoutExtension(definition.FileName),
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        try
        {
            var pixels = new Color32[definition.Width * definition.Height];
            Color32 white = new Color32(255, 255, 255, 255);
            Color32 clear = new Color32(255, 255, 255, 0);

            for (int y = 0; y < definition.Height; y++)
            {
                for (int x = 0; x < definition.Width; x++)
                {
                    bool inOuter = IsInsideRoundedRect(
                        x,
                        y,
                        definition.Width,
                        definition.Height,
                        definition.Radius,
                        0);
                    bool inInner = definition.StrokeWidth > 0 && IsInsideRoundedRect(
                        x,
                        y,
                        definition.Width,
                        definition.Height,
                        Mathf.Max(0, definition.Radius - definition.StrokeWidth),
                        definition.StrokeWidth);

                    pixels[y * definition.Width + x] = inOuter && !inInner ? white : clear;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture.EncodeToPNG();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static bool IsInsideRoundedRect(
        int x,
        int y,
        int width,
        int height,
        int radius,
        int inset)
    {
        float px = x + 0.5f;
        float py = y + 0.5f;
        float minX = inset;
        float minY = inset;
        float maxX = width - inset;
        float maxY = height - inset;

        if (px < minX || px > maxX || py < minY || py > maxY)
            return false;

        if (radius <= 0)
            return true;

        float centerX = Mathf.Clamp(px, minX + radius, maxX - radius);
        float centerY = Mathf.Clamp(py, minY + radius, maxY - radius);
        float dx = px - centerX;
        float dy = py - centerY;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static void ConfigureImporter(string assetPath, Vector4 border)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException($"Texture importer was not created for {assetPath}.");

        bool changed = false;
        SetIfDifferent(ref changed, importer.textureType, TextureImporterType.Sprite, value => importer.textureType = value);
        SetIfDifferent(ref changed, importer.textureShape, TextureImporterShape.Texture2D, value => importer.textureShape = value);
        SetIfDifferent(ref changed, importer.spriteImportMode, SpriteImportMode.Single, value => importer.spriteImportMode = value);
        SetIfDifferent(ref changed, importer.spriteBorder, border, value => importer.spriteBorder = value);
        SetIfDifferent(ref changed, importer.spritePixelsPerUnit, 100f, value => importer.spritePixelsPerUnit = value);
        SetIfDifferent(ref changed, importer.filterMode, FilterMode.Bilinear, value => importer.filterMode = value);
        SetIfDifferent(ref changed, importer.textureCompression, TextureImporterCompression.Uncompressed, value => importer.textureCompression = value);
        SetIfDifferent(ref changed, importer.crunchedCompression, false, value => importer.crunchedCompression = value);
        SetIfDifferent(ref changed, importer.alphaSource, TextureImporterAlphaSource.FromInput, value => importer.alphaSource = value);
        SetIfDifferent(ref changed, importer.alphaIsTransparency, true, value => importer.alphaIsTransparency = value);
        SetIfDifferent(ref changed, importer.mipmapEnabled, false, value => importer.mipmapEnabled = value);
        SetIfDifferent(ref changed, importer.isReadable, false, value => importer.isReadable = value);
        SetIfDifferent(ref changed, importer.sRGBTexture, true, value => importer.sRGBTexture = value);
        SetIfDifferent(ref changed, importer.wrapMode, TextureWrapMode.Clamp, value => importer.wrapMode = value);
        SetIfDifferent(ref changed, importer.npotScale, TextureImporterNPOTScale.None, value => importer.npotScale = value);

        if (changed)
            importer.SaveAndReimport();
    }

    private static void SetIfDifferent<T>(ref bool changed, T current, T desired, Action<T> setter)
    {
        if (Equals(current, desired))
            return;

        setter(desired);
        changed = true;
    }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
            return false;

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }
}
