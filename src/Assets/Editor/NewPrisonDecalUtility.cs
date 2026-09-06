using System;
using UnityEditor;
using UnityEngine;

public static class NewPrisonDecalUtility
{
    private const float FloorDecalMaxThickness = 0.08f;
    private const float FloorDecalMinPlaneSize = 0.1f;

    public static bool IsDecalRenderer(Renderer renderer)
    {
        if (renderer == null)
            return false;

        if (ContainsDecalToken(renderer.name) || ContainsDecalToken(renderer.gameObject.name))
            return true;

        foreach (Material material in renderer.sharedMaterials)
        {
            if (IsDecalMaterial(material))
                return true;
        }

        return false;
    }

    public static bool IsFloorDecalRenderer(Renderer renderer)
    {
        if (!IsDecalRenderer(renderer))
            return false;

        if (IsOverlayOnlyFloorDecalName(renderer.name) || IsOverlayOnlyFloorDecalName(renderer.gameObject.name))
            return false;

        Vector3 size = renderer.bounds.size;
        return size.y <= FloorDecalMaxThickness &&
            size.x >= FloorDecalMinPlaneSize &&
            size.z >= FloorDecalMinPlaneSize;
    }

    public static bool IsWallDecalRenderer(Renderer renderer)
    {
        return IsDecalRenderer(renderer) && !IsFloorDecalRenderer(renderer);
    }

    public static bool IsDecalTransformRoot(Transform transform)
    {
        if (transform == null)
            return false;

        if (ContainsDecalToken(transform.name))
            return true;

        var renderers = transform.GetComponents<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (IsDecalRenderer(renderers[i]))
                return true;
        }

        return false;
    }

    public static bool IsWallDecalTransformRoot(Transform transform)
    {
        if (transform == null)
            return false;

        var renderers = transform.GetComponents<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (IsWallDecalRenderer(renderers[i]))
                return true;
        }

        return false;
    }

    public static bool IsDecalRelativePath(string relativePath)
    {
        return ContainsDecalToken(relativePath);
    }

    public static bool IsFloorDecalRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return false;

        if (IsOverlayOnlyFloorDecalName(relativePath))
            return false;

        return relativePath.IndexOf("Decal_stripe", StringComparison.OrdinalIgnoreCase) >= 0 ||
            relativePath.IndexOf("Decal_Stripe", StringComparison.OrdinalIgnoreCase) >= 0 ||
            relativePath.IndexOf("Decal_Corridor_B", StringComparison.OrdinalIgnoreCase) >= 0 ||
            relativePath.IndexOf("FloorDecal", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsWallDecalRelativePath(string relativePath)
    {
        return IsDecalRelativePath(relativePath) && !IsFloorDecalRelativePath(relativePath);
    }

    public static bool IsDecalMaterial(Material material)
    {
        if (material == null)
            return false;

        string materialPath = AssetDatabase.GetAssetPath(material);
        string shaderName = material.shader != null ? material.shader.name : string.Empty;

        return ContainsDecalToken(material.name) ||
            ContainsDecalToken(materialPath) ||
            materialPath.IndexOf("/DECAL_MATERIAL/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            shaderName.IndexOf("NewPrison/Decals", StringComparison.OrdinalIgnoreCase) >= 0 ||
            shaderName.IndexOf("Decals", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsDecalToken(string value)
    {
        return !string.IsNullOrEmpty(value) &&
            value.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsOverlayOnlyFloorDecalName(string value)
    {
        return !string.IsNullOrEmpty(value) &&
            value.IndexOf("Decal_Corridor_C", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
