using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;
using DungeonDoor = DunGen.Door;

[DisallowMultipleComponent]
public sealed class MonsterDoorLinkBinding : MonoBehaviour
{
    [SerializeField] private NavMeshLink navMeshLink;
    [SerializeField] private Door regularDoor;
    [SerializeField] private SlidingDoor slidingDoor;
    [SerializeField] private DoorInteractable doorInteractable;
    [SerializeField] private DoorInteraction doorInteraction;
    [SerializeField] private DoorAnimatorOpener doorAnimatorOpener;
    [SerializeField] private DungeonDoor dungeonDoor;
    [SerializeField] private Collider[] passageColliders;

    public NavMeshLink Link => navMeshLink;
    public Door RegularDoor => regularDoor;
    public DungeonDoor DungeonDoor => dungeonDoor;
    public IReadOnlyList<Collider> PassageColliders => passageColliders;
    public bool HasPassageCollider => ContainsAnyCollider(passageColliders);

    public bool CanOpen =>
        regularDoor != null ||
        slidingDoor != null ||
        doorInteractable != null ||
        doorInteraction != null ||
        doorAnimatorOpener != null ||
        dungeonDoor != null;

    public bool IsOpen
    {
        get
        {
            if (regularDoor != null)
                return regularDoor.IsOpen;

            if (slidingDoor != null)
                return slidingDoor.IsOpen;

            if (doorInteractable != null)
                return doorInteractable.IsOpen;

            if (doorInteraction != null)
                return doorInteraction.IsOpen;

            if (doorAnimatorOpener != null)
                return doorAnimatorOpener.IsOpen;

            if (dungeonDoor != null)
                return dungeonDoor.IsOpen;

            return false;
        }
    }

    public bool IsOpenPassageCollider(Collider collider)
    {
        if (collider == null || !IsOpen)
            return false;

        if (!HasPassageCollider)
            RefreshPassageCollidersFromReferences();

        if (ContainsCollider(passageColliders, collider))
            return true;

        if (ContainsOwnCollider(regularDoor, collider))
            return true;

        if (ContainsOwnCollider(slidingDoor, collider))
            return true;

        if (doorInteraction != null && doorInteraction.SolidCollider == collider)
            return true;

        if (ContainsOwnCollider(doorInteractable, collider))
            return true;

        if (doorAnimatorOpener != null)
        {
            if (ContainsOwnCollider(doorAnimatorOpener, collider))
                return true;

            if (doorAnimatorOpener.animator != null
                && ContainsCollider(doorAnimatorOpener.animator.GetComponentsInChildren<Collider>(true), collider))
                return true;
        }

        return ContainsOwnCollider(dungeonDoor, collider);
    }

    /// <summary>
    /// Returns true only when the remaining path crosses the physical door plane,
    /// inside the door width, and continues to the opposite side. Merely being near
    /// or facing a door is not enough to open it.
    /// </summary>
    public bool IsPassageOnPath(
        Vector3[] pathCorners,
        int cornerCount,
        float horizontalPadding,
        float maxPathDistance,
        float minimumBeyondDistance)
    {
        if (pathCorners == null || cornerCount < 2 || maxPathDistance <= 0f)
            return false;

        if (!HasPassageCollider)
            RefreshPassageCollidersFromReferences();

        int count = Mathf.Min(cornerCount, pathCorners.Length);
        for (int i = 0; i < passageColliders.Length; i++)
        {
            Collider collider = passageColliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;

            if (!TryGetHorizontalPassagePlane(
                    collider,
                    out Vector3 center,
                    out Vector3 normal,
                    out Vector3 tangent,
                    out float halfWidth))
            {
                continue;
            }

            if (PathCrossesPassagePlane(
                    pathCorners,
                    count,
                    center,
                    normal,
                    tangent,
                    halfWidth + Mathf.Max(0f, horizontalPadding),
                    maxPathDistance,
                    Mathf.Max(0.01f, minimumBeyondDistance)))
            {
                return true;
            }
        }

        return false;
    }

