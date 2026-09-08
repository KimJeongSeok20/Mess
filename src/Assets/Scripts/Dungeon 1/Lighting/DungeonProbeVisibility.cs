using System.Collections.Generic;
using UnityEngine;

/// <summary>Visibility between a receiver and a baked probe, against explicit geometry only.</summary>
public static class DungeonProbeVisibility
{
    public static bool IsVisible(Vector3 from, Vector3 to, IReadOnlyList<Collider> blockers, out int rayCount,
        Transform ignoredGeometryRoot = null)
    {
        rayCount = 0;
        if (blockers == null || blockers.Count == 0)
            return true;

        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (float.IsNaN(distance) || float.IsInfinity(distance))
            return false;
        Vector3 direction = distance > 0.000001f ? delta / distance : Vector3.zero;
        var ray = new Ray(from, direction);
        var reverse = new Ray(to, -direction);
        for (int i = 0; i < blockers.Count; i++)
        {
            Collider blocker = blockers[i];
            if (blocker == null || !blocker.enabled || blocker.isTrigger || !blocker.gameObject.activeInHierarchy)
                continue;
            if (ignoredGeometryRoot != null && blocker.transform.IsChildOf(ignoredGeometryRoot))
                continue;
            Bounds bounds = blocker.bounds;
            if (!bounds.Contains(from) &&
                (!bounds.IntersectRay(ray, out float entryDistance) || entryDistance > distance))
                continue;

            // Full segments preserve thin walls near either endpoint. ClosestPoint is a
            // solid-interior test only for primitives/convex meshes, not non-convex meshes.
            bool nonConvexMesh = blocker is MeshCollider mesh && !mesh.convex;
            if (!nonConvexMesh && (blocker.ClosestPoint(from) - from).sqrMagnitude <= 0.00000001f)
                return false;
            if (distance <= 0.000001f)
                continue;
            rayCount++;
            if (blocker.Raycast(ray, out _, distance))
                return false;
            // A reverse ray handles single-sided wall meshes without changing global physics.
            if (nonConvexMesh)
            {
                rayCount++;
                if (blocker.Raycast(reverse, out _, distance))
                    return false;
            }
        }
        return true;
    }
}
