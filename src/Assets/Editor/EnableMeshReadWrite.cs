#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

// Editor utility: set Read/Write enabled on selected model assets or on all models in project.
public static class EnableMeshReadWrite
{
    [MenuItem("Tools/Enable Read/Write on Selected Models (verbose)")]
    public static void EnableReadWriteOnSelectedVerbose()
    {
        var guids = Selection.assetGUIDs;
        if (guids == null || guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Enable Read/Write on Models",
                "No assets selected. Use 'Tools/Enable Read/Write on All Models' to process the entire project.",
                "OK");
            return;
        }

        ProcessGuids(guids, verbose: true);
    }

    [MenuItem("Tools/Enable Read/Write on All Models (project-wide, verbose)")]
    public static void EnableReadWriteOnAllModels()
    {
        if (!EditorUtility.DisplayDialog("Enable Read/Write on All Models",
            "This will scan all model assets in the project and enable Read/Write where disabled. This can take time. Continue?", "Yes", "No"))
            return;

        var guids = AssetDatabase.FindAssets("t:Model");
        ProcessGuids(guids, verbose: true);
    }

    static void ProcessGuids(string[] guids, bool verbose = false)
    {
        if (guids == null) guids = new string[0];
        int examined = 0, changed = 0;
        var processedPaths = new HashSet<string>();

        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            if (string.IsNullOrEmpty(path)) continue;
            if (processedPaths.Contains(path)) continue;
            processedPaths.Add(path);
            examined++;

            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                if (verbose) Debug.Log($"[EnableMeshReadWrite] Skipping non-model asset: {path}");
                continue;
            }

            if (!importer.isReadable)
            {
                if (verbose) Debug.Log($"[EnableMeshReadWrite] Enabling Read/Write on: {path}");
                importer.isReadable = true;
                try { importer.SaveAndReimport(); }
                catch (System.Exception ex) { Debug.LogWarning($"[EnableMeshReadWrite] Reimport failed for {path}: {ex.Message}"); }
                changed++;
            }
            else
            {
                if (verbose) Debug.Log($"[EnableMeshReadWrite] Already readable: {path}");
            }
        }

        EditorUtility.DisplayDialog("Enable Read/Write on Models", $"Done. Examined {examined} model(s). Updated {changed} model(s).", "OK");
        Debug.Log($"[EnableMeshReadWrite] Done. Examined={examined} Updated={changed}");
    }
}
#endif
