using System.Collections.Generic;
using PurrNet;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class OctopusSwarmController : NetworkBehaviour, IMonsterFlowTelemetry
{
    private enum OctopusPresentationEvent
    {
        AttackWindup,
        Lunge,
        Hit,
        Damaged,
        MemberDeath,
        MemberExplosiveDeath,
        SwarmDefeated
    }

    [Header("Members")]
    [Tooltip("이 스웜 루트가 살아 있는 모든 문어 멤버를 관리합니다.")]
    [SerializeField] private List<OctopusSwarmMember> members = new();
    [SerializeField] private MonsterDoorTraversalGroup doorTraversalGroup;
    [SerializeField] private OctopusSwarmPresentation presentation;

    [Header("Swarm Motion")]
    [Tooltip("순찰하거나 추적할 때 보이지 않는 스웜 중심이 움직이는 속도입니다.")]
    [SerializeField] private float moveSpeed = 1.4f;
    [SerializeField] private float patrolMoveSpeedMultiplier = 0.5f;
    [Tooltip("스웜 중심이 이동 방향을 바라보도록 회전하는 속도입니다.")]
    [SerializeField] private float turnSpeed = 5f;
    [Tooltip("플레이어를 발견하지 못했을 때 원래 스폰 지점 주변을 순찰하는 반경입니다.")]
    [SerializeField] private float patrolRadius = 4f;
    [Tooltip("스웜 중심이 목표 지점에 이 거리만큼 가까워지면 다음 순찰 지점을 고를 수 있습니다.")]
    [SerializeField] private float retargetDistance = 0.6f;
    [Tooltip("다음 순찰 지점을 고르기 전에 현재 지점에 머무는 시간 범위입니다.")]
    [SerializeField] private Vector2 lingerDurationRange = new(1.5f, 3.5f);
    [Tooltip("울퉁불퉁한 높이를 따라가지 않고 처음 잡은 Y 평면을 유지합니다.")]
    [SerializeField] private bool lockHeightToInitialPlane = true;
    [Tooltip("스웜 중심이 목표 속도까지 가속하는 속도입니다.")]
    [SerializeField] private float centerAcceleration = 5.5f;
    [Tooltip("스웜 중심이 목적지 근처에서 감속하는 속도입니다.")]
    [SerializeField] private float centerDeceleration = 7.5f;
    [Tooltip("스웜 중심이 이 거리 안으로 들어오면 목적지에 도착한 것으로 봅니다.")]
    [SerializeField] private float centerArrivalRadius = 2.5f;

    [Header("Patrol Formation")]
    [Tooltip("멤버들이 스웜 중심 주변에서 대기할 때 사용하는 기본 포메이션 반경입니다.")]
    [SerializeField] private float patrolFormationRadius = 1.8f;
    [Tooltip("순찰 중 원을 그리며 도는 속도입니다.")]
    [SerializeField] private float patrolOrbitSpeed = 0.45f;
    [SerializeField] private float patrolSlotPhaseJitter = 0.55f;
    [SerializeField] private float patrolSlotRadiusJitter = 0.45f;
    [SerializeField] private float patrolSlotDriftStrength = 0.28f;
    [Tooltip("포메이션이 너무 딱딱해 보이지 않도록 각 멤버가 슬롯에서 흔들리는 정도입니다.")]
    [SerializeField] private float patrolWanderStrength = 0.3f;
    [Tooltip("일부 멤버를 바깥 순찰 링으로 밀어내는 추가 반경입니다.")]
    [SerializeField] private float patrolOuterRingOffset = 0.55f;

    [Header("Alert Chasing")]
    [Tooltip("플레이어를 봤더라도 스웜 중심에서 이 거리 밖이면 유효한 추적 대상으로 보지 않습니다.")]
    [SerializeField] private float chaseRadius = 16f;
    [Tooltip("추적 중 스웜 중심이 플레이어와 유지하려는 거리입니다.")]
    [SerializeField] private float chaseStopDistance = 2.85f;
    [Tooltip("플레이어를 발견한 뒤 주변을 감싸며 도는 속도입니다.")]
    [SerializeField] private float chaseOrbitSpeed = 0.7f;
    [Tooltip("전투 직전까지 유지하는 기본 추적 링 반경입니다.")]
    [SerializeField] private float chaseRingRadius = 3f;
    [Tooltip("경계 상태에서 일부 멤버가 사용하는 안쪽 포위 반경입니다.")]
    [SerializeField] private float encircleInnerRadius = 2.6f;
    [Tooltip("경계 상태에서 나머지 멤버가 사용하는 바깥 포위 반경입니다.")]
    [SerializeField] private float encircleOuterRadius = 3.7f;
    [Tooltip("시야를 놓친 뒤 마지막으로 본 플레이어 위치를 기억하는 시간입니다.")]
    [SerializeField] private float alertMemoryDuration = 5f;
    [Tooltip("타깃을 놓쳤지만 기억 중일 때 마지막 목격 지점 주변을 탐색하는 반경입니다.")]
    [SerializeField] private float alertSearchRadius = 1.75f;

    [Header("Combat")]
    [Tooltip("멤버들이 플레이어 주변 근접 전투 위치를 유지하려고 할 때 사용하는 반경입니다.")]
    [SerializeField] private float closeCombatRingRadius = 1.1f;

    [Header("Debug")]
    [Tooltip("씬 뷰에 스웜 전체 디버그 기즈모를 그립니다.")]
    [SerializeField] private bool drawGizmos = true;
    [Tooltip("순찰, 경계, 근접 전투 슬롯을 멤버별 기즈모로 표시합니다.")]
    [SerializeField] private bool drawSlotGizmos = true;
    [Tooltip("순찰 영역과 링을 표시할 때 쓰는 색상입니다.")]
    [SerializeField] private Color patrolColor = new(0.2f, 0.9f, 1f, 0.25f);
    [Tooltip("스웜 중심 마커 색상입니다.")]
    [SerializeField] private Color centerColor = new(1f, 0.7f, 0.2f, 1f);
    [Tooltip("경계/추적 상태 기즈모 색상입니다.")]
    [SerializeField] private Color alertColor = new(1f, 0.35f, 0.25f, 0.3f);
    [Tooltip("타깃과 목적지 마커 색상입니다.")]
    [SerializeField] private Color destinationColor = new(0.85f, 0.95f, 1f, 0.95f);

    private readonly List<OctopusSwarmMember> _memberBuffer = new();
    private readonly SyncVar<ulong> _deadMemberMask = new(ownerAuth: false);
    private readonly SyncVar<ulong> _explosiveDeathMemberMask = new(ownerAuth: false);
    private Vector3 _originCenter;
    private Vector3 _currentCenter;
    private Vector3 _destinationCenter;
    private Vector3 _centerVelocity;
    private Vector3 _lastSeenTargetPosition;
    private Vector3 _lastApproachDirection = Vector3.forward;
    private float _planeY;
    private float _lingerUntil;
    private float _lastSightedAt = float.NegativeInfinity;
    private bool _initialized;
    private bool _isDespawning;
    private bool _hasPresentedIntent;
    private bool _swarmDefeatedPresented;
    private int _formationSlotCount = 1;
    private MonsterIntent _lastPresentedIntent;
    private Transform _currentTarget;

    public IReadOnlyList<OctopusSwarmMember> Members => members;
    public Vector3 CurrentCenter => _currentCenter;
    public Vector3 DestinationCenter => _destinationCenter;
    public Vector3 CenterVelocity => _centerVelocity;
    public Transform CurrentTarget => HasVisibleTarget ? _currentTarget : null;
    public Vector3 AlertFocusPoint => IsAlerted ? _lastSeenTargetPosition : _currentCenter;
    public Vector3 SwarmForward { get; private set; } = Vector3.forward;
    public float ChaseStopDistance => chaseStopDistance;
    public float ChaseRingRadius => chaseRingRadius;
    public float PatrolWanderStrength => patrolWanderStrength;
    public bool HasVisibleTarget => _currentTarget != null && Time.time <= _lastSightedAt + 0.35f;
    public bool IsAlerted => _currentTarget != null && Time.time <= _lastSightedAt + alertMemoryDuration;
    public MonsterIntent CurrentIntent
    {
        get
        {
            if (_isDespawning || GetAliveMemberCount() == 0)
                return MonsterIntent.Dead;

            if (IsAttacking)
                return MonsterIntent.Attack;

            if (HasVisibleTarget)
                return MonsterIntent.Chase;

            if (IsAlerted)
                return MonsterIntent.Investigate;

            return MonsterIntent.Patrol;
        }
    }

    public bool HasTarget => _currentTarget != null;

    public string CurrentTargetName => _currentTarget != null ? _currentTarget.name : string.Empty;

    public bool IsTraversing => HasTraversingMember();

    public bool IsAttacking => GetAttackingMemberCount() > 0;

    public string CurrentAttackPhase => BuildAttackPhaseSummary();

    public Vector3 DesiredDestination => _destinationCenter;
    public MonsterDoorTraversalGroup DoorTraversalGroup => doorTraversalGroup;
    public ulong DeadMemberMask => _deadMemberMask.value;
    public ulong ExplosiveDeathMemberMask => _explosiveDeathMemberMask.value;

    private void OnEnable()
    {
        MonsterNoise.Reported += OnNoiseReported;
    }

    private void OnDisable()
    {
        MonsterNoise.Reported -= OnNoiseReported;
    }

    /// <summary>Server only: an idle swarm drifts toward a loud noise in range.</summary>
    private void OnNoiseReported(Vector3 position, float radius, NoiseKind kind)
    {
        if (!HasServerAuthority || _isDespawning || members.Count == 0)
            return;

        if (_currentTarget != null)
            return;

        if ((position - _currentCenter).sqrMagnitude > radius * radius)
            return;

        _destinationCenter = ClampToSwarmPlane(position);
        _lingerUntil = Time.time + 2f;
    }

    private void Awake()
    {
        EnsureDoorTraversalGroup();
        EnsurePresentation();
        RebuildMembers();
        InitializeCenter();
        ApplyMemberSimulationAuthority();
    }

    protected override void OnSpawned()
    {
        base.OnSpawned();
        _isDespawning = false;
        EnsureDoorTraversalGroup();
        EnsurePresentation();
        RebuildMembers();
        InitializeCenter(true);
        ApplyMemberSimulationAuthority();
        _deadMemberMask.onChanged -= OnDeadMemberMaskChanged;
        _deadMemberMask.onChanged += OnDeadMemberMaskChanged;
        _explosiveDeathMemberMask.onChanged -= OnExplosiveDeathMemberMaskChanged;
        _explosiveDeathMemberMask.onChanged += OnExplosiveDeathMemberMaskChanged;
        if (isServer)
        {
            _deadMemberMask.value = 0UL;
            _explosiveDeathMemberMask.value = 0UL;
        }
        ApplyDeadMemberMask(_deadMemberMask.value);
        _hasPresentedIntent = false;
        _swarmDefeatedPresented = false;
    }

    protected override void OnDespawned()
    {
        base.OnDespawned();
        _deadMemberMask.onChanged -= OnDeadMemberMaskChanged;
        _explosiveDeathMemberMask.onChanged -= OnExplosiveDeathMemberMaskChanged;
        _currentTarget = null;
    }

    private void OnApplicationQuit()
    {
        // Member OnDisable callbacks during shutdown are not a combat defeat.
        _isDespawning = true;
    }

    private void OnValidate()
    {
        patrolRadius = Mathf.Max(0.5f, patrolRadius);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        patrolMoveSpeedMultiplier = Mathf.Clamp(patrolMoveSpeedMultiplier, 0.1f, 1f);
        turnSpeed = Mathf.Max(0f, turnSpeed);
        retargetDistance = Mathf.Max(0.05f, retargetDistance);
        centerAcceleration = Mathf.Max(0.1f, centerAcceleration);
        centerDeceleration = Mathf.Max(0.1f, centerDeceleration);
        centerArrivalRadius = Mathf.Max(retargetDistance, centerArrivalRadius);
        patrolFormationRadius = Mathf.Max(0.5f, patrolFormationRadius);
        patrolOrbitSpeed = Mathf.Max(0f, patrolOrbitSpeed);
        patrolSlotPhaseJitter = Mathf.Max(0f, patrolSlotPhaseJitter);
        patrolSlotRadiusJitter = Mathf.Max(0f, patrolSlotRadiusJitter);
        patrolSlotDriftStrength = Mathf.Max(0f, patrolSlotDriftStrength);
        patrolWanderStrength = Mathf.Clamp01(patrolWanderStrength);
        patrolOuterRingOffset = Mathf.Max(0f, patrolOuterRingOffset);
        chaseRadius = Mathf.Max(0.5f, chaseRadius);
        chaseStopDistance = Mathf.Clamp(chaseStopDistance, 0.25f, chaseRadius);
        chaseOrbitSpeed = Mathf.Max(0f, chaseOrbitSpeed);
        chaseRingRadius = Mathf.Max(0.5f, chaseRingRadius);
        encircleInnerRadius = Mathf.Max(0.5f, encircleInnerRadius);
        encircleOuterRadius = Mathf.Max(encircleInnerRadius, encircleOuterRadius);
        alertMemoryDuration = Mathf.Max(0.25f, alertMemoryDuration);
        alertSearchRadius = Mathf.Max(0.25f, alertSearchRadius);
        closeCombatRingRadius = Mathf.Max(0.5f, closeCombatRingRadius);
        lingerDurationRange.x = Mathf.Max(0f, lingerDurationRange.x);
        lingerDurationRange.y = Mathf.Max(lingerDurationRange.x, lingerDurationRange.y);
    }

    private void Update()
    {
        if (!_initialized)
            InitializeCenter();

        if (!HasServerAuthority)
        {
            _currentCenter = ComputeMemberAverage();
            UpdatePresentationAnchor();
            return;
        }

        if (GetAliveMemberCount() == 0)
        {
            UpdatePresentationAnchor();
            UpdatePresentationState();
            return;
        }

        TickCenterMotion();
        UpdatePresentationAnchor();
        UpdatePresentationState();
    }

    public void RebuildMembers()
    {
        EnsureDoorTraversalGroup();
        EnsurePresentation();
        _memberBuffer.Clear();

        for (int i = members.Count - 1; i >= 0; i--)
        {
            if (members[i] == null)
                members.RemoveAt(i);
        }

        OctopusSwarmMember[] found = GetComponentsInChildren<OctopusSwarmMember>(true);
        for (int i = 0; i < found.Length; i++)
        {
            OctopusSwarmMember member = found[i];
            if (member == null || member.Controller != this)
                continue;

            _memberBuffer.Add(member);
        }

        _memberBuffer.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        members.Clear();
        members.AddRange(_memberBuffer);

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] != null && members[i].StableMemberId < 0)
                members[i].SetStableMemberId(i);
        }

        _formationSlotCount = 1;
        ExpandFormationSlotCount();
        AssignMemberIndices();
    }

    public void Register(OctopusSwarmMember member)
    {
        if (member == null || member.IsDead || members.Contains(member))
            return;

        members.Add(member);
        members.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        ExpandFormationSlotCount();
        AssignMemberIndices();
    }

    public void Unregister(OctopusSwarmMember member)
    {
        if (member == null)
            return;

        if (members.Remove(member))
            AssignMemberIndices();

        if (Application.isPlaying && HasServerAuthority && !_isDespawning && members.Count == 0)
            HandleSwarmDefeated();
    }

    public void NotifyMemberDefeated(OctopusSwarmMember member, bool explosive)
    {
        if (member == null || !HasServerAuthority)
            return;

        int stableId = member.StableMemberId;
        if (stableId >= 0 && stableId < 64)
        {
            if (explosive)
                _explosiveDeathMemberMask.value |= 1UL << stableId;
            _deadMemberMask.value |= 1UL << stableId;
        }

        AssignMemberIndices();

        if (Application.isPlaying && GetAliveMemberCount() == 0)
            PresentSwarmDefeated();
    }

    public void ReportTargetSighted(OctopusSwarmMember member, PlayerPawn targetPawn, Vector3 targetPosition)
    {
        if (member == null || targetPawn == null)
            return;

        Vector3 sightPosition = ClampToSwarmPlane(targetPosition);
        Vector3 toSight = sightPosition - _currentCenter;
        toSight.y = 0f;
        if (toSight.sqrMagnitude > chaseRadius * chaseRadius)
            return;

        _currentTarget = targetPawn.transform;
        _lastSeenTargetPosition = sightPosition;
        _lastSightedAt = Time.time;

        if (toSight.sqrMagnitude > 0.0001f)
            _lastApproachDirection = toSight.normalized;
    }

    public void ReportMemberAttackWindup(OctopusSwarmMember member, Vector3 position, Quaternion rotation)
    {
        if (!CanDrivePresentation(member))
            return;

        PlayMemberPresentationNetworked(OctopusPresentationEvent.AttackWindup, member, position, rotation);
    }

    public void ReportMemberLunge(OctopusSwarmMember member, Vector3 position, Quaternion rotation)
    {
        if (!CanDrivePresentation(member))
            return;

        PlayMemberPresentationNetworked(OctopusPresentationEvent.Lunge, member, position, rotation);
    }

    public void ReportMemberHit(OctopusSwarmMember member, Vector3 position, Quaternion rotation)
    {
        if (!CanDrivePresentation(member))
            return;

        PlayMemberPresentationNetworked(OctopusPresentationEvent.Hit, member, position, rotation);
    }

    public void ReportMemberDamaged(OctopusSwarmMember member, Vector3 position, Quaternion rotation)
    {
        if (!CanDrivePresentation(member))
            return;

        PlayMemberPresentationNetworked(OctopusPresentationEvent.Damaged, member, position, rotation);
    }

    public void ReportMemberDeath(
        OctopusSwarmMember member,
        Vector3 position,
        Quaternion rotation,
        bool explosive)
    {
        if (member == null || !HasServerAuthority)
            return;

        PlayMemberPresentationNetworked(
            explosive ? OctopusPresentationEvent.MemberExplosiveDeath : OctopusPresentationEvent.MemberDeath,
            member,
            position,
            rotation);
    }

    public Vector3 ClampToSwarmPlane(Vector3 position)
    {
        if (lockHeightToInitialPlane)
            position.y = _planeY;

        return position;
    }

    public Vector3 GetPatrolSlotPosition(int memberIndex, int totalMembers)
    {
        if (totalMembers <= 0)
            return _currentCenter;

        float angle = GetPatrolAngle(memberIndex, totalMembers);
        float radius = GetPatrolRadius(memberIndex, totalMembers);
        Vector3 offset = AngleToOffset(angle, radius);
        return ClampToSwarmPlane(_currentCenter + offset);
    }

    public Vector3 GetSearchSlotPosition(int memberIndex, int totalMembers)
    {
        if (!IsAlerted || totalMembers <= 0)
            return _currentCenter;

        float angle = GetMemberAngle(memberIndex, totalMembers, chaseOrbitSpeed * 0.6f);
        float radius = alertSearchRadius + GetRingOffset(memberIndex, totalMembers, 0.45f);
        Vector3 offset = AngleToOffset(angle, radius);
        return ClampToSwarmPlane(AlertFocusPoint + offset);
    }

    public Vector3 GetEncircleSlotPosition(int memberIndex, int totalMembers, Vector3 targetPosition)
    {
        if (totalMembers <= 0)
            return ClampToSwarmPlane(targetPosition);

        float angle = GetCombatAngle(memberIndex, totalMembers, chaseOrbitSpeed);
        float radius = totalMembers > 4 && memberIndex % 2 == 0 ? encircleInnerRadius : encircleOuterRadius;
        Vector3 offset = AngleToOffset(angle, radius);
        return ClampToSwarmPlane(targetPosition + offset);
    }

    public Vector3 GetCloseCombatSlotPosition(int memberIndex, int totalMembers, Vector3 targetPosition)
    {
        if (totalMembers <= 0)
            return ClampToSwarmPlane(targetPosition);

        float angle = GetCloseCombatAngle(memberIndex, totalMembers);
        Vector3 offset = AngleToOffset(angle, closeCombatRingRadius);
        return ClampToSwarmPlane(targetPosition + offset);
    }

    private float GetMemberAngle(int memberIndex, int totalMembers, float orbitSpeed)
    {
        float baseAngle = totalMembers <= 0 ? 0f : memberIndex * (Mathf.PI * 2f / totalMembers);
        return baseAngle + Time.time * orbitSpeed;
    }

    private float GetPatrolAngle(int memberIndex, int totalMembers)
    {
        float baseAngle = totalMembers <= 0 ? 0f : memberIndex * (Mathf.PI * 2f / totalMembers);
        float seed = GetMemberSeed(memberIndex);
        float fixedOffset = Mathf.Sin(seed) * patrolSlotPhaseJitter;
        float drift = Mathf.Sin(Time.time * 0.17f + seed * 1.7f) * patrolSlotDriftStrength;
        return baseAngle + Time.time * patrolOrbitSpeed + fixedOffset + drift;
    }

    private float GetPatrolRadius(int memberIndex, int totalMembers)
    {
        float seed = GetMemberSeed(memberIndex);
        float ringOffset = GetRingOffset(memberIndex, totalMembers, patrolOuterRingOffset);
        float fixedOffset = Mathf.Sin(seed * 2.13f) * patrolSlotRadiusJitter;
        float drift = Mathf.Sin(Time.time * 0.11f + seed * 0.63f) * patrolSlotRadiusJitter * 0.35f;
        return Mathf.Max(0.45f, patrolFormationRadius + ringOffset + fixedOffset + drift);
    }

    private float GetCombatAngle(int memberIndex, int totalMembers, float orbitSpeed)
    {
        Vector3 approach = GetApproachVectorFromTarget(AlertFocusPoint);
        float anchor = Mathf.Atan2(approach.x, approach.z);
        float baseAngle = totalMembers <= 0 ? 0f : memberIndex * (Mathf.PI * 2f / totalMembers);
        return anchor + baseAngle + Time.time * orbitSpeed;
    }

    private float GetCloseCombatAngle(int memberIndex, int totalMembers)
    {
        Vector3 approach = GetApproachVectorFromTarget(AlertFocusPoint);
        float anchor = Mathf.Atan2(approach.x, approach.z);
        float baseAngle = totalMembers <= 0 ? 0f : memberIndex * (Mathf.PI * 2f / totalMembers);
        return anchor + baseAngle;
    }

    private Vector3 GetApproachVectorFromTarget(Vector3 targetPosition)
    {
        Vector3 fromTarget = _currentCenter - targetPosition;
        fromTarget.y = 0f;
        if (fromTarget.sqrMagnitude <= 0.0001f)
            fromTarget = _lastApproachDirection.sqrMagnitude > 0.0001f ? _lastApproachDirection : -SwarmForward;

        if (fromTarget.sqrMagnitude <= 0.0001f)
            fromTarget = Vector3.back;

        return fromTarget.normalized;
    }

    private static float GetRingOffset(int memberIndex, int totalMembers, float offset)
    {
        if (totalMembers < 5 || offset <= 0f)
            return 0f;

        return memberIndex % 2 == 0 ? -offset * 0.4f : offset;
    }

    private static Vector3 AngleToOffset(float angle, float radius)
    {
        return new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
    }

    private static float GetMemberSeed(int memberIndex)
    {
        return (memberIndex + 1) * 12.9898f;
    }

    private void InitializeCenter(bool force = false)
    {
        if (_initialized && !force)
            return;

        _originCenter = ComputeMemberAverage();
        _currentCenter = _originCenter;
        _destinationCenter = _originCenter;
        _lastSeenTargetPosition = _originCenter;
        _centerVelocity = Vector3.zero;
        _planeY = _originCenter.y;
        PickNextDestination(true);
        _initialized = true;
    }

    private void TickCenterMotion()
    {
        PruneTargetState();

        if (TryUpdateAlertDestination())
            _lingerUntil = Time.time;

        Vector3 toTarget = _destinationCenter - _currentCenter;
        Vector3 planar = new Vector3(toTarget.x, 0f, toTarget.z);

        if (!IsAlerted && planar.sqrMagnitude <= retargetDistance * retargetDistance && Time.time >= _lingerUntil)
            PickNextDestination(false);

        float distance = planar.magnitude;
        Vector3 desiredVelocity = Vector3.zero;
        if (distance > retargetDistance)
        {
            float desiredSpeed = IsAlerted ? moveSpeed : moveSpeed * patrolMoveSpeedMultiplier;
            if (distance < centerArrivalRadius)
                desiredSpeed *= Mathf.Clamp01(distance / centerArrivalRadius);

            desiredVelocity = planar.normalized * desiredSpeed;
        }

        float acceleration = desiredVelocity.sqrMagnitude > _centerVelocity.sqrMagnitude
            ? centerAcceleration
            : centerDeceleration;
        _centerVelocity = Vector3.MoveTowards(_centerVelocity, desiredVelocity, acceleration * Time.deltaTime);

        if (distance <= retargetDistance && !IsAlerted)
            _centerVelocity = Vector3.MoveTowards(_centerVelocity, Vector3.zero, centerDeceleration * Time.deltaTime);

        if (_centerVelocity.sqrMagnitude <= 0.000001f)
            return;

        Vector3 step = _centerVelocity * Time.deltaTime;
        if (step.sqrMagnitude > planar.sqrMagnitude && desiredVelocity.sqrMagnitude > 0.0001f)
            step = planar;

        _currentCenter += step;
        _currentCenter = ClampToSwarmPlane(_currentCenter);

        Vector3 velocityForward = new Vector3(_centerVelocity.x, 0f, _centerVelocity.z);
        if (velocityForward.sqrMagnitude > 0.0001f)
        {
            SwarmForward = Vector3.Slerp(SwarmForward, velocityForward.normalized, Time.deltaTime * turnSpeed);
        }
    }

    private void PickNextDestination(bool immediate)
    {
        Vector2 random = Random.insideUnitCircle * patrolRadius;
        Vector3 candidate = _originCenter + new Vector3(random.x, 0f, random.y);
        candidate = ClampToSwarmPlane(candidate);

        if (TrySampleSwarmNavMesh(candidate, Mathf.Max(1.5f, patrolRadius), out NavMeshHit hit))
            _destinationCenter = ClampToSwarmPlane(hit.position);
        else
            _destinationCenter = candidate;

        float linger = Random.Range(lingerDurationRange.x, lingerDurationRange.y);
        _lingerUntil = immediate ? Time.time : Time.time + linger;
    }

    private bool TryUpdateAlertDestination()
    {
        if (!IsAlerted)
            return false;

        Vector3 desiredCenter;
        if (HasVisibleTarget)
        {
            Vector3 awayFromTarget = _currentCenter - _lastSeenTargetPosition;
            awayFromTarget.y = 0f;
            if (awayFromTarget.sqrMagnitude <= 0.0001f)
                awayFromTarget = _lastApproachDirection.sqrMagnitude > 0.0001f ? _lastApproachDirection : -SwarmForward;

            awayFromTarget.y = 0f;
            awayFromTarget.Normalize();

            desiredCenter = _lastSeenTargetPosition + awayFromTarget * chaseStopDistance;
        }
        else
        {
            float searchAngle = Time.time * chaseOrbitSpeed;
            Vector3 searchOffset = AngleToOffset(searchAngle, alertSearchRadius);
            desiredCenter = _lastSeenTargetPosition + searchOffset;
        }

        desiredCenter = ClampToSwarmPlane(desiredCenter);

        if (TrySampleSwarmNavMesh(desiredCenter, Mathf.Max(1.5f, chaseStopDistance + 1f), out NavMeshHit hit))
            _destinationCenter = ClampToSwarmPlane(hit.position);
        else
            _destinationCenter = desiredCenter;

        return true;
    }

    private bool TrySampleSwarmNavMesh(Vector3 position, float maxDistance, out NavMeshHit hit)
    {
        for (int i = 0; i < members.Count; i++)
        {
            OctopusSwarmMember member = members[i];
            if (member == null || member.IsDead)
                continue;

            NavMeshQueryFilter filter = new NavMeshQueryFilter
            {
                agentTypeID = member.NavMeshAgentTypeId,
                areaMask = member.NavMeshAreaMask
            };
            return NavMesh.SamplePosition(position, out hit, maxDistance, filter);
        }

        return NavMesh.SamplePosition(position, out hit, maxDistance, NavMesh.AllAreas);
    }

    private void PruneTargetState()
    {
        if (_currentTarget != null && !_currentTarget.gameObject.activeInHierarchy)
            _currentTarget = null;

        if (_currentTarget == null)
            return;

        if (Time.time > _lastSightedAt + alertMemoryDuration)
            _currentTarget = null;
    }

    private Vector3 ComputeMemberAverage()
    {
        if (members.Count == 0)
            return transform.position;

        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < members.Count; i++)
        {
            OctopusSwarmMember member = members[i];
            if (member == null || member.IsDead)
                continue;

            sum += member.transform.position;
            count++;
        }

        return count > 0 ? sum / count : transform.position;
    }

    private void AssignMemberIndices()
    {
        EnsureDoorTraversalGroup();
        ExpandFormationSlotCount();
        int fallbackIndex = 0;

        for (int i = 0; i < members.Count; i++)
        {
            OctopusSwarmMember member = members[i];
            if (member == null)
                continue;

            int slotIndex = member.StableMemberId >= 0 ? member.StableMemberId : fallbackIndex;
            member.SetMemberIndex(slotIndex, _formationSlotCount);
            fallbackIndex++;
        }
    }

    private void ExpandFormationSlotCount()
    {
        int requiredSlotCount = Mathf.Max(1, members.Count);
        for (int i = 0; i < members.Count; i++)
        {
            OctopusSwarmMember member = members[i];
            if (member != null && member.StableMemberId >= 0)
                requiredSlotCount = Mathf.Max(requiredSlotCount, member.StableMemberId + 1);
        }

        _formationSlotCount = Mathf.Max(_formationSlotCount, requiredSlotCount);
    }

    private int GetAliveMemberCount()
    {
        int aliveCount = 0;
        for (int i = 0; i < members.Count; i++)
        {
            OctopusSwarmMember member = members[i];
            if (member != null && !member.IsDead)
                aliveCount++;
        }

        return aliveCount;
    }

    private int GetAttackingMemberCount()
    {
        int attackCount = 0;
        for (int i = 0; i < members.Count; i++)
        {
            OctopusSwarmMember member = members[i];
            if (member != null && !member.IsDead && member.IsInAttackSequence)
                attackCount++;
        }

        return attackCount;
    }

    private bool HasTraversingMember()
    {
        for (int i = 0; i < members.Count; i++)
        {
            OctopusSwarmMember member = members[i];
            if (member != null && !member.IsDead && member.IsTraversingDoor)
                return true;
        }

        return false;
    }

    private void EnsureDoorTraversalGroup()
    {
        if (doorTraversalGroup == null)
            doorTraversalGroup = GetComponent<MonsterDoorTraversalGroup>();

        if (doorTraversalGroup == null)
            doorTraversalGroup = gameObject.AddComponent<MonsterDoorTraversalGroup>();
    }

    private void EnsurePresentation()
    {
        if (presentation == null)
            presentation = GetComponent<OctopusSwarmPresentation>();
    }

    public bool HasServerAuthority
    {
        get
        {
            if (isSpawned)
                return isServer;

            NetworkManager networkManager = NetworkManager.main;
            return networkManager == null || networkManager.isServer;
        }
    }

    private bool CanBroadcastPresentation => isSpawned && HasServerAuthority;

    private bool CanDrivePresentation(OctopusSwarmMember member)
    {
        return member != null && !member.IsDead && HasServerAuthority;
    }

    private void ApplyMemberSimulationAuthority()
    {
        for (int i = 0; i < members.Count; i++)
            members[i]?.ApplySimulationAuthority();
    }

    private void OnDeadMemberMaskChanged(ulong deadMask)
    {
        ApplyDeadMemberMask(deadMask);
    }

    private void OnExplosiveDeathMemberMaskChanged(ulong explosiveMask)
    {
        ApplyDeadMemberMask(_deadMemberMask.value);
    }

    private void ApplyDeadMemberMask(ulong deadMask)
    {
        for (int i = 0; i < members.Count; i++)
        {
            OctopusSwarmMember member = members[i];
            if (member == null || member.StableMemberId < 0 || member.StableMemberId >= 64)
                continue;

            if ((deadMask & (1UL << member.StableMemberId)) != 0UL)
            {
                bool explosive = (_explosiveDeathMemberMask.value & (1UL << member.StableMemberId)) != 0UL;
                member.ApplyReplicatedDeath(explosive);
            }
        }
    }

    private void UpdatePresentationState()
    {
        if (!HasServerAuthority)
            return;

        MonsterIntent intent = CurrentIntent;
        if (_hasPresentedIntent && intent == _lastPresentedIntent)
            return;

        MonsterIntent previous = _hasPresentedIntent ? _lastPresentedIntent : intent;
        _hasPresentedIntent = true;
        _lastPresentedIntent = intent;

        PlayIntentPresentationNetworked(previous, intent, AlertFocusPoint);
    }

    private void PlayIntentPresentationNetworked(MonsterIntent previous, MonsterIntent current, Vector3 focusPoint)
    {
        PlayIntentPresentationLocal(previous, current, focusPoint);

        if (CanBroadcastPresentation)
            PlayIntentPresentationObserversRpc((int)previous, (int)current, focusPoint);
    }

    private void PlayIntentPresentationLocal(MonsterIntent previous, MonsterIntent current, Vector3 focusPoint)
    {
        EnsurePresentation();
        presentation?.PlayIntentTransition(previous, current, focusPoint);
    }

    private void UpdatePresentationAnchor()
    {
        EnsurePresentation();
        presentation?.SetSwarmAnchor(_currentCenter);
    }

    [ObserversRpc]
    private void PlayIntentPresentationObserversRpc(int previous, int current, Vector3 focusPoint)
    {
        if (HasServerAuthority)
            return;

        PlayIntentPresentationLocal((MonsterIntent)previous, (MonsterIntent)current, focusPoint);
    }

    private void PlayMemberPresentationNetworked(OctopusPresentationEvent eventId, OctopusSwarmMember member, Vector3 position, Quaternion rotation)
    {
        int memberIndex = member != null ? member.StableMemberId : -1;
        PlayMemberPresentationLocal(eventId, member, position, rotation);

        if (CanBroadcastPresentation)
            PlayMemberPresentationObserversRpc((int)eventId, memberIndex, position, rotation);
    }

    private void PlayMemberPresentationLocal(OctopusPresentationEvent eventId, OctopusSwarmMember member, Vector3 position, Quaternion rotation)
    {
        EnsurePresentation();
        if (presentation == null)
            return;

        switch (eventId)
        {
            case OctopusPresentationEvent.AttackWindup:
                presentation.PlayMemberAttackWindup(member, position, rotation);
                break;
            case OctopusPresentationEvent.Lunge:
                presentation.PlayMemberLunge(member, position, rotation);
                break;
            case OctopusPresentationEvent.Hit:
                presentation.PlayMemberHit(member, position, rotation);
                break;
            case OctopusPresentationEvent.Damaged:
                presentation.PlayMemberDamaged(member, position, rotation);
                break;
            case OctopusPresentationEvent.MemberDeath:
                presentation.PlayMemberDeath(member, position, rotation);
                break;
            case OctopusPresentationEvent.MemberExplosiveDeath:
                presentation.PlayMemberExplosiveDeath(member, position, rotation);
                break;
            case OctopusPresentationEvent.SwarmDefeated:
                presentation.PlaySwarmDefeated(position);
                break;
        }
    }

    [ObserversRpc]
    private void PlayMemberPresentationObserversRpc(int eventId, int memberIndex, Vector3 position, Quaternion rotation)
    {
        if (HasServerAuthority)
            return;

        PlayMemberPresentationLocal((OctopusPresentationEvent)eventId, ResolveMemberByIndex(memberIndex), position, rotation);
    }

    private OctopusSwarmMember ResolveMemberByIndex(int memberIndex)
    {
        if (memberIndex < 0)
            return null;

        for (int i = 0; i < members.Count; i++)
        {
            OctopusSwarmMember member = members[i];
            if (member != null && member.StableMemberId == memberIndex)
                return member;
        }

        return memberIndex < members.Count ? members[memberIndex] : null;
    }

    private string BuildAttackPhaseSummary()
    {
        int windupCount = 0;
        int lungeCount = 0;
        int recoverCount = 0;

        for (int i = 0; i < members.Count; i++)
        {
            OctopusSwarmMember member = members[i];
            if (member == null || member.IsDead || !member.IsInAttackSequence)
                continue;

            string phase = member.CurrentAttackPhaseLabel;
            if (phase == "Windup")
            {
                windupCount++;
                continue;
            }

            if (phase == "Lunge")
            {
                lungeCount++;
                continue;
            }

            if (phase == "Recover")
            {
                recoverCount++;
            }
        }

        if (windupCount == 0 && lungeCount == 0 && recoverCount == 0)
            return string.Empty;

        if (lungeCount > 0)
            return $"Lunge x{lungeCount}";

        if (windupCount > 0)
            return $"Windup x{windupCount}";

        return $"Recover x{recoverCount}";
    }

    private void HandleSwarmDefeated()
    {
        if (!HasServerAuthority || _isDespawning)
            return;

        _isDespawning = true;

        PresentSwarmDefeated();

        if (isSpawned)
        {
            Despawn();
            return;
        }

        Destroy(gameObject);
    }

    private void PresentSwarmDefeated()
    {
        if (_swarmDefeatedPresented)
            return;

        _swarmDefeatedPresented = true;
        PlayMemberPresentationNetworked(OctopusPresentationEvent.SwarmDefeated, null, _currentCenter, Quaternion.identity);
        SpawnSwarmTrophy();
    }

    [Header("Trophy")]
    [Tooltip("스웜을 전멸시키면 중심에 떨어지는 전리품 아이템 이름 (인벤토리 아이템 카탈로그/던전 루팅 목록에서 찾는다)")]
    [SerializeField] private string swarmTrophyItemName = "Octopus";

    /// <summary>
    /// The swarm has no corpse; killing all of it leaves one "Octopus" trophy item that the
    /// corpse station accepts for skill points (and that sells like normal loot otherwise).
    /// </summary>
    private void SpawnSwarmTrophy()
    {
        if (!HasServerAuthority || string.IsNullOrEmpty(swarmTrophyItemName))
            return;

        InventoryManager inventory = PurrNet.InstanceHandler.TryGetInstance(out InventoryManager found)
            ? found
            : FindFirstObjectByType<InventoryManager>();
        GameObject prefab = inventory != null ? inventory.GetItemPrefab(swarmTrophyItemName) : null;
        if (prefab == null)
        {
            Debug.LogWarning($"[OctopusSwarmController] Trophy prefab '{swarmTrophyItemName}' not found; no trophy dropped.", this);
            return;
        }

        Vector3 position = _currentCenter + Vector3.up * 0.6f;
        GameObject spawned = Instantiate(prefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        var identity = spawned.GetComponent<PurrNet.NetworkIdentity>();
        if (identity != null && !identity.isSpawned)
            identity.Spawn(prefab);

        Debug.Log($"[OctopusSwarmController] Swarm defeated → dropped trophy '{swarmTrophyItemName}' at {position}", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Vector3 center = Application.isPlaying && _initialized ? _currentCenter : transform.position;
        center = ClampToSwarmPlane(center);

        Gizmos.color = IsAlerted ? alertColor : patrolColor;
        Gizmos.DrawWireSphere(center, patrolRadius);
        Gizmos.color = centerColor;
        Gizmos.DrawSphere(center, 0.25f);

        Gizmos.color = destinationColor;
        Gizmos.DrawWireSphere(Application.isPlaying ? _destinationCenter : center, 0.35f);
        Gizmos.DrawLine(center, Application.isPlaying ? _destinationCenter : center);

        if (IsAlerted)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(AlertFocusPoint, 0.45f);
            Gizmos.DrawWireSphere(AlertFocusPoint, chaseStopDistance);
            Gizmos.DrawWireSphere(AlertFocusPoint, encircleOuterRadius);
        }

        if (!drawSlotGizmos || members.Count == 0)
            return;

        for (int i = 0; i < members.Count; i++)
        {
            OctopusSwarmMember member = members[i];
            if (member == null)
                continue;

            int slotIndex = member.MemberIndex;
            int slotCount = Mathf.Max(1, _formationSlotCount);
            Vector3 slot;
            if (IsAlerted)
            {
                slot = HasVisibleTarget
                    ? GetEncircleSlotPosition(slotIndex, slotCount, AlertFocusPoint)
                    : GetSearchSlotPosition(slotIndex, slotCount);
            }
            else
            {
                slot = GetPatrolSlotPosition(slotIndex, slotCount);
            }

            Gizmos.color = HasVisibleTarget ? Color.red : Color.cyan;
            Gizmos.DrawWireSphere(slot, 0.18f);
            Gizmos.DrawLine(member.transform.position, slot);
        }
    }
}