    public void ConfigureContinuousDoor(
        Door targetDoor,
        DungeonDoor targetDungeonDoor,
        Collider[] targetPassageColliders)
    {
        regularDoor = targetDoor;
        dungeonDoor = targetDungeonDoor;
        passageColliders = targetPassageColliders ?? System.Array.Empty<Collider>();
        ResolveReferences(transform.position, 2f, ~0);
    }

    public void RefreshPassageCollidersFromReferences()
    {
        List<Collider> colliders = new List<Collider>();
        AddColliders(colliders, passageColliders);
        AddOwnColliders(colliders, regularDoor);
        AddOwnColliders(colliders, slidingDoor);
        AddAnimatorColliders(colliders, slidingDoor != null ? slidingDoor.GetComponent<Animator>() : null);

        if (doorInteraction != null)
        {
            AddCollider(colliders, doorInteraction.SolidCollider);
            if (doorInteraction.SolidCollider == null)
                AddOwnColliders(colliders, doorInteraction);
        }

        AddOwnColliders(colliders, doorInteractable);
        AddAnimatorColliders(colliders, doorInteractable != null ? doorInteractable.GetComponentInChildren<Animator>(true) : null);

        AddOwnColliders(colliders, doorAnimatorOpener);
        AddAnimatorColliders(colliders, doorAnimatorOpener != null ? doorAnimatorOpener.animator : null);
        AddOwnColliders(colliders, dungeonDoor);

        passageColliders = colliders.ToArray();
    }

    public static bool TryResolve(
        NavMeshLink link,
        Vector3 midpoint,
        float searchRadius,
        LayerMask layerMask,
        out MonsterDoorLinkBinding binding)
    {
        binding = null;

        if (link != null)
        {
            binding = link.GetComponent<MonsterDoorLinkBinding>()
                ?? link.GetComponentInParent<MonsterDoorLinkBinding>()
                ?? link.GetComponentInChildren<MonsterDoorLinkBinding>();

            if (binding != null)
            {
                binding.navMeshLink = link;
                binding.ResolveReferences(midpoint, searchRadius, layerMask);
                if (binding.CanOpen)
                    return true;
            }
        }

        Collider[] hits = Physics.OverlapSphere(midpoint, Mathf.Max(0.1f, searchRadius), layerMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
                continue;

            binding = hit.GetComponent<MonsterDoorLinkBinding>()
                ?? hit.GetComponentInParent<MonsterDoorLinkBinding>()
                ?? hit.GetComponentInChildren<MonsterDoorLinkBinding>();
            if (binding == null)
                continue;

            binding.navMeshLink = link;
            binding.ResolveReferences(midpoint, searchRadius, layerMask);
            if (binding.CanOpen)
                return true;
        }

        binding = null;
        return false;
    }

    public static bool TryResolve(Collider collider, out MonsterDoorLinkBinding binding)
    {
        binding = null;
        if (collider == null)
            return false;

        binding = collider.GetComponent<MonsterDoorLinkBinding>()
            ?? collider.GetComponentInParent<MonsterDoorLinkBinding>()
            ?? collider.GetComponentInChildren<MonsterDoorLinkBinding>();
        if (binding == null)
            return false;

        binding.ResolveReferences(collider.bounds.center, 1f, ~0);
        return binding.CanOpen;
    }

    public void Open()
    {
        if (regularDoor != null && !regularDoor.IsOpen)
            regularDoor.RequestOpen();

        if (slidingDoor != null && !slidingDoor.IsOpen)
            slidingDoor.InteractWithSlidingDoor();

        if (doorAnimatorOpener != null && !doorAnimatorOpener.IsOpen)
            doorAnimatorOpener.OpenDoor();

        if (doorInteractable != null && !doorInteractable.IsOpen)
            doorInteractable.SV_RequestOpen();

        if (doorInteraction != null && !doorInteraction.IsOpen)
            doorInteraction.SV_RequestOpen();

        if (dungeonDoor != null && !dungeonDoor.IsOpen)
            dungeonDoor.IsOpen = true;
    }

