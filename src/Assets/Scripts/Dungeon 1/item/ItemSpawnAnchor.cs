using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ItemSpawnAnchor : MonoBehaviour
{
    [Header("Anchor")]
    [SerializeField] private Transform anchorPoint;
    [SerializeField] private float yOffset = 0.05f;

    [Header("Selection")]
    [Min(0f)]
    [SerializeField] private float weight = 1f;
    [SerializeField] private List<string> tags = new List<string>();

    public Transform AnchorPoint => anchorPoint != null ? anchorPoint : transform;
    public Vector3 Position => AnchorPoint.position + Vector3.up * yOffset;
    public float Weight => Mathf.Max(0f, weight);
    public IReadOnlyList<string> Tags => tags;

    public bool HasAnyTag(IReadOnlyList<string> filter)
    {
        if (filter == null || filter.Count == 0) return true;
        if (tags == null || tags.Count == 0) return false;

        for (int i = 0; i < filter.Count; i++)
        {
            if (tags.Contains(filter[i]))
                return true;
        }

        return false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.95f);
        Gizmos.DrawWireSphere(Position, 0.12f);
    }
#endif
}
