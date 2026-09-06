using System;
using DunGen;
using UnityEngine;
using UnityEngine.AI;

public class ItemSpawner : MonoBehaviour
{
    [Header("Point Sampling")]
    [SerializeField] private LayerMask floorMask;
    [SerializeField] private float rayStartYOffset = 0f;
    [SerializeField] private float maxDistance = 200f;
    [SerializeField] private float minNormalY = 0.85f;

    [Header("NavMesh")]
    [SerializeField] private bool useNavMesh = true;
    [SerializeField] private float navMeshRadius = 0.5f;
    [SerializeField] private int navMeshAgentTypeId = 0;
    [SerializeField] private int navMeshAreaMask = NavMesh.AllAreas;

    [Header("Spawn Offset")]
    [SerializeField] private float spawnYOffset = 0.05f;

    [Header("Hit Choose")]
    [Tooltip("true면 '가장 가까운(위쪽) 유효 히트'를 사용. false면 유효 히트 중 랜덤(저장소 샘플링).")]
    [SerializeField] private bool pickNearestValid = true;

    [Header("RaycastNonAlloc")]
    [SerializeField, Min(1)] private int maxRaycastHits = 32;

    private RaycastHit[] _hits;

    private void Awake()
    {
        EnsureBuffer();
    }

    private void OnValidate()
    {
        if (maxRaycastHits < 1) maxRaycastHits = 1;
        EnsureBuffer();
    }

    private void EnsureBuffer()
    {
        if (_hits == null || _hits.Length != maxRaycastHits)
            _hits = new RaycastHit[maxRaycastHits];
    }

    public bool TryFindPointInTile(Tile tile, System.Random rng, out Vector3 point)
    {
        point = default;
        if (tile == null) return false;

        Bounds b = tile.Bounds;

        float tX = (float)rng.NextDouble();
        float tZ = (float)rng.NextDouble();

        float x = Mathf.Lerp(b.min.x, b.max.x, tX);
        float z = Mathf.Lerp(b.min.z, b.max.z, tZ);
        float y = b.max.y + rayStartYOffset;

        Vector3 origin = new Vector3(x, y, z);

        EnsureBuffer();

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            _hits,
            maxDistance,
            floorMask,
            QueryTriggerInteraction.Ignore
        );

        if (hitCount <= 0) return false;

        int valid = 0;
        RaycastHit chosen = default;
        float bestDist = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            var h = _hits[i];
            if (h.collider == null) continue;
            if (h.normal.y < minNormalY) continue;

            var hitTile = h.collider.GetComponentInParent<Tile>();
            if (hitTile != tile) continue;

            if (pickNearestValid)
            {
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    chosen = h;
                }
                valid = 1; // 하나라도 찾았다는 뜻
            }
            else
            {
                valid++;
                if (rng.Next(valid) == 0)
                    chosen = h; // reservoir sampling
            }
        }

        if (valid == 0) return false;

        Vector3 raw = chosen.point + Vector3.up * spawnYOffset;

        if (!useNavMesh)
        {
            point = raw;
            return true;
        }

        if (!TrySampleNavMesh(raw, navMeshRadius, out var navHit))
            return false;

        point = navHit.position + Vector3.up * spawnYOffset;
        return true;
    }

    public bool TrySampleNavMesh(Vector3 sourcePosition, float radius, out NavMeshHit hit)
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = navMeshAgentTypeId,
            areaMask = navMeshAreaMask
        };

        return NavMesh.SamplePosition(sourcePosition, out hit, radius, filter);
    }

    public bool TryEnsurePointAboveFloor(Tile tile, Vector3 candidatePoint, out Vector3 adjustedPoint)
    {
        adjustedPoint = candidatePoint;
        if (tile == null) return false;

        Bounds b = tile.Bounds;
        float startY = Mathf.Max(candidatePoint.y + 1f, b.max.y + rayStartYOffset + 0.5f);
        Vector3 origin = new Vector3(candidatePoint.x, startY, candidatePoint.z);

        EnsureBuffer();

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            _hits,
            maxDistance,
            floorMask,
            QueryTriggerInteraction.Ignore
        );

        if (hitCount <= 0) return false;

        bool found = false;
        float highestY = float.NegativeInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            var h = _hits[i];
            if (h.collider == null) continue;
            if (h.normal.y < minNormalY) continue;

            var hitTile = h.collider.GetComponentInParent<Tile>();
            if (hitTile != tile) continue;

            if (h.point.y > highestY)
            {
                highestY = h.point.y;
                found = true;
            }
        }

        if (!found) return false;

        float minSafeY = highestY + spawnYOffset;
        if (candidatePoint.y < minSafeY)
            adjustedPoint = new Vector3(candidatePoint.x, minSafeY, candidatePoint.z);

        return true;
    }
}