    public IEnumerator WaitUntilOpen(float timeout)
    {
        if (IsOpen)
            yield break;

        float elapsed = 0f;
        while (!IsOpen)
        {
            if (timeout > 0f && elapsed >= timeout)
                yield break;

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private void Reset()
    {
        navMeshLink = GetComponent<NavMeshLink>();
        ResolveReferences(transform.position, 2f, ~0);
    }

    private void ResolveReferences(Vector3 midpoint, float searchRadius, LayerMask layerMask)
    {
        ResolveFromTransform(transform);

        if (!CanOpen && navMeshLink != null)
        {
            ResolveFromTransform(navMeshLink.transform);

            if (!CanOpen && navMeshLink.transform.parent != null)
                ResolveFromTransform(navMeshLink.transform.parent);
        }

        if (!CanOpen)
        {
            Collider[] hits = Physics.OverlapSphere(midpoint, Mathf.Max(0.1f, searchRadius), layerMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null)
                    continue;

                ResolveFromTransform(hit.transform);
                if (CanOpen)
                    break;
            }
        }

        RefreshPassageCollidersFromReferences();
    }

    private void ResolveFromTransform(Transform source)
    {
        if (source == null)
            return;

        if (regularDoor == null)
            regularDoor = source.GetComponentInParent<Door>() ?? source.GetComponentInChildren<Door>();

        if (slidingDoor == null)
            slidingDoor = source.GetComponentInParent<SlidingDoor>() ?? source.GetComponentInChildren<SlidingDoor>();

        if (doorInteractable == null)
            doorInteractable = source.GetComponentInParent<DoorInteractable>() ?? source.GetComponentInChildren<DoorInteractable>();

        if (doorInteraction == null)
            doorInteraction = source.GetComponentInParent<DoorInteraction>() ?? source.GetComponentInChildren<DoorInteraction>();

        if (doorAnimatorOpener == null)
            doorAnimatorOpener = source.GetComponentInParent<DoorAnimatorOpener>() ?? source.GetComponentInChildren<DoorAnimatorOpener>();

        if (dungeonDoor == null)
            dungeonDoor = source.GetComponentInParent<DungeonDoor>() ?? source.GetComponentInChildren<DungeonDoor>();
    }

    private static bool ContainsOwnCollider(Component component, Collider target)
    {
        if (component == null || target == null)
            return false;

        return ContainsCollider(component.GetComponents<Collider>(), target);
    }

    private static bool TryGetHorizontalPassagePlane(
        Collider collider,
        out Vector3 center,
        out Vector3 normal,
        out Vector3 tangent,
        out float halfWidth)
    {
        center = collider.bounds.center;
        normal = Vector3.zero;
        tangent = Vector3.zero;
        halfWidth = 0f;

        if (collider is BoxCollider box)
        {
            Vector3 scale = Abs(box.transform.lossyScale);
            float worldX = box.size.x * scale.x;
            float worldZ = box.size.z * scale.z;
            center = box.transform.TransformPoint(box.center);
            if (worldX <= worldZ)
            {
                normal = Horizontal(box.transform.right);
                tangent = Horizontal(box.transform.forward);
                halfWidth = worldZ * 0.5f;
            }
            else
            {
                normal = Horizontal(box.transform.forward);
                tangent = Horizontal(box.transform.right);
                halfWidth = worldX * 0.5f;
            }
        }
        else
        {
            Bounds bounds = collider.bounds;
            if (bounds.size.x <= bounds.size.z)
            {
                normal = Vector3.right;
                tangent = Vector3.forward;
                halfWidth = bounds.extents.z;
            }
            else
            {
                normal = Vector3.forward;
                tangent = Vector3.right;
                halfWidth = bounds.extents.x;
            }
        }

        if (normal.sqrMagnitude < 0.0001f || tangent.sqrMagnitude < 0.0001f || halfWidth <= 0.001f)
            return false;

        normal.Normalize();
        tangent.Normalize();
        return true;
    }

    private static bool PathCrossesPassagePlane(
        Vector3[] corners,
        int cornerCount,
        Vector3 center,
        Vector3 normal,
        Vector3 tangent,
        float allowedHalfWidth,
        float maxPathDistance,
        float minimumBeyondDistance)
    {
        Vector3 previous = corners[0];
        float previousSigned = SignedHorizontalDistance(previous, center, normal);
        float initialSide = Mathf.Abs(previousSigned) > 0.01f ? Mathf.Sign(previousSigned) : 0f;
        float travelled = 0f;
        bool crossedInsidePassage = false;

        for (int i = 1; i < cornerCount && travelled < maxPathDistance; i++)
        {
            Vector3 current = corners[i];
            Vector3 horizontalDelta = Horizontal(current - previous);
            float segmentLength = horizontalDelta.magnitude;
            if (segmentLength <= 0.0001f)
            {
                previous = current;
                previousSigned = SignedHorizontalDistance(previous, center, normal);
                continue;
            }

            float remaining = maxPathDistance - travelled;
            if (segmentLength > remaining)
            {
                current = Vector3.Lerp(previous, current, remaining / segmentLength);
                segmentLength = remaining;
            }

            float currentSigned = SignedHorizontalDistance(current, center, normal);
            if (initialSide == 0f)
            {
                float motionAcrossPlane = Vector3.Dot(Horizontal(current - previous), normal);
                if (Mathf.Abs(motionAcrossPlane) > 0.001f)
                    initialSide = -Mathf.Sign(motionAcrossPlane);
                else if (Mathf.Abs(currentSigned) > 0.01f)
                    initialSide = Mathf.Sign(currentSigned);
            }

            if (SegmentCrossesPlane(previousSigned, currentSigned, out float crossingT))
            {
                Vector3 crossing = Vector3.Lerp(previous, current, crossingT);
                float tangentOffset = Mathf.Abs(Vector3.Dot(Horizontal(crossing - center), tangent));
                if (tangentOffset <= allowedHalfWidth)
                    crossedInsidePassage = true;
            }

            if (crossedInsidePassage && initialSide != 0f &&
                currentSigned * initialSide <= -minimumBeyondDistance)
            {
                return true;
            }

            travelled += segmentLength;
            previous = current;
            previousSigned = currentSigned;
        }

        return false;
    }

    private static bool SegmentCrossesPlane(float start, float end, out float t)
    {
        t = 0f;
        if ((start < 0f && end < 0f) || (start > 0f && end > 0f))
            return false;

        float denominator = start - end;
        if (Mathf.Abs(denominator) <= 0.0001f)
            return Mathf.Abs(start) <= 0.0001f;

        t = Mathf.Clamp01(start / denominator);
        return true;
    }

    private static float SignedHorizontalDistance(Vector3 point, Vector3 center, Vector3 normal)
    {
        return Vector3.Dot(Horizontal(point - center), normal);
    }

    private static Vector3 Horizontal(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private static void AddOwnColliders(List<Collider> colliders, Component component)
    {
        if (component == null)
            return;

        AddColliders(colliders, component.GetComponents<Collider>());
    }

    private static void AddAnimatorColliders(List<Collider> colliders, Animator animator)
    {
        if (animator == null)
            return;

        AddColliders(colliders, animator.GetComponentsInChildren<Collider>(true));
    }

    private static void AddColliders(List<Collider> colliders, Collider[] source)
    {
        if (colliders == null || source == null)
            return;

        for (int i = 0; i < source.Length; i++)
            AddCollider(colliders, source[i]);
    }

    private static void AddCollider(List<Collider> colliders, Collider collider)
    {
        if (colliders == null || collider == null || colliders.Contains(collider))
            return;

        colliders.Add(collider);
    }

    private static bool ContainsAnyCollider(Collider[] colliders)
    {
        if (colliders == null)
            return false;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                return true;
        }

        return false;
    }

    private static bool ContainsCollider(Collider[] colliders, Collider target)
    {
        if (colliders == null || target == null)
            return false;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == target)
                return true;
        }

        return false;
    }
}
