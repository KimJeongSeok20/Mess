// 2025-09-04 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

[RequireComponent(typeof(NavMeshLink))]
public class DoorNavLink : MonoBehaviour
{
    public DoorInteractable door;

    [Header("Geometry")]
    [Tooltip("문 통과 방향의 반 깊이(로컬 X축) + 여유")]
    public float halfDepth = 0.6f;     // 문의 반 깊이 + 여유(문 크기)
    [Tooltip("링크 폭(로컬 Z축) = 에이전트 지름 + 여유")]
    public float width = 1.5f;         // >= 2*Agent.radius + 0.2
    public bool bidirectional = true;
    public float sampleRadius = 1.0f;

    [Header("Extra")]
    [Tooltip("링크 끝점에서 살짝 물러나는 거리(문틀 간섭 완화)")]
    public float edgeBackoff = 0.25f;

    [Header("NavMesh")]
    public int agentTypeID = -1;
    public int area = 0;

    private NavMeshLink link;

    // ---- Registry ---------------------------------------------------------
    private static readonly HashSet<DoorNavLink> s_All = new HashSet<DoorNavLink>();
    public static IReadOnlyCollection<DoorNavLink> All => s_All;

    private void OnEnable()
    {
        s_All.Add(this);
    }

    private void OnDisable()
    {
        s_All.Remove(this);
    }
    // ----------------------------------------------------------------------

    private void Awake()
    {
        link = GetComponent<NavMeshLink>();

        // 기본 세팅
        link.width = width;
        link.bidirectional = bidirectional;
        link.area = area;

        if (agentTypeID < 0)
        {
            var surf = FindFirstObjectByType<NavMeshSurface>();
            if (surf) agentTypeID = surf.agentTypeID;
        }
        link.agentTypeID = agentTypeID;

        UpdateEnds(snapToNav: true);
        link.enabled = (door == null) ? true : door.IsOpen;
    }

    private void LateUpdate()
    {
        // 문짝이 움직일 수도 있으니 끝점 갱신
        UpdateEnds(snapToNav: false);

        // 열림/닫힘에 맞춰 링크 토글
        if (door) link.enabled = door.IsOpen;
    }

    private void OnValidate()
    {
        if (!link) link = GetComponent<NavMeshLink>();
        if (!link) return;

        link.width = width;
        link.bidirectional = bidirectional;
        link.area = area;
        link.agentTypeID = agentTypeID < 0 ? link.agentTypeID : agentTypeID;
        UpdateEnds(snapToNav: true);
    }

    /// 링크의 시작/끝 점을 로컬 X축 기준으로 배치
    private void UpdateEnds(bool snapToNav)
    {
        // 로컬 기준: -X → +X 방향으로 통과
        var start = Vector3.left * (halfDepth + edgeBackoff);
        var end = Vector3.right * (halfDepth + edgeBackoff);

        if (snapToNav)
        {
            if (NavMesh.SamplePosition(transform.TransformPoint(start), out var h1, sampleRadius, NavMesh.AllAreas))
                start = transform.InverseTransformPoint(h1.position);

            if (NavMesh.SamplePosition(transform.TransformPoint(end), out var h2, sampleRadius, NavMesh.AllAreas))
                end = transform.InverseTransformPoint(h2.position);
        }

        link.startPoint = start;
        link.endPoint = end;
        link.UpdateLink();
    }

    // -------------------- Helpers for AI -----------------------------------

    /// from 위치 기준, 문에 접근 가능한 '접근점' 계산 (문틀에 부딪히지 않게 backoff 적용)
    public bool TryGetApproach(Vector3 from, float backoff, out Vector3 approach)
    {
        var wsStart = transform.TransformPoint(link.startPoint);
        var wsEnd = transform.TransformPoint(link.endPoint);

        // from이 어느 쪽(로컬 ±X)인지 판정
        Vector3 doorRight = transform.right;
        bool fromStartSide = Vector3.Dot(from - transform.position, doorRight) < 0f;

        Vector3 target = fromStartSide ? wsStart : wsEnd;
        Vector3 dir = fromStartSide ? -doorRight : doorRight;

        Vector3 p = target - dir * Mathf.Max(0.1f, backoff);
        if (NavMesh.SamplePosition(p, out var hit, sampleRadius, NavMesh.AllAreas))
        {
            approach = hit.position;
            return true;
        }
        approach = target;
        return true;
    }

    /// worldPos가 이 링크 스트립(직사각형) 안에 있는지 간단 판정 (XZ 평면 기준)
    public bool ContainsXZ(Vector3 worldPos, float padding = 0f)
    {
        var local = transform.InverseTransformPoint(worldPos);
        float halfW = (link.width * 0.5f) + padding;
        float halfX = (halfDepth + edgeBackoff) + padding;
        return Mathf.Abs(local.z) <= halfW && Mathf.Abs(local.x) <= halfX;
    }

    /// 현재 위치 아래에 활성화된 문 링크가 있는지(= 스트립 안에 있는지) 찾기
    public static bool TryGetLinkUnder(Vector3 worldPos, float padding, out DoorNavLink link)
    {
        foreach (var d in s_All)
        {
            if (d != null && d.isActiveAndEnabled && d.link != null && d.link.enabled)
            {
                if (d.ContainsXZ(worldPos, padding))
                {
                    link = d;
                    return true;
                }
            }
        }
        link = null;
        return false;
    }

    /// 가장 가까운 문 링크를 검색 (필요 시 사용)
    public static DoorNavLink FindNearest(Vector3 worldPos, float maxDist = 5f)
    {
        DoorNavLink best = null;
        float bestSqr = (maxDist <= 0f) ? float.PositiveInfinity : maxDist * maxDist;

        foreach (var d in s_All)
        {
            if (d == null || !d.isActiveAndEnabled) continue;
            float sqr = (d.transform.position - worldPos).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = d; }
        }
        return best;
    }
}
