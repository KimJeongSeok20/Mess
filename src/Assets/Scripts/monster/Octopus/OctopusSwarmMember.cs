using PurrNet;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class OctopusSwarmMember : MonoBehaviour
{
    private enum AttackPhase
    {
        None,
        Windup,
        Lunge,
        Recover
    }

    [Header("Refs")]
    [Tooltip("포메이션 슬롯, 경계 상태, 공용 타깃 정보를 제공하는 스웜 루트입니다.")]
    [SerializeField] private OctopusSwarmController controller;
    [Tooltip("walk/chase/attack/hit/die 상태를 구동하는 Animator입니다.")]
    [SerializeField] private Animator animator;
    [Tooltip("서버가 구동한 애니메이션 파라미터와 트리거를 클라이언트에 복제합니다.")]
    [SerializeField] private NetworkAnimator networkAnimator;
    [Tooltip("서버 권위 이동을 클라이언트에 보간해서 복제합니다.")]
    [SerializeField] private NetworkTransform networkTransform;
    [Tooltip("이동과 경로 탐색에 사용하는 NavMeshAgent입니다.")]
    [SerializeField] private NavMeshAgent agent;
    [Tooltip("총알과 폭발에 항상 맞을 수 있는 개체별 비트리거 피격 콜라이더입니다.")]
    [SerializeField] private Collider damageCollider;
    [Tooltip("모델의 바운드를 계산해서 바닥 높이를 보정할 때 사용하는 Renderer입니다.")]
    [SerializeField] private Renderer visualRenderer;
    [Tooltip("공격 판정 타이밍에 잠깐 활성화되는 트리거 히트박스입니다.")]
    [SerializeField] private OctopusAttackHitbox attackHitbox;
    [SerializeField] private DoorAutoOpener doorAutoOpener;

    [Header("Presentation")]
    [SerializeField] private AudioSource presentationAudioSource;

    [Header("Motion")]
    [Tooltip("이 멤버의 기본 이동 속도입니다.")]
    [SerializeField] private float moveSpeed = 1.8f;
    [Tooltip("순찰 중에는 슬롯 복귀가 질주처럼 보이지 않도록 기본 이동 속도에 곱합니다.")]
    [SerializeField, Range(0.1f, 1f)] private float patrolSpeedMultiplier = 0.7f;
    [Tooltip("이 멤버가 이동 방향 또는 타깃 방향으로 회전하는 속도입니다.")]
    [SerializeField] private float turnSpeed = 8f;
    [Tooltip("순찰 움직임이 너무 딱딱해 보이지 않도록 주는 로컬 랜덤 오프셋 크기입니다.")]
    [SerializeField] private float localRoamRadius = 1.1f;
    [Tooltip("스웜 중심에서 이 거리 이상 벗어나면 다시 안쪽으로 보정됩니다.")]
    [SerializeField] private float maxDistanceFromCenter = 2.2f;
    [Tooltip("목표 위치에 이 거리만큼 가까워지면 도착한 것으로 봅니다.")]
    [SerializeField] private float targetReachedDistance = 0.18f;
    [Tooltip("새 로컬 랜덤 이동 오프셋을 다시 고를 수 있는 주기 범위입니다.")]
    [SerializeField] private Vector2 retargetIntervalRange = new(1.8f, 3.5f);
    [FormerlySerializedAs("patrolDestinationSmoothing")]
    [Tooltip("느슨한 포메이션 목표가 바뀔 때 목적지를 부드럽게 따라가는 정도입니다.")]
    [SerializeField] private float formationDestinationSmoothing = 2f;
    [FormerlySerializedAs("patrolDestinationRefreshDistance")]
    [Tooltip("느슨한 포메이션 이동에서 이 거리보다 목표가 적게 변하면 재경로하지 않습니다.")]
    [SerializeField] private float formationDestinationRefreshDistance = 0.5f;
    [Tooltip("이동 방향이 아니라 전투 타깃을 바라볼 때 추가로 적용되는 회전 배수입니다.")]
    [SerializeField] private float targetFacingTurnMultiplier = 1.45f;
    [Tooltip("이 거리 안에서는 전투 중 플레이어를 더 우선적으로 바라봅니다.")]
    [SerializeField] private float combatFaceDistance = 10f;

    [Header("Soft Formation")]
    [Tooltip("대략적인 슬롯에서 이 거리 이상 벗어났을 때만 대열 복귀를 시작합니다.")]
    [SerializeField] private float slotReturnEnterDistance = 1.4f;
    [Tooltip("복귀 중 이 거리 안에 들어오면 정확한 슬롯 추적을 멈추고 다시 자유롭게 움직입니다.")]
    [SerializeField] private float slotReturnExitDistance = 0.65f;
    [Tooltip("경계와 포위 상태에서 개인별 느슨한 이동 반경에 곱하는 값입니다.")]
    [SerializeField] private float alertWanderStrength = 0.45f;

    [Header("Sight")]
    [Tooltip("이 멤버가 PlayerPawn을 감지할 수 있는 최대 거리입니다.")]
    [SerializeField] private float sightRange = 14f;
    [Tooltip("시야 원뿔의 반각입니다. 값이 클수록 더 넓게 봅니다.")]
    [SerializeField, Range(10f, 180f)] private float sightHalfAngle = 55f;
    [Tooltip("시야 Raycast 시작점으로 사용하는 눈 높이 오프셋입니다.")]
    [SerializeField] private float sightEyeHeight = 1.35f;
    [Tooltip("플레이어 시야 체크 시 조준하는 높이 오프셋입니다.")]
    [SerializeField] private float sightTargetHeight = 1f;
    [Tooltip("활성 상태에서 시야 체크를 수행하는 주기입니다.")]
    [SerializeField] private float sightRefreshInterval = 0.15f;
    [Tooltip("플레이어와의 시야를 가릴 수 있는 레이어입니다.")]
    [SerializeField] private LayerMask sightOcclusionMask = ~0;

    [Header("Separation")]
    [Tooltip("가까운 문어끼리 너무 겹치지 않도록 서로 밀어내는 반경입니다.")]
    [SerializeField] private float separationRadius = 0.72f;
    [Tooltip("서로 밀어내는 힘의 강도입니다.")]
    [SerializeField] private float separationStrength = 1.15f;

    [Header("Animation")]
    [Tooltip("끄면 AI는 동작하지만 이 스크립트가 Animator 파라미터를 더 이상 제어하지 않습니다.")]
    [SerializeField] private bool driveAnimation = true;
    [Tooltip("이동/걷기 애니메이션에 사용하는 Bool 파라미터 이름입니다.")]
    [SerializeField] private string isMovingParameter = "walk";
    [Tooltip("공격 애니메이션 상태에 사용하는 Bool 파라미터 이름입니다.")]
    [SerializeField] private string isAttackingParameter = "attack";
    [Tooltip("컨트롤러가 idle Bool을 기대할 때 사용하는 파라미터 이름입니다.")]
    [SerializeField] private string idleParameter = "idle";
    [Tooltip("컨트롤러가 chase Bool을 기대할 때 사용하는 파라미터 이름입니다.")]
    [SerializeField] private string chaseParameter = "chase";
    [Tooltip("죽지 않고 피격됐을 때 발동하는 Trigger 파라미터 이름입니다.")]
    [SerializeField] private string hitTriggerParameter = "hit";
    [Tooltip("죽을 때 발동하는 Trigger 파라미터 이름입니다.")]
    [SerializeField] private string dieTriggerParameter = "die";
    [Tooltip("순찰/걷기 상태에서 애니메이션 속도 1.0으로 간주할 기준 이동 속도입니다.")]
    [SerializeField] private float walkAnimationReferenceSpeed = 2.6f;
    [Tooltip("추적 상태에서 애니메이션 속도 1.0으로 간주할 기준 이동 속도입니다.")]
    [SerializeField] private float chaseAnimationReferenceSpeed = 4.6f;
    [Tooltip("이동 애니메이션 재생 속도의 최소값입니다.")]
    [SerializeField] private float minMovementAnimationSpeed = 0.85f;
    [Tooltip("이동 애니메이션 재생 속도의 최대값입니다.")]
    [SerializeField] private float maxMovementAnimationSpeed = 1.45f;

    [Header("Tuning")]
    [Tooltip("비주얼 메시를 NavMesh 높이에 맞출 때 추가로 띄우는 높이입니다.")]
    [SerializeField] private float modelGroundClearance = 0.02f;
    [Tooltip("멤버마다 랜덤하게 적용되는 이동 속도 배수 범위입니다.")]
    [SerializeField] private Vector2 speedVarianceRange = new(0.9f, 1.1f);
    [Tooltip("멤버마다 랜덤하게 적용되는 로컬 이동 배수 범위입니다.")]
    [SerializeField] private Vector2 roamVarianceRange = new(0.85f, 1.2f);
    [Tooltip("멤버마다 랜덤하게 적용되는 회전 속도 배수 범위입니다.")]
    [SerializeField] private Vector2 turnVarianceRange = new(0.9f, 1.15f);
    [Tooltip("이 문어 개체 한 마리의 최대 HP입니다.")]
    [SerializeField] private int maxHealth = 1;
    [Tooltip("이 문어 개체의 현재 HP입니다. 활성화될 때 보통 최대 HP로 초기화됩니다.")]
    [SerializeField] private int currentHealth = 1;
    [Tooltip("죽음 애니메이션 후 이 개체가 비활성화되기까지의 지연 시간입니다.")]
    [SerializeField] private float deathDisableDelay = 0.7f;
    [Tooltip("사망 RPC와 프레젠테이션에서 사용하는 변하지 않는 개체 ID입니다.")]
    [SerializeField] private int stableMemberId = -1;

    [Header("Attack")]
    [Tooltip("이 멤버가 공격을 아예 할 수 있는지 결정하는 전체 스위치입니다.")]
    [SerializeField] private bool enableAttack = true;
    [Tooltip("공격이 성공했을 때 한 번에 주는 피해량입니다. (런타임에 baseAttackDamage로 덮어씀)")]
    [SerializeField] private int attackDamage = 1;
    [Tooltip("스웜 멤버 한 마리의 실제 공격 피해량. 100 HP 기준 8이면 7마리가 붙었을 때 실제 압박이 된다.")]
    [SerializeField, Min(1)] private int baseAttackDamage = 8;
    [Tooltip("Animator의 공격 상태 이름. FBX 기본 컨트롤러는 '아마튜어|Attack'처럼 레이어 접두어가 붙는다.")]
    [SerializeField] private string attackStateName = "아마튜어|Attack";
    [Tooltip("플레이어가 이 거리 밖에 있으면 공격을 시작하지 않습니다.")]
    [SerializeField] private float attackEngageDistance = 7f;
    [Tooltip("공격 준비 단계에서 플레이어와 유지하려는 거리입니다.")]
    [SerializeField] private float attackHoldDistance = 1.4f;
    [Tooltip("공격 준비를 시작하기 전에 공격 슬롯에 이만큼 가까워져야 합니다.")]
    [SerializeField] private float attackSlotEnterDistance = 0.55f;
    [Tooltip("플레이어 방향으로 어느 정도 깊게 파고들며 런지할지 정하는 거리입니다.")]
    [SerializeField] private float attackCommitDistance = 0.8f;
    [Tooltip("실제 공격 판정이 켜지는 동안 사용하는 히트박스 반경입니다.")]
    [SerializeField] private float attackHitRadius = 1.75f;
    [Tooltip("공격 후 Recover 단계에서 얼마나 물러설지 정하는 거리입니다.")]
    [SerializeField] private float attackRecoverDistance = 1.2f;
    [Tooltip("Lunge 단계에서 추가로 적용되는 이동 속도 배수입니다.")]
    [SerializeField] private float attackLungeSpeedMultiplier = 2.15f;
    [Tooltip("다음 공격을 시작하기 전까지 기다리는 쿨다운 범위입니다.")]
    [SerializeField] private Vector2 attackCooldownRange = new(1.75f, 3.2f);
    [Tooltip("멤버 인덱스마다 추가되는 공격 시작 지연입니다. 값이 클수록 여러 마리가 동시에 공격하기 어려워집니다.")]
    [SerializeField] private float attackStaggerPerMember = 0.22f;
    [Tooltip("공격 시작 타이밍이 너무 기계적으로 보이지 않도록 더하는 추가 랜덤 지연입니다.")]
    [SerializeField] private float attackStaggerJitter = 0.08f;
    [Tooltip("Lunge 전에 유지되는 Windup 단계 시간 범위입니다.")]
    [SerializeField] private Vector2 attackWindupRange = new(0.35f, 0.8f);
    [Tooltip("공격 후 Recover 단계가 유지되는 시간 범위입니다.")]
    [SerializeField] private Vector2 attackRecoverRange = new(0.45f, 0.9f);
    [Tooltip("공격 애니메이션 안에서 실제 타격 시점으로 취급하는 정규화 시간값입니다.")]
    [SerializeField, Range(0.05f, 0.95f)] private float attackHitNormalizedTime = 0.55f;

    [Header("Hit Reaction")]
    [Tooltip("켜면 죽지 않은 피격 시 잠깐 행동이 끊기고 피격 반응을 재생합니다.")]
    [SerializeField] private bool enableHitReaction = true;
    [Tooltip("치명타가 아닌 피격 후 행동이 끊기는 시간입니다.")]
    [SerializeField] private float hitReactionDuration = 0.45f;

    [Header("Debug")]
    [Tooltip("씬 뷰에 개별 멤버의 시야/전투 기즈모를 그립니다.")]
    [SerializeField] private bool drawDebugGizmos = true;
    [Tooltip("이 멤버의 시야 기즈모 색상입니다.")]
    [SerializeField] private Color sightGizmoColor = new(0.45f, 1f, 0.75f, 0.85f);
    [Tooltip("공격/전투 슬롯 기즈모 색상입니다.")]
    [SerializeField] private Color attackSlotColor = new(1f, 0.4f, 0.35f, 0.9f);

    private Vector3 _targetPosition;
    private Vector3 _looseFormationTarget;
    private Vector3 _smoothedFormationDestination;
    private Vector3 _attackApproachPoint;
    private Vector3 _attackLungePoint;
    private Vector3 _attackRecoverPoint;
    private Vector3 _lastKnownTargetPosition;
    private float _nextFormationRetargetAt;
    private float _nextAttackAt;
    private float _attackPhaseUntil;
    private float _hitReactUntil;
    private float _nextSightRefreshAt;
    private float _speedMultiplier = 1f;
    private float _roamMultiplier = 1f;
    private float _turnMultiplier = 1f;
    private float _deathDisableAt;
    private bool _baseOffsetCalibrated;
    private bool _attackDamageApplied;
    private bool _isDead;
    private bool _hasLooseFormationTarget;
    private bool _hasSmoothedFormationDestination;
    private bool _isReturningToFormation;
    private int _memberIndex;
    private int _memberCount;
    private int _isMovingHash;
    private int _attackTriggerHash;
    private int _idleHash;
    private int _chaseHash;
    private int _hitTriggerHash;
    private int _dieTriggerHash;
    private int _attackStateHash;
    private AttackPhase _attackPhase;
    private float _lastAnimatorPlaybackSpeed = float.NaN;

    public OctopusSwarmController Controller => controller;
    public bool IsDead => _isDead;
    public int MaxHealth => 1;
    public int CurrentHealth => currentHealth;
    public int AttackDamage => attackDamage;
    public int StableMemberId => stableMemberId;
    public Collider DamageCollider => damageCollider;
    public uint RenderingLayerMask => visualRenderer != null ? visualRenderer.renderingLayerMask : 1u;
    public int NavMeshAgentTypeId => agent != null ? agent.agentTypeID : 0;
    public int NavMeshAreaMask => agent != null ? agent.areaMask : NavMesh.AllAreas;
    public bool IsInAttackSequence => _attackPhase != AttackPhase.None;
    public bool IsTraversingDoor => doorAutoOpener != null && doorAutoOpener.IsTraversing;
    public string CurrentAttackPhaseLabel => _attackPhase == AttackPhase.None ? string.Empty : _attackPhase.ToString();
    public int MemberIndex => _memberIndex;
    private bool IsPatrolling => controller != null && !controller.IsAlerted && _attackPhase == AttackPhase.None;

    private void Awake()
    {
        controller ??= GetComponentInParent<OctopusSwarmController>();
        animator ??= GetComponent<Animator>();
        networkAnimator ??= GetComponent<NetworkAnimator>();
        networkTransform ??= GetComponent<NetworkTransform>();
        agent ??= GetComponent<NavMeshAgent>();
        damageCollider ??= GetComponent<CapsuleCollider>();
        visualRenderer ??= GetComponentInChildren<Renderer>();
        attackHitbox ??= GetComponentInChildren<OctopusAttackHitbox>(true);
        EnsurePresentationAudioSource();
        EnsureDoorTraversal();
        CacheAnimationHashes();
        InitializeVariation();
        maxHealth = 1;
        attackDamage = Mathf.Max(1, baseAttackDamage);
        currentHealth = 1;
        if (controller != null)
            controller.Register(this);
        ScheduleNextAttack(true);
        ConfigureAgent();
        TrySnapToNavMesh();
        CalibrateBaseOffset();
        SyncAttackHitboxShape();
        ApplySimulationAuthority();
    }

    private void OnEnable()
    {
        CancelInvoke(nameof(HideReplicatedCorpse));
        GetComponent<OctopusDeathHideTimer>()?.Cancel();
        _isDead = false;
        maxHealth = 1;
        attackDamage = Mathf.Max(1, baseAttackDamage);
        currentHealth = 1;
        _deathDisableAt = 0f;
        _attackPhase = AttackPhase.None;
        _attackDamageApplied = false;
        _hitReactUntil = 0f;
        _hasLooseFormationTarget = false;
        _hasSmoothedFormationDestination = false;
        _isReturningToFormation = false;
        AnimationEvent_DisableAttackHitbox();

        if (damageCollider != null)
            damageCollider.enabled = true;

        if (visualRenderer != null)
            visualRenderer.enabled = true;

        if (controller != null)
            controller.Register(this);

        ScheduleNextAttack(true);
        ApplySimulationAuthority();
    }

    private void OnDisable()
    {
        AnimationEvent_DisableAttackHitbox();

        if (controller != null)
            controller.Unregister(this);
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        patrolSpeedMultiplier = Mathf.Clamp(patrolSpeedMultiplier, 0.1f, 1f);
        turnSpeed = Mathf.Max(0f, turnSpeed);
        localRoamRadius = Mathf.Max(0.1f, localRoamRadius);
        maxDistanceFromCenter = Mathf.Max(localRoamRadius, maxDistanceFromCenter);
        targetReachedDistance = Mathf.Max(0.01f, targetReachedDistance);
        retargetIntervalRange.x = Mathf.Max(0.1f, retargetIntervalRange.x);
        retargetIntervalRange.y = Mathf.Max(retargetIntervalRange.x, retargetIntervalRange.y);
        formationDestinationSmoothing = Mathf.Max(0.1f, formationDestinationSmoothing);
        formationDestinationRefreshDistance = Mathf.Max(0.05f, formationDestinationRefreshDistance);
        targetFacingTurnMultiplier = Mathf.Max(0.5f, targetFacingTurnMultiplier);
        combatFaceDistance = Mathf.Max(0.5f, combatFaceDistance);
        slotReturnEnterDistance = Mathf.Max(0.2f, slotReturnEnterDistance);
        slotReturnExitDistance = Mathf.Clamp(slotReturnExitDistance, 0.05f, slotReturnEnterDistance - 0.05f);
        alertWanderStrength = Mathf.Max(0f, alertWanderStrength);
        sightRange = Mathf.Max(0.5f, sightRange);
        sightHalfAngle = Mathf.Clamp(sightHalfAngle, 10f, 180f);
        sightEyeHeight = Mathf.Max(0f, sightEyeHeight);
        sightTargetHeight = Mathf.Max(0f, sightTargetHeight);
        sightRefreshInterval = Mathf.Max(0.05f, sightRefreshInterval);
        separationRadius = Mathf.Max(0.01f, separationRadius);
        separationStrength = Mathf.Max(0f, separationStrength);
        speedVarianceRange.x = Mathf.Max(0.1f, speedVarianceRange.x);
        speedVarianceRange.y = Mathf.Max(speedVarianceRange.x, speedVarianceRange.y);
        roamVarianceRange.x = Mathf.Max(0.1f, roamVarianceRange.x);
        roamVarianceRange.y = Mathf.Max(roamVarianceRange.x, roamVarianceRange.y);
        turnVarianceRange.x = Mathf.Max(0.1f, turnVarianceRange.x);
        turnVarianceRange.y = Mathf.Max(turnVarianceRange.x, turnVarianceRange.y);
        modelGroundClearance = Mathf.Max(0f, modelGroundClearance);
        maxHealth = 1;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        deathDisableDelay = Mathf.Max(0.05f, deathDisableDelay);
        attackDamage = Mathf.Max(1, baseAttackDamage);
        attackEngageDistance = Mathf.Max(0.5f, attackEngageDistance);
        attackHoldDistance = Mathf.Max(0.25f, attackHoldDistance);
        attackSlotEnterDistance = Mathf.Max(0.1f, attackSlotEnterDistance);
        attackCommitDistance = Mathf.Clamp(attackCommitDistance, 0.1f, attackHoldDistance);
        attackHitRadius = Mathf.Max(0.1f, attackHitRadius);
        attackRecoverDistance = Mathf.Clamp(attackRecoverDistance, 0.1f, attackHoldDistance);
        attackLungeSpeedMultiplier = Mathf.Max(1f, attackLungeSpeedMultiplier);
        attackCooldownRange.x = Mathf.Max(0.1f, attackCooldownRange.x);
        attackCooldownRange.y = Mathf.Max(attackCooldownRange.x, attackCooldownRange.y);
        attackStaggerPerMember = Mathf.Max(0f, attackStaggerPerMember);
        attackStaggerJitter = Mathf.Max(0f, attackStaggerJitter);
        attackWindupRange.x = Mathf.Max(0.05f, attackWindupRange.x);
        attackWindupRange.y = Mathf.Max(attackWindupRange.x, attackWindupRange.y);
        attackRecoverRange.x = Mathf.Max(0.05f, attackRecoverRange.x);
        attackRecoverRange.y = Mathf.Max(attackRecoverRange.x, attackRecoverRange.y);
        attackHitNormalizedTime = Mathf.Clamp01(attackHitNormalizedTime);
        hitReactionDuration = Mathf.Max(0.05f, hitReactionDuration);
        walkAnimationReferenceSpeed = Mathf.Max(0.1f, walkAnimationReferenceSpeed);
        chaseAnimationReferenceSpeed = Mathf.Max(walkAnimationReferenceSpeed, chaseAnimationReferenceSpeed);
        minMovementAnimationSpeed = Mathf.Clamp(minMovementAnimationSpeed, 0.1f, 3f);
        maxMovementAnimationSpeed = Mathf.Max(minMovementAnimationSpeed, maxMovementAnimationSpeed);

        if (animator == null)
            animator = GetComponent<Animator>();

        if (networkAnimator == null)
            networkAnimator = GetComponent<NetworkAnimator>();

        if (networkTransform == null)
            networkTransform = GetComponent<NetworkTransform>();

        if (agent == null)
            agent = GetComponent<NavMeshAgent>();

        if (damageCollider == null)
            damageCollider = GetComponent<CapsuleCollider>();

        if (visualRenderer == null)
            visualRenderer = GetComponentInChildren<Renderer>();

        if (attackHitbox == null)
            attackHitbox = GetComponentInChildren<OctopusAttackHitbox>(true);

        if (doorAutoOpener == null)
            doorAutoOpener = GetComponent<DoorAutoOpener>();

        if (presentationAudioSource == null)
            presentationAudioSource = GetComponent<AudioSource>();

        CacheAnimationHashes();
        ConfigureAgent();
        SyncAttackHitboxShape();
    }

    private void Update()
    {
        if (controller == null)
            return;

        if (!HasServerAuthority)
            return;

        if (_isDead)
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh)
                agent.ResetPath();

            UpdateAnimation(false, false);
            AnimationEvent_DisableAttackHitbox();

            if (Time.time >= _deathDisableAt)
                DespawnOrDisableMember();

            return;
        }

        RefreshSighting();

        if (Time.time < _hitReactUntil)
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh)
                agent.ResetPath();

            UpdateAnimation(false, false);
            AnimationEvent_DisableAttackHitbox();
            return;
        }

        if (doorAutoOpener != null && doorAutoOpener.IsTraversing)
        {
            bool movingAcrossLink = doorAutoOpener.IsMovingAcrossLink;
            UpdateAnimation(movingAcrossLink, false);
            UpdateAnimatorPlaybackSpeed(movingAcrossLink, agent != null ? agent.desiredVelocity * Time.deltaTime : Vector3.zero);
            return;
        }

        Vector3 desired = ResolveDesiredPosition(out bool usesSoftFormation);
        desired = SmoothFormationDestination(desired, usesSoftFormation);
        _targetPosition = desired;

        Vector3 moveDelta = Vector3.zero;
        Vector3 animationMotion = Vector3.zero;
        if (agent != null && agent.enabled)
        {
            ConfigureAgent();

            if (TrySnapToNavMesh())
            {
                CalibrateBaseOffset();

                float refreshDistance = usesSoftFormation ? formationDestinationRefreshDistance : 0.1f;
                if ((_targetPosition - agent.destination).sqrMagnitude > refreshDistance * refreshDistance)
                    agent.SetDestination(_targetPosition);

                moveDelta = agent.velocity * Time.deltaTime;
                animationMotion = agent.desiredVelocity * Time.deltaTime;
            }
        }
        else
        {
            Vector3 nextPosition = Vector3.MoveTowards(transform.position, _targetPosition, moveSpeed * Time.deltaTime);
            moveDelta = nextPosition - transform.position;
            animationMotion = moveDelta;
            transform.position = nextPosition;
        }

        RotateTowardsDesiredFacing(moveDelta);

        bool isMoving = ShouldPlayMoveAnimation(moveDelta, animationMotion);
        bool attackAnimationActive = _attackPhase == AttackPhase.Windup || _attackPhase == AttackPhase.Lunge;
        if (attackAnimationActive)
            isMoving = false;

        UpdateAnimation(isMoving, attackAnimationActive);
        UpdateAnimatorPlaybackSpeed(isMoving, animationMotion);
    }

    public void SetController(OctopusSwarmController swarmController)
    {
        if (controller == swarmController)
            return;

        if (controller != null)
            controller.Unregister(this);

        controller = swarmController;

        if (controller != null)
            controller.Register(this);

        ConfigureAgent();
        EnsureDoorTraversal();
        TrySnapToNavMesh();
        CalibrateBaseOffset();
        _hasLooseFormationTarget = false;
        _hasSmoothedFormationDestination = false;
        _isReturningToFormation = false;
        ApplySimulationAuthority();
    }

    public void SetStableMemberId(int memberId)
    {
        stableMemberId = Mathf.Max(0, memberId);
    }

    public void ApplySimulationAuthority()
    {
        bool shouldSimulate = HasServerAuthority;

        if (agent != null && agent.enabled != shouldSimulate)
        {
            if (!shouldSimulate && agent.enabled && agent.isOnNavMesh)
                agent.ResetPath();

            agent.enabled = shouldSimulate;
        }

        if (!shouldSimulate)
            AnimationEvent_DisableAttackHitbox();
    }

    private bool HasServerAuthority
    {
        get
        {
            if (controller != null)
                return controller.HasServerAuthority;

            if (networkTransform != null && networkTransform.isSpawned)
                return networkTransform.isServer;

            NetworkManager networkManager = NetworkManager.main;
            return networkManager == null || networkManager.isServer;
        }
    }

    private void DespawnOrDisableMember()
    {
        if (networkTransform != null && networkTransform.isSpawned && networkTransform.isServer)
        {
            networkTransform.Despawn();
            return;
        }

        gameObject.SetActive(false);
    }

    internal void CompleteDeathPresentation()
    {
        if (_isDead)
            DespawnOrDisableMember();
    }

    public void SetMemberIndex(int memberIndex, int memberCount)
    {
        _memberIndex = memberIndex;
        _memberCount = Mathf.Max(1, memberCount);
        EnsureDoorTraversal();
    }

    private void CacheAnimationHashes()
    {
        _isMovingHash = string.IsNullOrWhiteSpace(isMovingParameter) ? 0 : Animator.StringToHash(isMovingParameter);
        _attackTriggerHash = string.IsNullOrWhiteSpace(isAttackingParameter) ? 0 : Animator.StringToHash(isAttackingParameter);
        _idleHash = string.IsNullOrWhiteSpace(idleParameter) ? 0 : Animator.StringToHash(idleParameter);
        _chaseHash = string.IsNullOrWhiteSpace(chaseParameter) ? 0 : Animator.StringToHash(chaseParameter);
        _hitTriggerHash = string.IsNullOrWhiteSpace(hitTriggerParameter) ? 0 : Animator.StringToHash(hitTriggerParameter);
        _dieTriggerHash = string.IsNullOrWhiteSpace(dieTriggerParameter) ? 0 : Animator.StringToHash(dieTriggerParameter);
        _attackStateHash = Animator.StringToHash(string.IsNullOrWhiteSpace(attackStateName) ? "Attack" : attackStateName);
    }

    private void RotateTowardsDesiredFacing(Vector3 moveDelta)
    {
        Vector3 desiredFacing = Vector3.zero;
        float turnMultiplier = _turnMultiplier;

        if (TryGetTargetFacingDirection(out Vector3 targetFacing))
        {
            desiredFacing = targetFacing;
            turnMultiplier *= targetFacingTurnMultiplier;
        }
        else
        {
            desiredFacing = new Vector3(moveDelta.x, 0f, moveDelta.z);
        }

        desiredFacing.y = 0f;
        if (desiredFacing.sqrMagnitude <= 0.000001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(desiredFacing.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * turnSpeed * turnMultiplier);
    }

    private bool TryGetTargetFacingDirection(out Vector3 facing)
    {
        facing = Vector3.zero;
        if (controller == null || !controller.IsAlerted)
            return false;

        Vector3 targetPosition = controller.AlertFocusPoint;
        if (controller.HasVisibleTarget && controller.CurrentTarget != null)
            targetPosition = controller.CurrentTarget.position;

        _lastKnownTargetPosition = targetPosition;

        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.0001f)
            return false;

        bool shouldFaceTarget = _attackPhase != AttackPhase.None
            || toTarget.sqrMagnitude <= combatFaceDistance * combatFaceDistance;
        if (!shouldFaceTarget)
            return false;

        facing = toTarget.normalized;
        return true;
    }

    private void RefreshSighting()
    {
        if (Time.time < _nextSightRefreshAt)
            return;

        _nextSightRefreshAt = Time.time + sightRefreshInterval;

        PlayerPawn bestPawn = null;
        float bestSqrDistance = float.MaxValue;

        for (int i = 0; i < PlayerPawn.All.Count; i++)
        {
            PlayerPawn candidate = PlayerPawn.All[i];
            if (!IsValidSightTarget(candidate))
                continue;

            Vector3 candidatePosition = candidate.transform.position;
            if (!IsWithinSightCone(candidatePosition))
                continue;

            if (!HasLineOfSight(candidate))
                continue;

            Vector3 planar = candidatePosition - transform.position;
            planar.y = 0f;
            float sqrDistance = planar.sqrMagnitude;
            if (sqrDistance >= bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            bestPawn = candidate;
        }

        if (bestPawn != null)
        {
            _lastKnownTargetPosition = bestPawn.transform.position;
            controller.ReportTargetSighted(this, bestPawn, bestPawn.transform.position);
        }
    }

    private bool IsValidSightTarget(PlayerPawn candidate)
    {
        if (candidate == null || !candidate.gameObject.activeInHierarchy)
            return false;

        Vector3 planar = candidate.transform.position - transform.position;
        planar.y = 0f;
        float range = sightRange * DungeonDarknessSense.DetectionMultiplierAt(candidate.transform.position);
        if (planar.sqrMagnitude > range * range)
            return false;

        PlayerVitals vitals = candidate.GetComponent<PlayerVitals>()
            ?? candidate.GetComponentInParent<PlayerVitals>()
            ?? candidate.GetComponentInChildren<PlayerVitals>();
        return vitals == null || !vitals.IsDead;
    }

    private bool IsWithinSightCone(Vector3 targetPosition)
    {
        Vector3 toTarget = targetPosition - GetSightOrigin();
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.0001f)
            return true;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f && controller != null)
            forward = controller.SwarmForward;

        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;

        forward.Normalize();
        toTarget.Normalize();
        return Vector3.Angle(forward, toTarget) <= sightHalfAngle;
    }

    private bool HasLineOfSight(PlayerPawn candidate)
    {
        Vector3 origin = GetSightOrigin();
        Vector3 targetPoint = candidate.transform.position + Vector3.up * sightTargetHeight;
        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;
        if (distance <= 0.001f)
            return true;

        int mask = sightOcclusionMask.value == 0 ? ~0 : sightOcclusionMask.value;
        if (!Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore))
            return true;

        return hit.collider != null && hit.collider.GetComponentInParent<PlayerPawn>() == candidate;
    }

    private Vector3 GetSightOrigin()
    {
        return transform.position + Vector3.up * sightEyeHeight;
    }

    private void PickLooseFormationTarget(Vector3 nominalSlot, float wanderStrength)
    {
        float wanderRadius = localRoamRadius * _roamMultiplier * Mathf.Max(0f, wanderStrength);
        Vector2 sample = Random.insideUnitCircle * wanderRadius;
        _looseFormationTarget = controller.ClampToSwarmPlane(nominalSlot + new Vector3(sample.x, 0f, sample.y));
        _nextFormationRetargetAt = Time.time + Random.Range(retargetIntervalRange.x, retargetIntervalRange.y);
        _hasLooseFormationTarget = true;
    }

    private Vector3 SmoothFormationDestination(Vector3 desired, bool usesSoftFormation)
    {
        if (!usesSoftFormation)
        {
            _hasSmoothedFormationDestination = false;
            return desired;
        }

        if (!_hasSmoothedFormationDestination)
        {
            _smoothedFormationDestination = transform.position;
            _hasSmoothedFormationDestination = true;
        }

        if (!_isReturningToFormation && (desired - transform.position).sqrMagnitude <= targetReachedDistance * targetReachedDistance)
        {
            _smoothedFormationDestination = desired;
            return desired;
        }

        float t = 1f - Mathf.Exp(-formationDestinationSmoothing * Time.deltaTime);
        _smoothedFormationDestination = Vector3.Lerp(_smoothedFormationDestination, desired, t);
        return _smoothedFormationDestination;
    }

    private Vector3 ComputeSeparationOffset()
    {
        if (controller == null)
            return Vector3.zero;

        Vector3 offset = Vector3.zero;
        float radiusSqr = separationRadius * separationRadius;

        var members = controller.Members;
        for (int i = 0; i < members.Count; i++)
        {
            OctopusSwarmMember other = members[i];
            if (other == null || other == this || other.IsDead)
                continue;

            Vector3 diff = transform.position - other.transform.position;
            diff.y = 0f;
            float sqrDistance = diff.sqrMagnitude;
            if (sqrDistance <= 0.0001f || sqrDistance > radiusSqr)
                continue;

            float strength = 1f - Mathf.Clamp01(Mathf.Sqrt(sqrDistance) / separationRadius);
            offset += diff.normalized * (strength * separationStrength);
        }

        return Vector3.ClampMagnitude(offset, separationRadius * 0.5f);
    }

    private void ConfigureAgent()
    {
        if (agent == null)
            return;

        float activeSpeedMultiplier = _attackPhase == AttackPhase.Lunge ? attackLungeSpeedMultiplier : 1f;
        if (IsPatrolling)
            activeSpeedMultiplier *= patrolSpeedMultiplier;
        agent.speed = moveSpeed * _speedMultiplier * activeSpeedMultiplier;
        agent.angularSpeed = Mathf.Max(120f, turnSpeed * _turnMultiplier * 60f);
        agent.acceleration = Mathf.Max(6f, moveSpeed * _speedMultiplier * activeSpeedMultiplier * 4f);
        agent.stoppingDistance = Mathf.Max(0.05f, targetReachedDistance * 0.85f);
        agent.radius = Mathf.Min(0.28f, separationRadius * 0.48f);
        agent.height = Mathf.Max(0.4f, transform.localScale.y * 3f);
        agent.updatePosition = true;
        agent.updateRotation = false;
        agent.autoBraking = false;
        agent.autoTraverseOffMeshLink = false;
    }

    private void EnsureDoorTraversal()
    {
        if (agent == null)
            agent = GetComponent<NavMeshAgent>();

        if (agent == null)
            return;

        if (doorAutoOpener == null)
            doorAutoOpener = GetComponent<DoorAutoOpener>();

        if (doorAutoOpener == null)
            doorAutoOpener = gameObject.AddComponent<DoorAutoOpener>();

        if (controller != null)
            doorAutoOpener.ConfigureGroup(controller.DoorTraversalGroup, true);
    }

    private void EnsurePresentationAudioSource()
    {
        if (presentationAudioSource == null)
            presentationAudioSource = GetComponent<AudioSource>();
    }

    public bool PlayPresentationOneShot(AudioClip clip, float eventVolume, float minDistance, float maxDistance)
    {
        if (clip == null)
            return false;

        EnsurePresentationAudioSource();
        if (presentationAudioSource == null)
            return false;

        ConfigurePresentationAudioSource(minDistance, maxDistance);
        presentationAudioSource.PlayOneShot(clip, Mathf.Clamp01(eventVolume));
        return true;
    }

    private void ConfigurePresentationAudioSource(float minDistance, float maxDistance)
    {
        if (presentationAudioSource == null)
            return;

        float clampedMin = Mathf.Max(0.01f, minDistance);
        presentationAudioSource.playOnAwake = false;
        presentationAudioSource.spatialBlend = 1f;
        presentationAudioSource.minDistance = clampedMin;
        presentationAudioSource.maxDistance = Mathf.Max(clampedMin, maxDistance);
        presentationAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
    }

    private bool TrySnapToNavMesh()
    {
        if (agent == null || !agent.enabled)
            return false;

        if (agent.isOnNavMesh)
            return true;

        if (!TrySampleNavMesh(transform.position, 3f, out NavMeshHit hit))
            return false;

        return agent.Warp(hit.position);
    }

    private void InitializeVariation()
    {
        _speedMultiplier = Random.Range(speedVarianceRange.x, speedVarianceRange.y);
        _roamMultiplier = Random.Range(roamVarianceRange.x, roamVarianceRange.y);
        _turnMultiplier = Random.Range(turnVarianceRange.x, turnVarianceRange.y);
        ScheduleNextAttack(true);
    }

    private void PlayHitReaction()
    {
        if (_isDead || !enableHitReaction)
            return;

        _attackPhase = AttackPhase.None;
        _attackDamageApplied = false;
        _hitReactUntil = Time.time + hitReactionDuration;
        _nextAttackAt = Mathf.Max(_nextAttackAt, _hitReactUntil + Random.Range(0.15f, 0.5f) + GetAttackStaggerOffset());

        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.ResetPath();

        if (animator != null && _hitTriggerHash != 0)
            SetAnimatorTrigger(_hitTriggerHash);
    }

    private void CalibrateBaseOffset()
    {
        if (_baseOffsetCalibrated || agent == null || !agent.enabled || !agent.isOnNavMesh || visualRenderer == null)
            return;

        if (!TrySampleNavMesh(transform.position, 2f, out NavMeshHit hit))
            return;

        float requiredLift = hit.position.y - visualRenderer.bounds.min.y + modelGroundClearance;
        if (requiredLift > 0f)
            agent.baseOffset += requiredLift;

        _baseOffsetCalibrated = true;
    }

    private Vector3 ResolveDesiredPosition(out bool usesSoftFormation)
    {
        usesSoftFormation = false;
        if (controller.IsAlerted)
        {
            Transform target = controller.CurrentTarget;
            if (enableAttack && controller.HasVisibleTarget && target != null)
            {
                bool shouldUseCombatSlot = _attackPhase != AttackPhase.None
                    || IsTargetWithinAttackEngageDistance(controller.AlertFocusPoint);
                if (shouldUseCombatSlot)
                {
                    Vector3 combatDesired = ResolveCombatDesiredPosition(target);
                    usesSoftFormation = _attackPhase == AttackPhase.None;
                    return combatDesired;
                }
            }

            Vector3 alertSlot = controller.HasVisibleTarget
                ? controller.GetEncircleSlotPosition(_memberIndex, _memberCount, controller.AlertFocusPoint)
                : controller.GetSearchSlotPosition(_memberIndex, _memberCount);
            usesSoftFormation = true;
            return ResolveLooseFormationPosition(alertSlot, alertWanderStrength);
        }

        Vector3 patrolSlot = controller.GetPatrolSlotPosition(_memberIndex, _memberCount);
        usesSoftFormation = true;
        return ResolveLooseFormationPosition(patrolSlot, controller.PatrolWanderStrength);
    }

    private Vector3 ResolveCombatDesiredPosition(Transform target)
    {
        Vector3 targetPosition = controller.ClampToSwarmPlane(target.position);
        _lastKnownTargetPosition = targetPosition;

        Vector3 nominalCloseCombatPoint = controller.GetCloseCombatSlotPosition(_memberIndex, _memberCount, targetPosition);
        Vector3 closeCombatPoint = _attackPhase == AttackPhase.None
            ? ResolveLooseFormationPosition(nominalCloseCombatPoint, alertWanderStrength * 0.35f)
            : ClampWithinSwarm(nominalCloseCombatPoint + ComputeSeparationOffset());

        Vector3 outward = closeCombatPoint - targetPosition;
        outward.y = 0f;
        if (outward.sqrMagnitude <= 0.001f)
            outward = GetPlanarDirectionAwayFrom(targetPosition);
        else
            outward.Normalize();

        switch (_attackPhase)
        {
            case AttackPhase.None:
                if (CanStartAttack(targetPosition, closeCombatPoint))
                    BeginAttack(targetPosition, outward, closeCombatPoint);

                return ClampWithinSwarm(closeCombatPoint);

            case AttackPhase.Windup:
                if (HasReachedAttackAnimationHitWindow())
                    BeginLunge(targetPosition, outward);

                return ClampWithinSwarm(_attackApproachPoint);

            case AttackPhase.Lunge:
                if (HasAttackAnimationFinished())
                    BeginRecover(targetPosition, closeCombatPoint);

                return ClampWithinSwarm(_attackLungePoint);

            case AttackPhase.Recover:
                if (Time.time >= _attackPhaseUntil)
                    EndAttack();

                return ClampWithinSwarm(_attackRecoverPoint);

            default:
                return ClampWithinSwarm(closeCombatPoint);
        }
    }

    private bool CanStartAttack(Vector3 targetPosition, Vector3 closeCombatPoint)
    {
        if (Time.time < _nextAttackAt)
            return false;

        if (!IsTargetWithinAttackEngageDistance(targetPosition))
            return false;

        Vector3 radial = transform.position - targetPosition;
        radial.y = 0f;
        float distance = radial.magnitude;
        if (distance > attackHoldDistance + 0.85f)
            return false;

        Vector3 toHold = closeCombatPoint - transform.position;
        toHold.y = 0f;
        if (toHold.sqrMagnitude > attackSlotEnterDistance * attackSlotEnterDistance)
            return false;

        return true;
    }

    private void BeginAttack(Vector3 targetPosition, Vector3 outward, Vector3 holdPoint)
    {
        _attackPhase = AttackPhase.Windup;
        _attackApproachPoint = holdPoint;
        _attackLungePoint = SampleNavMeshDestination(targetPosition + outward * attackCommitDistance, attackCommitDistance + 1f);
        _attackRecoverPoint = holdPoint;
        _attackDamageApplied = false;
        _attackPhaseUntil = Time.time + Random.Range(attackWindupRange.x, attackWindupRange.y) + 1.5f;
        _lastKnownTargetPosition = targetPosition;

        if (animator != null && _attackTriggerHash != 0)
        {
            ResetAnimatorTrigger(_attackTriggerHash);
            SetAnimatorTrigger(_attackTriggerHash);
        }

        AnimationEvent_DisableAttackHitbox();
        controller?.ReportMemberAttackWindup(this, transform.position, transform.rotation);
    }

    private Vector3 ResolveLooseFormationPosition(Vector3 nominalSlot, float wanderStrength)
    {
        nominalSlot = controller.ClampToSwarmPlane(nominalSlot);
        Vector3 toSlot = nominalSlot - transform.position;
        toSlot.y = 0f;
        float distanceToSlot = toSlot.magnitude;

        if (_isReturningToFormation)
        {
            if (distanceToSlot <= slotReturnExitDistance)
            {
                _isReturningToFormation = false;
                _hasLooseFormationTarget = false;
            }
        }
        else if (distanceToSlot >= slotReturnEnterDistance)
        {
            _isReturningToFormation = true;
        }

        Vector3 desired;
        if (_isReturningToFormation)
        {
            desired = nominalSlot;
        }
        else
        {
            if (!_hasLooseFormationTarget || Time.time >= _nextFormationRetargetAt)
                PickLooseFormationTarget(nominalSlot, wanderStrength);

            desired = _looseFormationTarget;
        }

        desired += ComputeSeparationOffset();
        return ClampWithinSwarm(desired);
    }

    private void BeginLunge(Vector3 targetPosition, Vector3 outward)
    {
        _attackPhase = AttackPhase.Lunge;
        _attackLungePoint = SampleNavMeshDestination(targetPosition + outward * attackCommitDistance, attackCommitDistance + 1f);
        _attackPhaseUntil = Time.time + 1.2f;
        _lastKnownTargetPosition = targetPosition;

        AnimationEvent_EnableAttackHitbox();
        controller?.ReportMemberLunge(this, transform.position, transform.rotation);
    }

    private void BeginRecover(Vector3 targetPosition, Vector3 recoverPoint)
    {
        if (_attackPhase == AttackPhase.Recover)
            return;

        _attackPhase = AttackPhase.Recover;
        _attackRecoverPoint = SampleNavMeshDestination(recoverPoint, attackRecoverDistance + 0.5f);
        _attackPhaseUntil = Time.time + Random.Range(attackRecoverRange.x, attackRecoverRange.y);
        _lastKnownTargetPosition = targetPosition;

        AnimationEvent_DisableAttackHitbox();
    }

    private void EndAttack()
    {
        _attackPhase = AttackPhase.None;
        _attackDamageApplied = false;
        _attackPhaseUntil = 0f;
        ScheduleNextAttack(false);

        AnimationEvent_DisableAttackHitbox();
    }

    private void SyncAttackHitboxShape()
    {
        if (attackHitbox != null)
            attackHitbox.ConfigureRadius(attackHitRadius);
    }

    public bool TryApplyHitboxDamage(Collider other, Vector3 hitPoint, out int targetId)
    {
        targetId = 0;
        if (_isDead || _attackPhase != AttackPhase.Lunge || _attackDamageApplied || other == null)
            return false;

        PlayerVitals vitals = other.GetComponent<PlayerVitals>()
            ?? other.GetComponentInParent<PlayerVitals>()
            ?? other.GetComponentInChildren<PlayerVitals>();
        if (vitals == null)
            return false;

        Transform targetRoot = vitals.transform.root;
        targetId = targetRoot != null ? targetRoot.GetInstanceID() : vitals.GetInstanceID();
        return ApplyDamageToTarget(vitals, hitPoint);
    }

    private bool ApplyDamageToTarget(PlayerVitals vitals, Vector3 hitPoint)
    {
        if (vitals == null || vitals.IsDead)
            return false;

        if (!HasServerAuthority)
            return false;

        vitals.ApplyDamage(attackDamage, null);
        _attackDamageApplied = true;
        controller?.ReportMemberHit(this, ResolvePresentationPoint(hitPoint), transform.rotation);
        return true;
    }

    public void AnimationEvent_EnableAttackHitbox()
    {
        if (_attackPhase != AttackPhase.Lunge)
            return;

        if (attackHitbox != null)
            attackHitbox.Activate();
    }

    public void AnimationEvent_DisableAttackHitbox()
    {
        if (attackHitbox != null)
            attackHitbox.Deactivate();
    }

    public void TakeDamage(int amount)
    {
        TakeDamage(DamageRequest.Bullet(amount, transform.position));
    }

    public void TakeDamage(int amount, Vector3 hitPoint)
    {
        TakeDamage(DamageRequest.Bullet(amount, hitPoint));
    }

    public void TakeDamage(DamageRequest request)
    {
        ApplyDamage(request);
    }

    public void ApplyReplicatedDeath(bool explosive)
    {
        if (_isDead)
            return;

        _isDead = true;
        currentHealth = 0;
        _attackPhase = AttackPhase.None;
        _attackDamageApplied = false;
        _hitReactUntil = 0f;
        _nextAttackAt = float.PositiveInfinity;
        AnimationEvent_DisableAttackHitbox();

        if (agent != null)
        {
            if (agent.enabled && agent.isOnNavMesh)
                agent.ResetPath();
            agent.enabled = false;
        }

        if (damageCollider != null)
            damageCollider.enabled = false;

        if (explosive)
        {
            if (animator != null)
                animator.enabled = false;

            if (visualRenderer != null)
                visualRenderer.enabled = false;
        }
        else if (animator != null && _dieTriggerHash != 0)
        {
            SetAnimatorTrigger(_dieTriggerHash);
        }

        CancelInvoke(nameof(HideReplicatedCorpse));
        Invoke(nameof(HideReplicatedCorpse), explosive ? 0.05f : deathDisableDelay);
    }

    private void HideReplicatedCorpse()
    {
        if (!HasServerAuthority && visualRenderer != null)
            visualRenderer.enabled = false;
    }

    private void ApplyDamage(DamageRequest request)
    {
        if (!HasServerAuthority || request.Amount <= 0 || _isDead)
            return;

        Vector3 hitPoint = request.HasHitPoint ? request.HitPoint : transform.position;
        currentHealth = Mathf.Max(0, currentHealth - request.Amount);
        if (currentHealth <= 0)
        {
            BeginDeath(request, hitPoint);
            return;
        }

        Vector3 sprayDirection = request.HitDirection.sqrMagnitude > 0.0001f
            ? -request.HitDirection.normalized
            : Vector3.up;
        Quaternion bloodRotation = Quaternion.FromToRotation(Vector3.up, sprayDirection);
        controller?.ReportMemberDamaged(this, ResolvePresentationPoint(hitPoint), bloodRotation);
        PlayHitReaction();
    }

    private void BeginDeath(DamageRequest request, Vector3 hitPoint)
    {
        if (_isDead)
            return;

        _isDead = true;
        currentHealth = 0;
        _attackPhase = AttackPhase.None;
        _attackDamageApplied = false;
        _hitReactUntil = 0f;
        _nextAttackAt = float.PositiveInfinity;
        bool explosive = request.DamageType == DamageType.Explosive;
        float disableDelay = explosive ? 0.05f : deathDisableDelay;
        _deathDisableAt = Time.time + disableDelay;
        OctopusDeathHideTimer.Schedule(this, disableDelay);

        controller?.ReportMemberDeath(this, ResolvePresentationPoint(hitPoint), transform.rotation, explosive);
        controller?.NotifyMemberDefeated(this, explosive);
        AnimationEvent_DisableAttackHitbox();

        if (damageCollider != null)
            damageCollider.enabled = false;

        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.ResetPath();

        if (explosive)
        {
            if (animator != null)
                animator.enabled = false;

            if (visualRenderer != null)
                visualRenderer.enabled = false;
        }
        else if (animator != null)
        {
            if (_isMovingHash != 0)
                animator.SetBool(_isMovingHash, false);

            if (_attackTriggerHash != 0)
                ResetAnimatorTrigger(_attackTriggerHash);

            if (_idleHash != 0)
                animator.SetBool(_idleHash, false);

            if (_chaseHash != 0)
                animator.SetBool(_chaseHash, false);

            if (_dieTriggerHash != 0)
                SetAnimatorTrigger(_dieTriggerHash);
        }
    }

    private Vector3 ResolvePresentationPoint(Vector3 hitPoint)
    {
        if (float.IsNaN(hitPoint.x) || float.IsNaN(hitPoint.y) || float.IsNaN(hitPoint.z))
            return transform.position;

        if (float.IsInfinity(hitPoint.x) || float.IsInfinity(hitPoint.y) || float.IsInfinity(hitPoint.z))
            return transform.position;

        return hitPoint;
    }

    private bool IsTargetWithinAttackEngageDistance(Vector3 targetPosition)
    {
        Vector3 delta = targetPosition - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= attackEngageDistance * attackEngageDistance;
    }

    private Vector3 GetPlanarDirectionAwayFrom(Vector3 targetPosition)
    {
        Vector3 outward = transform.position - targetPosition;
        outward.y = 0f;
        if (outward.sqrMagnitude <= 0.001f)
        {
            outward = transform.right;
            outward.y = 0f;
        }

        if (outward.sqrMagnitude <= 0.001f && controller != null)
        {
            outward = controller.SwarmForward.sqrMagnitude > 0.001f ? -controller.SwarmForward : Vector3.right;
            outward.y = 0f;
        }

        return outward.normalized;
    }

    private Vector3 ClampWithinSwarm(Vector3 desired)
    {
        desired = controller.ClampToSwarmPlane(desired);
        Vector3 toDesired = desired - controller.CurrentCenter;
        Vector3 planar = new Vector3(toDesired.x, 0f, toDesired.z);
        float allowedDistance = maxDistanceFromCenter;
        if (controller.IsAlerted && controller.HasVisibleTarget)
            allowedDistance = Mathf.Max(allowedDistance, controller.ChaseStopDistance + attackHoldDistance + 1.25f);

        if (planar.magnitude > allowedDistance)
            desired = controller.CurrentCenter + planar.normalized * allowedDistance;

        desired = controller.ClampToSwarmPlane(desired);
        return SampleNavMeshDestination(desired, allowedDistance + 1f);
    }

    private Vector3 SampleNavMeshDestination(Vector3 desired, float radius)
    {
        desired = controller.ClampToSwarmPlane(desired);
        if (TrySampleNavMesh(desired, Mathf.Max(1f, radius), out NavMeshHit hit))
            return controller.ClampToSwarmPlane(hit.position);

        return desired;
    }

    private bool TrySampleNavMesh(Vector3 position, float maxDistance, out NavMeshHit hit)
    {
        if (agent == null)
            return NavMesh.SamplePosition(position, out hit, maxDistance, NavMesh.AllAreas);

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };
        return NavMesh.SamplePosition(position, out hit, maxDistance, filter);
    }

    private bool HasReachedAttackAnimationHitWindow()
    {
        if (animator == null || !animator.isActiveAndEnabled)
            return Time.time >= _attackPhaseUntil;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != _attackStateHash)
            return Time.time >= _attackPhaseUntil;

        return state.normalizedTime >= attackHitNormalizedTime;
    }

    private bool HasAttackAnimationFinished()
    {
        if (animator == null || !animator.isActiveAndEnabled)
            return Time.time >= _attackPhaseUntil;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != _attackStateHash)
            return _attackDamageApplied || Time.time >= _attackPhaseUntil;

        return state.normalizedTime >= 0.98f;
    }

    private void UpdateAnimatorPlaybackSpeed(bool isMoving, Vector3 animationMotion)
    {
        if (!driveAnimation || animator == null || animator.runtimeAnimatorController == null)
            return;

        if (_attackPhase != AttackPhase.None || Time.time < _hitReactUntil || _isDead || !isMoving)
        {
            SetAnimatorPlaybackSpeed(1f);
            return;
        }

        Vector3 planarMotion = new(animationMotion.x, 0f, animationMotion.z);
        float actualSpeed = planarMotion.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        bool isChasing = controller != null && controller.IsAlerted;
        float referenceSpeed = isChasing ? chaseAnimationReferenceSpeed : walkAnimationReferenceSpeed;
        float playbackSpeed = Mathf.Clamp(actualSpeed / Mathf.Max(0.1f, referenceSpeed), minMovementAnimationSpeed, maxMovementAnimationSpeed);
        SetAnimatorPlaybackSpeed(playbackSpeed);
    }

    private void SetAnimatorPlaybackSpeed(float playbackSpeed)
    {
        float clampedSpeed = Mathf.Max(0f, playbackSpeed);
        if (Mathf.Approximately(_lastAnimatorPlaybackSpeed, clampedSpeed))
            return;

        _lastAnimatorPlaybackSpeed = clampedSpeed;
        if (networkAnimator != null && networkAnimator.isSpawned && HasServerAuthority)
        {
            networkAnimator.speed = clampedSpeed;
            return;
        }

        if (animator != null)
            animator.speed = clampedSpeed;
    }

    private void SetAnimatorTrigger(int parameterHash)
    {
        if (parameterHash == 0 || animator == null)
            return;

        if (networkAnimator != null && networkAnimator.isSpawned && HasServerAuthority)
        {
            networkAnimator.SetTrigger(parameterHash);
            return;
        }

        animator.SetTrigger(parameterHash);
    }

    private void ResetAnimatorTrigger(int parameterHash)
    {
        if (parameterHash == 0 || animator == null)
            return;

        if (networkAnimator != null && networkAnimator.isSpawned && HasServerAuthority)
        {
            networkAnimator.ResetTrigger(parameterHash);
            return;
        }

        animator.ResetTrigger(parameterHash);
    }

    private void ScheduleNextAttack(bool initial)
    {
        float minCooldown = initial ? attackCooldownRange.x * 0.35f : attackCooldownRange.x;
        float baseCooldown = Random.Range(minCooldown, attackCooldownRange.y);
        _nextAttackAt = Time.time + baseCooldown + GetAttackStaggerOffset();
    }

    private float GetAttackStaggerOffset()
    {
        float memberOffset = attackStaggerPerMember * Mathf.Max(0, _memberIndex);
        float jitterOffset = Random.Range(0f, attackStaggerJitter);
        return memberOffset + jitterOffset;
    }

    private bool ShouldPlayMoveAnimation(Vector3 moveDelta, Vector3 animationMotion)
    {
        Vector3 planarMove = new(moveDelta.x, 0f, moveDelta.z);
        if (planarMove.sqrMagnitude > 0.00001f)
            return true;

        Vector3 planarAnimationMotion = new(animationMotion.x, 0f, animationMotion.z);
        if (planarAnimationMotion.sqrMagnitude > 0.00001f)
            return true;

        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return false;

        if (agent.pathPending)
            return true;

        if (agent.hasPath && agent.remainingDistance > Mathf.Max(targetReachedDistance, agent.stoppingDistance + 0.05f))
            return true;

        return false;
    }

    private void UpdateAnimation(bool isMoving, bool isAttacking)
    {
        if (!driveAnimation || animator == null || animator.runtimeAnimatorController == null)
            return;

        bool isChasing = controller != null && controller.IsAlerted && isMoving && !isAttacking;
        bool isWalking = isMoving && !isAttacking && !isChasing;

        if (_isMovingHash != 0)
            animator.SetBool(_isMovingHash, isWalking);

        if (_idleHash != 0)
            animator.SetBool(_idleHash, !isMoving && !isAttacking);

        if (_chaseHash != 0)
            animator.SetBool(_chaseHash, isChasing);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
            return;

        Vector3 origin = GetSightOrigin();
        Vector3 forward = Application.isPlaying ? transform.forward : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;

        Quaternion leftRotation = Quaternion.AngleAxis(-sightHalfAngle, Vector3.up);
        Quaternion rightRotation = Quaternion.AngleAxis(sightHalfAngle, Vector3.up);

        Gizmos.color = sightGizmoColor;
        Gizmos.DrawWireSphere(transform.position, separationRadius);
        Gizmos.DrawLine(origin, origin + leftRotation * forward.normalized * sightRange);
        Gizmos.DrawLine(origin, origin + rightRotation * forward.normalized * sightRange);

        if (controller == null)
            return;

        Gizmos.color = _attackPhase != AttackPhase.None ? attackSlotColor : Color.cyan;
        Gizmos.DrawWireSphere(_targetPosition == Vector3.zero ? transform.position : _targetPosition, 0.16f);
        Gizmos.DrawLine(transform.position, _targetPosition);

        if (Application.isPlaying && controller.IsAlerted)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, controller.AlertFocusPoint);
        }
    }
}
