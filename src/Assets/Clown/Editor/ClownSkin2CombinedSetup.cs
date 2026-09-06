using System.Text;
using UnityEditor;
using UnityEngine;

public static class ClownSkin2CombinedSetup
{
    private const string ModelPath = "Assets/Clown/Base mesh/ClownSkin2_Combined.fbx";

    [MenuItem("Tools/Clown/Setup Combined Skin2 Model")]
    public static void SetupMenu()
    {
        Debug.Log(Run());
    }

    public static string Run()
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
            return $"ModelImporter not found: {ModelPath}";

        importer.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName, ModelImporterMaterialSearch.Everywhere);
        importer.SaveAndReimport();

        return Report();
    }

    public static string Report()
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
            return $"ModelImporter not found: {ModelPath}";

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
            return $"Model not found after import: {ModelPath}";

        var renderer = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (renderer == null)
            return $"SkinnedMeshRenderer not found in: {ModelPath}";

        var sb = new StringBuilder();
        sb.AppendLine("ClownSkin2CombinedSetup complete");
        sb.AppendLine($"Model: {model.name}");
        sb.AppendLine($"Renderer: {renderer.name}");
        sb.AppendLine($"Mesh: {(renderer.sharedMesh != null ? renderer.sharedMesh.name : "null")}");
        sb.AppendLine($"Importer Material Name Mode: {importer.materialName}");
        sb.AppendLine($"Importer Material Search Mode: {importer.materialSearch}");
        sb.AppendLine($"Material Count: {renderer.sharedMaterials.Length}");

        for (var i = 0; i < renderer.sharedMaterials.Length; i++)
        {
            var material = renderer.sharedMaterials[i];
            sb.AppendLine($"{i}: {(material != null ? material.name : "null")}");
        }

        return sb.ToString();
    }
}
