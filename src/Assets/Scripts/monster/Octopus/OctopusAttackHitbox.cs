using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(Rigidbody))]
public sealed class OctopusAttackHitbox : MonoBehaviour
{
    [SerializeField] private OctopusSwarmMember owner;
    [SerializeField] private BoxCollider hitCollider;
    [SerializeField] private LayerMask damageMask = ~0;
    [SerializeField] private int overlapBufferSize = 16;

    private readonly HashSet<int> _hitTargets = new();
    private Collider[] _overlapBuffer;
    private bool _active;

    private void Awake()
    {
        owner ??= GetComponentInParent<OctopusSwarmMember>();
        hitCollider ??= GetComponent<BoxCollider>();

        if (hitCollider != null)
            hitCollider.isTrigger = true;

        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        EnsureOverlapBuffer();
        SetActive(false);
    }

    private void OnValidate()
    {
        overlapBufferSize = Mathf.Max(1, overlapBufferSize);
    }

    public void SetOwner(OctopusSwarmMember member)
    {
        owner = member;
    }

    public void ConfigureRadius(float radius)
    {
        if (hitCollider == null)
            hitCollider = GetComponent<BoxCollider>();

        if (hitCollider == null)
            return;

        float clampedRadius = Mathf.Max(0.1f, radius);
        Vector3 scale = transform.lossyScale;
        float scaleX = Mathf.Max(0.0001f, Mathf.Abs(scale.x));
        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(scale.y));
        float scaleZ = Mathf.Max(0.0001f, Mathf.Abs(scale.z));

        // The imported octopus is scaled to 0.25 in the swarm prefab. Keep the
        // configured attack radius in world units so the hitbox does not shrink
        // to one quarter of the intended melee reach.
        hitCollider.size = new Vector3(
            clampedRadius * 1.4f / scaleX,
            Mathf.Max(1.5f, clampedRadius * 1.2f) / scaleY,
            clampedRadius * 1.75f / scaleZ);
        hitCollider.center = new Vector3(0f, 0f, Mathf.Max(0.2f, clampedRadius * 0.45f) / scaleZ);
    }

    public void Activate()
    {
        if (_active)
            return;

        _hitTargets.Clear();
        SetActive(true);
        ApplyImmediateOverlaps();
    }

    public void Deactivate()
    {
        SetActive(false);
        _hitTargets.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        TryDamage(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryDamage(other);
    }

    private void TryDamage(Collider other)
    {
        if (!_active || owner == null || owner.IsDead || other == null)
            return;

        if (!owner.TryApplyHitboxDamage(other, ResolveHitPoint(other), out int targetId))
            return;

        if (targetId == 0)
            return;

        _hitTargets.Add(targetId);
    }

    private void ApplyImmediateOverlaps()
    {
        if (hitCollider == null)
            return;

        Vector3 center = transform.TransformPoint(hitCollider.center);
        Vector3 halfExtents = Vector3.Scale(hitCollider.size * 0.5f, transform.lossyScale);
        EnsureOverlapBuffer();

        int mask = damageMask.value == 0 ? ~0 : damageMask.value;
        int hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, _overlapBuffer, transform.rotation, mask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hitCount; i++)
            TryDamage(_overlapBuffer[i]);
    }

    private void SetActive(bool isActive)
    {
        _active = isActive;
        if (hitCollider != null)
            hitCollider.enabled = isActive;
    }

    private void EnsureOverlapBuffer()
    {
        int size = Mathf.Max(1, overlapBufferSize);
        if (_overlapBuffer == null || _overlapBuffer.Length != size)
            _overlapBuffer = new Collider[size];
    }

    private Vector3 ResolveHitPoint(Collider other)
    {
        if (other == null)
            return transform.position;

        Vector3 referencePoint = hitCollider != null
            ? transform.TransformPoint(hitCollider.center)
            : transform.position;
        Vector3 point = other.ClosestPoint(referencePoint);
        if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z))
            return other.transform.position;

        return point;
    }
}
