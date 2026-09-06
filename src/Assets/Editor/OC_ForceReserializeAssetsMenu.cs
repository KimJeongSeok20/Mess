// System imports
using System;
using System.Collections.Generic;
using System.Diagnostics;

// Unity imports
using UnityEditor;
using UnityEngine;

public static class OC_ForceReserializeAssetsMenu
{
    private const string MenuPath = "Tools/Assets/Force Reserialize All";
    private const string ContextMenuPath = "Assets/Force Reserialize";

    [MenuItem(MenuPath)]
    private static void ForceReserializeAll()
    {
        var confirmed = EditorUtility.DisplayDialog(
            "Force Reserialize All",
            "This will reserialize all assets under Assets/. This may take a while. Continue?",
            "Reserialize",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var allPaths = AssetDatabase.GetAllAssetPaths();
        var assetPaths = new List<string>(allPaths.Length);

        foreach (var path in allPaths)
        {
            if (path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                assetPaths.Add(path);
            }
        }

        try
        {
            if (assetPaths.Count == 0)
            {
                UnityEngine.Debug.LogWarning("No Assets paths found to reserialize.");
                return;
            }

            EditorUtility.DisplayProgressBar("Force Reserialize", "Reserializing assets...", 0f);
            AssetDatabase.ForceReserializeAssets(assetPaths);
            AssetDatabase.SaveAssets();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Force reserialize failed: {ex.Message}");
            throw;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            stopwatch.Stop();
        }

        UnityEngine.Debug.Log($"Force reserialize complete. Assets: {assetPaths.Count}. Time: {stopwatch.Elapsed}.");
    }

    [MenuItem(ContextMenuPath)]
    private static void ForceReserializeSelection()
    {
        var selectionPaths = GetSelectedAssetPaths();
        if (selectionPaths.Count == 0)
        {
            UnityEngine.Debug.LogWarning("No valid Assets selection found to reserialize.");
            return;
        }

        var confirmed = EditorUtility.DisplayDialog(
            "Force Reserialize",
            $"This will reserialize {selectionPaths.Count} asset(s) from the selection. Continue?",
            "Reserialize",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            EditorUtility.DisplayProgressBar("Force Reserialize", "Reserializing selected assets...", 0f);
            AssetDatabase.ForceReserializeAssets(selectionPaths);
            AssetDatabase.SaveAssets();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Force reserialize failed: {ex.Message}");
            throw;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            stopwatch.Stop();
        }

        UnityEngine.Debug.Log($"Force reserialize complete. Assets: {selectionPaths.Count}. Time: {stopwatch.Elapsed}.");
    }

    [MenuItem(ContextMenuPath, true)]
    private static bool ForceReserializeSelectionValidate()
    {
        var selection = Selection.assetGUIDs;
        if (selection == null || selection.Length == 0)
        {
            return false;
        }

        foreach (var guid in selection)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> GetSelectedAssetPaths()
    {
        var results = new HashSet<string>(StringComparer.Ordinal);
        var selection = Selection.assetGUIDs;
        if (selection == null || selection.Length == 0)
        {
            return new List<string>();
        }

        foreach (var guid in selection)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                continue;
            }

            if (AssetDatabase.IsValidFolder(path))
            {
                var guids = AssetDatabase.FindAssets(string.Empty, new[] { path });
                foreach (var assetGuid in guids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                    if (assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                    {
                        results.Add(assetPath);
                    }
                }
            }
            else
            {
                results.Add(path);
            }
        }

        return new List<string>(results);
    }
}
