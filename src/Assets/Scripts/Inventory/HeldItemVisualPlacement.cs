using System;
using UnityEngine;

public static class HeldItemVisualPlacement
{
    public const float HandFitMaxExtent = 0.22f;
    public const float AutoFitThreshold = 0.35f;
    public const float AutoFitMinExtent = 0.08f;

    public static void Apply(Transform pivot, Transform visual, HeldItemVisualCatalog.Entry entry)
    {
        if (pivot == null || visual == null || entry == null)
            return;

        pivot.localPosition = entry.localPosition;
        pivot.localRotation = Quaternion.Euler(entry.localEulerAngles);
        pivot.localScale = SanitizeScale(entry.localScale);

        if (visual.parent != pivot)
            visual.SetParent(pivot, false);

        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
        CenterLocalBounds(visual);
    }

    public static void Capture(Transform pivot, HeldItemVisualCatalog.Entry entry)
    {
        if (pivot == null || entry == null)
            return;

        entry.localPosition = pivot.localPosition;
        entry.localEulerAngles = NormalizeEuler(pivot.localEulerAngles);
        entry.localScale = SanitizeScale(pivot.localScale);
    }

    public static Vector3 FitPivotScaleToHand(Transform visual, float targetMaxExtent = HandFitMaxExtent)
    {
        if (visual == null || !TryGetWorldRendererBounds(visual, out Bounds worldBounds))
            return Vector3.one;

        float maxExtent = Mathf.Max(worldBounds.size.x, worldBounds.size.y, worldBounds.size.z);
        if (maxExtent <= 0.0001f)
            return Vector3.one;

        float currentPivot = 1f;
        if (visual.parent != null)
            currentPivot = SanitizeScale(visual.parent.localScale).x;

        return Vector3.one * (currentPivot * (targetMaxExtent / maxExtent));
    }

    public static bool ShouldAutoFit(Bounds localBounds)
    {
        float maxExtent = Mathf.Max(localBounds.size.x, localBounds.size.y, localBounds.size.z);
        return maxExtent > AutoFitThreshold || maxExtent < AutoFitMinExtent;
    }

    public static void CenterLocalBounds(Transform visual)
    {
        if (visual == null || !TryGetWorldRendererBounds(visual, out Bounds worldBounds))
            return;

        Vector3 target = visual.parent != null ? visual.parent.position : Vector3.zero;
        visual.position += target - worldBounds.center;
    }

    public static bool TryGetLocalRendererBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        if (root == null)
            return false;

        foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
                continue;

