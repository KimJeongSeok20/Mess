using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple, prefab-owned authoring for dungeon navigation.
/// Add green Walkable areas for places monsters may use and red NotWalkable
/// areas for places that must be removed from the runtime NavMesh.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Dungeon/NavMesh Area")]
public sealed class DungeonNavMeshArea : MonoBehaviour
{
    public enum AreaType
    {
        Walkable,
        NotWalkable,
    }

    public enum ShapeType
    {
        Box,
        AttachedColliders,
    }

    [Header("Simple NavMesh Area")]
    [SerializeField, Tooltip("Green areas are walkable. Red areas are removed from the NavMesh.")]
    private AreaType areaType = AreaType.Walkable;

    [SerializeField, Tooltip("Use a resizable box, or reuse the Collider components on this object.")]
    private ShapeType shape = ShapeType.Box;

    [SerializeField, Tooltip("Disable this without deleting the authored area.")]
    private bool includeInRuntimeBake = true;

    [Header("Box")]
    [SerializeField] private Vector3 center = Vector3.zero;
    [SerializeField] private Vector3 size = new Vector3(4f, 0.2f, 4f);

    public AreaType Type => areaType;
    public ShapeType Shape => shape;
    public bool IncludeInRuntimeBake => includeInRuntimeBake;
    public Vector3 Center => center;
    public Vector3 Size => size;
    public bool IsWalkable => areaType == AreaType.Walkable;
    public bool IsNotWalkable => areaType == AreaType.NotWalkable;

    public void ConfigureBox(AreaType type, Vector3 localCenter, Vector3 localSize)
    {
        areaType = type;
        shape = ShapeType.Box;
        SetBoxBounds(localCenter, localSize);
        includeInRuntimeBake = true;
    }

    public void SetBoxBounds(Vector3 localCenter, Vector3 localSize)
    {
        center = localCenter;
        size = PositiveSize(localSize);
    }

    public void ConfigureFromAttachedColliders(AreaType type)
    {
        areaType = type;
        shape = ShapeType.AttachedColliders;
        includeInRuntimeBake = true;
    }

    public int GetEnabledAttachedColliders(List<Collider> results)
    {
        if (results == null)
            return 0;

        results.Clear();
        Collider[] colliders = GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider != null && collider.enabled && !collider.isTrigger)
                results.Add(collider);
        }

        return results.Count;
    }

    private void Reset()
    {
        Collider collider = GetComponent<Collider>();
        shape = collider != null ? ShapeType.AttachedColliders : ShapeType.Box;
        areaType = AreaType.Walkable;
        includeInRuntimeBake = true;
    }

    private void OnValidate()
    {
        size = PositiveSize(size);
    }

    private void OnDrawGizmos()
    {
        if (!includeInRuntimeBake)
            return;

        Color color = IsWalkable
            ? new Color(0.15f, 0.95f, 0.3f, 0.75f)
            : new Color(1f, 0.2f, 0.15f, 0.85f);

        if (shape == ShapeType.Box)
        {
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Color oldColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = color;
            Gizmos.DrawWireCube(center, size);
            Gizmos.matrix = oldMatrix;
            Gizmos.color = oldColor;
            return;
        }

        Collider[] colliders = GetComponents<Collider>();
        Color previous = Gizmos.color;
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = color;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;

            Bounds bounds = collider.bounds;
            Vector3 displaySize = bounds.size;
            displaySize.y = Mathf.Max(displaySize.y, 0.08f);
            Gizmos.DrawWireCube(bounds.center, displaySize);
        }

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previous;
    }

    private static Vector3 PositiveSize(Vector3 value)
    {
        return new Vector3(
            Mathf.Max(0.01f, Mathf.Abs(value.x)),
            Mathf.Max(0.01f, Mathf.Abs(value.y)),
            Mathf.Max(0.01f, Mathf.Abs(value.z)));
    }
}
