using UnityEngine;

/// <summary>
/// Marks a fixed prop whose complete visible footprint must be removed from the
/// runtime NavMesh even when its child colliders have gaps (for example LOD benches).
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Dungeon/NavMesh Footprint Blocker")]
public sealed class DungeonNavMeshFootprintBlocker : MonoBehaviour
{
    [SerializeField, Min(0f), Tooltip("Extra footprint width added on each horizontal side.")]
    private float horizontalPadding = 0.03f;

    [SerializeField, Min(0f), Tooltip("Small overlap below the room floor so touching bake boundaries are not missed.")]
    private float floorOverlap = 0.02f;

    public float HorizontalPadding => horizontalPadding;
    public float FloorOverlap => floorOverlap;

    public bool TryGetColliderBounds(out Bounds bounds)
    {
        bounds = default;
        bool initialized = false;
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy)
                continue;

            if (!initialized)
            {
                bounds = collider.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return initialized && bounds.size.x > 0.01f && bounds.size.z > 0.01f;
    }

    private void OnDrawGizmosSelected()
    {
        if (!TryGetColliderBounds(out Bounds bounds))
            return;

        float padding = Mathf.Max(0f, horizontalPadding);
        bounds.Expand(new Vector3(padding * 2f, 0f, padding * 2f));
        Color oldColor = Gizmos.color;
        Gizmos.color = new Color(1f, 0.2f, 0.15f, 0.9f);
        Gizmos.DrawWireCube(bounds.center, bounds.size);
        Gizmos.color = oldColor;
    }
}