            Encapsulate(ref bounds, ref hasBounds, root, meshFilter.transform, meshFilter.sharedMesh.bounds);
        }

        foreach (SkinnedMeshRenderer skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skinned == null)
                continue;

            Encapsulate(ref bounds, ref hasBounds, root, skinned.transform, skinned.localBounds);
        }

        return hasBounds;
    }

    public static bool TryGetWorldRendererBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        if (root == null)
            return false;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    public static Vector3 SanitizeScale(Vector3 scale)
    {
        if (Mathf.Abs(scale.x) < 0.0001f && Mathf.Abs(scale.y) < 0.0001f && Mathf.Abs(scale.z) < 0.0001f)
            return Vector3.one;

        return new Vector3(
            Mathf.Abs(scale.x) < 0.0001f ? 1f : scale.x,
            Mathf.Abs(scale.y) < 0.0001f ? 1f : scale.y,
            Mathf.Abs(scale.z) < 0.0001f ? 1f : scale.z);
    }

    public static void ApplyHeldGrasp(Transform pivot, Transform visual, string itemName,
        float matchWorldMaxExtent = 0f)
    {
        if (pivot == null || visual == null)
            return;

        GuessGrasp(itemName, out Vector3 localPosition, out Vector3 localEuler, out _);
        pivot.localScale = matchWorldMaxExtent > 0.001f
            ? FitPivotScaleToHand(visual, matchWorldMaxExtent)
            : Vector3.one;
        CenterLocalBounds(visual);
        pivot.localRotation = Quaternion.Euler(localEuler);
        pivot.localPosition = localPosition;
    }

    public static void GuessGrasp(string itemName, out Vector3 localPosition, out Vector3 localEuler, out float targetExtent)
    {
        localPosition = new Vector3(0.01f, -0.02f, -0.04f);
        localEuler = Vector3.zero;
        targetExtent = 0.16f;
        if (string.IsNullOrWhiteSpace(itemName))
            return;

        if (itemName.StartsWith("GiftBox", StringComparison.Ordinal))
        {
            localPosition = new Vector3(0.015f, -0.025f, -0.035f);
            localEuler = new Vector3(18f, 22f, -12f);
            targetExtent = 0.13f;
            return;
        }

        if (itemName.IndexOf("Guitar", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            localPosition = new Vector3(0.02f, -0.01f, -0.02f);
            localEuler = new Vector3(10f, 0f, 80f);
            targetExtent = 0.34f;
            return;
        }

        if (itemName.IndexOf("Ammo", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            localPosition = new Vector3(0.01f, -0.015f, -0.03f);
            localEuler = new Vector3(0f, 0f, 90f);
            targetExtent = 0.12f;
            return;
        }

        if (itemName.IndexOf("backpack", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            localPosition = new Vector3(0.02f, -0.02f, -0.03f);
            localEuler = new Vector3(0f, 90f, 0f);
            targetExtent = 0.18f;
            return;
        }

        if (itemName.IndexOf("Corpse", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            localPosition = new Vector3(0.02f, -0.02f, -0.03f);
            localEuler = new Vector3(0f, 0f, 90f);
            targetExtent = 0.22f;
            return;
        }

        switch (itemName)
        {
            case "Banana":
                localPosition = new Vector3(0.01f, -0.02f, -0.03f);
                localEuler = new Vector3(15f, 0f, 75f);
                targetExtent = 0.2f;
                break;
            case "Bleach":
            case "Jerrycan":
                localPosition = new Vector3(0.01f, -0.02f, -0.03f);
                localEuler = new Vector3(8f, 0f, 85f);
                targetExtent = 0.2f;
                break;
            case "Fish":
                localPosition = new Vector3(0.01f, -0.02f, -0.03f);
                localEuler = new Vector3(0f, 0f, 70f);
                targetExtent = 0.2f;
                break;
            case "Trophy":
                localPosition = new Vector3(0.01f, -0.03f, -0.03f);
                localEuler = new Vector3(0f, 0f, 0f);
                targetExtent = 0.18f;
                break;
            case "Helmet":
                localPosition = new Vector3(0.02f, -0.02f, -0.03f);
                localEuler = new Vector3(-20f, 15f, 0f);
                targetExtent = 0.16f;
                break;
            case "Painting":
                localPosition = new Vector3(0.01f, -0.02f, -0.03f);
                localEuler = new Vector3(0f, 0f, 90f);
                targetExtent = 0.2f;
                break;
            case "Camera":
                localPosition = new Vector3(0.01f, -0.02f, -0.03f);
                localEuler = new Vector3(0f, 90f, 0f);
                targetExtent = 0.14f;
                break;
            case "Octopus":
                localPosition = new Vector3(0.02f, -0.02f, -0.03f);
                localEuler = new Vector3(0f, 0f, 25f);
                targetExtent = 0.18f;
                break;
            case "Test_Cube":
                localPosition = new Vector3(0.01f, -0.02f, -0.03f);
                localEuler = Vector3.zero;
                targetExtent = 0.12f;
                break;
        }
    }

    public static Vector3 NormalizeEuler(Vector3 euler)
    {
        return new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));
    }

    private static void Encapsulate(ref Bounds result, ref bool hasBounds, Transform root,
        Transform sourceTransform, Bounds sourceBounds)
    {
        Matrix4x4 toRoot = root.worldToLocalMatrix * sourceTransform.localToWorldMatrix;
        Vector3 min = sourceBounds.min;
        Vector3 max = sourceBounds.max;

        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
        {
            Vector3 corner = new(
                x == 0 ? min.x : max.x,
                y == 0 ? min.y : max.y,
                z == 0 ? min.z : max.z);
            Vector3 point = toRoot.MultiplyPoint3x4(corner);

            if (!hasBounds)
            {
                result = new Bounds(point, Vector3.zero);
                hasBounds = true;
            }
            else
            {
                result.Encapsulate(point);
            }
        }
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f)
            angle -= 360f;
        else if (angle < -180f)
            angle += 360f;
        return angle;
    }
}
