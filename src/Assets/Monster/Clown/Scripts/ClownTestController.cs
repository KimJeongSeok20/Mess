using System;
using System.Collections.Generic;
using System.Linq;
using DunGen;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class ClownTestController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MonsterHealth health;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private RangeDetector rangeDetector;
    [SerializeField] private LineOfSightDetector lineOfSightDetector;
    [SerializeField] private ClownDeathSequence deathSequence;
    [SerializeField] private Animator animator;
    [SerializeField] private BehaviorGraphAgent behaviorGraphAgent;
    [SerializeField] private Transform throwOrigin;
    [SerializeField] private GameObject giftVisualPrefab;
    [SerializeField] private GameObject heldGiftVisualPrefab;
    [SerializeField] private Item[] randomItemDrops;

    [Header("Behavior")]
    [SerializeField] private float reacquireInterval = 0.2f;
    [SerializeField] private float chaseDistance = 16f;
    [SerializeField] private float throwDistance = 10f;
    [SerializeField] private float throwCooldown = 2.75f;
    [SerializeField] private float throwSpeed = 11f;
    [SerializeField] private float upwardArc = 2.2f;
    [SerializeField] private float fallbackMoveSpeed = 3f;
    [SerializeField] private float throwReleaseNormalizedTime = 24f / 66f;
    [SerializeField] private float disappearTeleportNormalizedTime = 0.15f;
    [SerializeField] private float teleportNavMeshSampleRadius = 1.5f;
    [SerializeField] private int teleportRegistryTries = 8;
    [SerializeField] private int teleportTileTries = 16;
    [SerializeField] private float teleportMinDistance = 6f;
    [SerializeField] private bool teleportDebugLog;
    [SerializeField] private string moveSpeedParameter = "MoveSpeed";
    [SerializeField] private string throwTrigger = "Throw";
    [SerializeField] private float throwSpawnForwardOffset = 0.75f;
    [SerializeField] private float throwSpawnUpOffset = 0.08f;
    [SerializeField] private Vector3 heldGiftLocalPosition = new(0.04f, -0.04f, 0.08f);
    [SerializeField] private Vector3 heldGiftLocalEulerAngles = new(0f, 0f, 90f);
    [SerializeField] private Vector3 heldGiftLocalScale = Vector3.one;

    private GameObject _target;
    private GameObject _heldGiftInstance;
    private float _nextScanAt;
    private float _nextThrowAt;
    private Vector3 _desiredPosition;
    private float _desiredYaw;
    private int _throwTriggerHash;
    private int _moveSpeedHash;
    private int _throwStateHash;
    private int _cheerStateHash;
    private int _disappearStateHash;
    private bool _throwSequenceActive;
    private bool _throwReleased;
    private bool _teleportTriggered;
    private Vector3 _pendingThrowTargetPosition;

    private void Awake()
    {
        health ??= GetComponent<MonsterHealth>();
        agent ??= GetComponent<NavMeshAgent>();
        rangeDetector ??= GetComponent<RangeDetector>();
        lineOfSightDetector ??= GetComponent<LineOfSightDetector>();
        deathSequence ??= GetComponent<ClownDeathSequence>();
        animator ??= GetComponentInChildren<Animator>(true);
        behaviorGraphAgent ??= GetComponent<BehaviorGraphAgent>();

        if (throwOrigin == null)
            throwOrigin = FindThrowOrigin();

        if (heldGiftVisualPrefab == null)
            heldGiftVisualPrefab = giftVisualPrefab;

        if (!string.IsNullOrEmpty(throwTrigger))
            _throwTriggerHash = Animator.StringToHash(throwTrigger);

        if (!string.IsNullOrEmpty(moveSpeedParameter))
            _moveSpeedHash = Animator.StringToHash(moveSpeedParameter);

        _throwStateHash = Animator.StringToHash("Throw");
        _cheerStateHash = Animator.StringToHash("Cheer");
        _disappearStateHash = Animator.StringToHash("Disappear");

        EnsureHeldGiftVisual();
        SetHeldGiftVisible(true);
        CaptureDesiredTransform();

        if (behaviorGraphAgent != null)
            behaviorGraphAgent.enabled = false;
    }

    private void LateUpdate()
    {
        if (health == null || IsMonsterDead())
            return;

        transform.position = _desiredPosition;
        transform.rotation = Quaternion.Euler(0f, _desiredYaw, 0f);

        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.nextPosition = _desiredPosition;
    }

    private void Update()
    {
        if (IsBehaviorGraphDriving())
            return;

        TickBehavior();
    }

    public void TickBehavior()
    {
        UpdateAnimationSequence();

        if (health != null)
        {
            if (IsMonsterDead())
            {
                StopMovement();
                UpdateAnimatorMoveSpeed(0f);
                SetHeldGiftVisible(false);
                enabled = false;
                return;
            }

            if (!health.isServer)
                return;
        }

        if (_throwSequenceActive)
        {
            StopMovement();
            UpdateAnimatorMoveSpeed(0f);

            if (!_throwReleased)
                FaceTarget(_pendingThrowTargetPosition);

            CaptureDesiredTransform();

            return;
        }

        if (Time.time >= _nextScanAt)
        {
            _nextScanAt = Time.time + Mathf.Max(0.05f, reacquireInterval);
            _target = AcquireTarget();
        }

        if (_target == null)
        {
            StopMovement();
            UpdateAnimatorMoveSpeed(0f);
            CaptureDesiredTransform();
            return;
        }

        Vector3 targetPosition = _target.transform.position;
        float sqrDistance = (targetPosition - transform.position).sqrMagnitude;

        if (HasLineOfSight(_target) && sqrDistance <= throwDistance * throwDistance)
        {
            StopMovement();
            UpdateAnimatorMoveSpeed(0f);
            FaceTarget(targetPosition);

            if (Time.time >= _nextThrowAt)
                BeginThrowSequence(targetPosition);

            CaptureDesiredTransform();

            return;
        }

        if (sqrDistance <= chaseDistance * chaseDistance)
        {
            MoveTowards(targetPosition);
            UpdateAnimatorMoveSpeed(1f);
            CaptureDesiredTransform();
            return;
        }

        _target = null;
        StopMovement();
        UpdateAnimatorMoveSpeed(0f);
        CaptureDesiredTransform();
    }

    public void StopBehavior()
    {
        StopMovement();
        UpdateAnimatorMoveSpeed(0f);
        _target = null;
        CaptureDesiredTransform();
    }

    private GameObject AcquireTarget()
    {
        GameObject candidate = rangeDetector != null ? InvokeRangeDetector() : null;
        if (candidate != null && HasLineOfSight(candidate))
            return candidate;

        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        float best = float.MaxValue;
        GameObject bestTarget = null;

        for (int i = 0; i < players.Length; i++)
        {
            GameObject player = players[i];
            if (player == null)
                continue;

            float sqrDistance = (player.transform.position - transform.position).sqrMagnitude;
            if (sqrDistance > chaseDistance * chaseDistance || sqrDistance >= best)
                continue;

            if (!HasLineOfSight(player))
                continue;

            best = sqrDistance;
            bestTarget = player;
        }

        return bestTarget;
    }

    private bool HasLineOfSight(GameObject candidate)
    {
        if (candidate == null)
            return false;

        if (lineOfSightDetector == null)
            return true;

        if (InvokeLineOfSight(candidate) != null)
            return true;

        Vector3 origin = transform.position + Vector3.up * 1.5f;
        Vector3 targetPoint = candidate.transform.position + Vector3.up * 1.0f;
        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;
        if (distance <= 0.001f)
            return true;

        if (!Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
            return true;

        return hit.collider != null && hit.collider.GetComponentInParent<global::PlayerDeath>() != null;
    }

    private void MoveTowards(Vector3 targetPosition)
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.SetDestination(targetPosition);
            return;
        }

        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        direction.Normalize();
        transform.position += direction * fallbackMoveSpeed * Time.deltaTime;
        transform.forward = Vector3.Slerp(transform.forward, direction, Time.deltaTime * 8f);
    }

    private void StopMovement()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    private void FaceTarget(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
    }

    private void CaptureDesiredTransform()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            _desiredPosition = agent.nextPosition;
        else
            _desiredPosition = transform.position;

        _desiredYaw = transform.eulerAngles.y;
    }

    private void ThrowGift(Vector3 targetPosition)
    {
        if (giftVisualPrefab == null)
            return;

        Vector3 origin = throwOrigin != null ? throwOrigin.position : transform.position + Vector3.up * 1.4f;
        Vector3 aimPoint = targetPosition + Vector3.up * 0.8f;
        Vector3 velocity = CalculateThrowVelocity(origin, aimPoint);
        Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 launchDirection = flatVelocity.sqrMagnitude > 0.0001f ? flatVelocity.normalized : transform.forward;
        Vector3 spawnPosition = origin + launchDirection * throwSpawnForwardOffset + Vector3.up * throwSpawnUpOffset;

        GameObject giftObject = Instantiate(giftVisualPrefab, spawnPosition, Quaternion.LookRotation(launchDirection, Vector3.up));
        giftObject.name = "ClownGift_Runtime";

        Rigidbody rigidbody = giftObject.GetComponent<Rigidbody>();
        if (rigidbody == null)
            rigidbody = giftObject.AddComponent<Rigidbody>();

        rigidbody.useGravity = true;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.mass = 0.35f;

        ClownGift gift = giftObject.GetComponent<ClownGift>();
        if (gift == null)
            gift = giftObject.AddComponent<ClownGift>();

        gift.InitializeServer(velocity, deathSequence, randomItemDrops, GetComponentsInChildren<Collider>(true));
        _nextThrowAt = Time.time + Mathf.Max(0.25f, throwCooldown);
    }

    private void BeginThrowSequence(Vector3 targetPosition)
    {
        _pendingThrowTargetPosition = targetPosition;

        if (animator == null || animator.runtimeAnimatorController == null)
        {
            ThrowGift(_pendingThrowTargetPosition);
            return;
        }

        _throwSequenceActive = true;
        _throwReleased = false;
        _teleportTriggered = false;
        SetHeldGiftVisible(true);

        if (_throwTriggerHash != 0)
            animator.SetTrigger(_throwTriggerHash);
    }

    private void UpdateAnimationSequence()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        int stateHash = state.shortNameHash;

        if (_throwSequenceActive && !_throwReleased && stateHash == _throwStateHash && state.normalizedTime >= throwReleaseNormalizedTime)
        {
            ThrowGift(_pendingThrowTargetPosition);
            _throwReleased = true;
            SetHeldGiftVisible(false);
        }

        if (_throwSequenceActive && !_teleportTriggered && stateHash == _disappearStateHash && state.normalizedTime >= disappearTeleportNormalizedTime)
        {
            TeleportToRandomPoint();
            _teleportTriggered = true;
        }

        if (_throwSequenceActive && _throwReleased && !IsActionState(stateHash))
        {
            if (!_teleportTriggered)
            {
                TeleportToRandomPoint();
                _teleportTriggered = true;
            }

            _throwSequenceActive = false;
            _throwReleased = false;
            _teleportTriggered = false;
            SetHeldGiftVisible(true);
        }
    }

    private bool IsActionState(int stateHash)
    {
        return stateHash == _throwStateHash || stateHash == _cheerStateHash || stateHash == _disappearStateHash;
    }

    private void UpdateAnimatorMoveSpeed(float value)
    {
        if (animator == null || animator.runtimeAnimatorController == null || _moveSpeedHash == 0)
            return;

        animator.SetFloat(_moveSpeedHash, value);
    }

    private void EnsureHeldGiftVisual()
    {
        if (_heldGiftInstance != null || heldGiftVisualPrefab == null || throwOrigin == null)
            return;

        _heldGiftInstance = Instantiate(heldGiftVisualPrefab, throwOrigin);
        _heldGiftInstance.name = "ClownHeldGift";
        _heldGiftInstance.transform.localPosition = heldGiftLocalPosition;
        _heldGiftInstance.transform.localEulerAngles = heldGiftLocalEulerAngles;
        _heldGiftInstance.transform.localScale = heldGiftLocalScale;

        Collider[] colliders = _heldGiftInstance.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        Rigidbody[] rigidbodies = _heldGiftInstance.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
            Destroy(rigidbodies[i]);

        ClownGift[] gifts = _heldGiftInstance.GetComponentsInChildren<ClownGift>(true);
        for (int i = 0; i < gifts.Length; i++)
            Destroy(gifts[i]);
    }

    private void SetHeldGiftVisible(bool visible)
    {
        if (_heldGiftInstance == null)
            return;

        Renderer[] renderers = _heldGiftInstance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = visible;
    }

    private void TeleportToRandomPoint()
    {
        if (!TryPickTeleportDestination(out Vector3 destination, out string source, out string reason))
        {
            LogTeleport($"FAILED reason={reason}");
            return;
        }

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.ResetPath();
            bool warped = agent.Warp(destination);
            LogTeleport($"{(warped ? "WARP" : "WARP_FAILED")} source={source} destination={destination}");
            if (!warped)
                transform.position = destination;
        }
        else
        {
            transform.position = destination;
            LogTeleport($"TRANSFORM source={source} destination={destination}");
        }

        _target = null;
        _desiredPosition = destination;
    }

    private bool TryPickTeleportDestination(out Vector3 destination, out string source, out string reason)
    {
        destination = default;
        source = string.Empty;
        reason = string.Empty;

        string tileReason = string.Empty;
        string registryReason = string.Empty;

        if (TryPickDungeonTileDestination(out destination, out source, out tileReason))
            return true;

        if (TryPickRegistryDestination(out destination, out source, out registryReason))
            return true;

        reason = $"tileReason={tileReason} registryReason={registryReason}";

        return false;
    }

    private bool TryPickRegistryDestination(out Vector3 destination, out string source, out string reason)
    {
        destination = default;
        source = string.Empty;
        reason = "registry-empty";

        List<Transform> candidates = new();
        candidates.AddRange(GetRegistryPoints("PatrolPoints"));
        candidates.AddRange(GetRegistryPoints("SpawnPoints"));

        if (candidates.Count == 0)
            return false;

        int tries = Mathf.Max(1, Mathf.Min(candidates.Count, teleportRegistryTries));
        for (int i = 0; i < tries; i++)
        {
            Transform picked = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            if (picked == null)
            {
                reason = "registry-picked-null";
                continue;
            }

            if (!NavMesh.SamplePosition(picked.position, out NavMeshHit hit, teleportNavMeshSampleRadius, GetTeleportAreaMask()))
            {
                reason = $"registry-sample-failed:{picked.name}";
                continue;
            }

            if (!IsTeleportDistanceValid(hit.position))
            {
                reason = $"registry-too-close:{picked.name}";
                continue;
            }

            destination = hit.position;
            source = $"registry:{picked.name}";
            return true;
        }

        return false;
    }

    private bool TryPickDungeonTileDestination(out Vector3 destination, out string source, out string reason)
    {
        destination = default;
        source = string.Empty;
        reason = "runtime-dungeon-missing";

        RuntimeDungeon runtimeDungeon = FindFirstObjectByType<RuntimeDungeon>();
        if (runtimeDungeon == null)
        {
            reason = "runtime-dungeon-missing";
            return false;
        }

        if (runtimeDungeon.Generator == null)
        {
            reason = "runtime-dungeon-generator-missing";
            return false;
        }

        if (runtimeDungeon.Generator.CurrentDungeon == null)
        {
            reason = "runtime-current-dungeon-missing";
            return false;
        }

        IList<Tile> tiles = runtimeDungeon.Generator.CurrentDungeon.AllTiles;
        if (tiles == null || tiles.Count == 0)
        {
            reason = "runtime-dungeon-tiles-empty";
            return false;
        }

        int tries = Mathf.Max(1, teleportTileTries);
        for (int i = 0; i < tries; i++)
        {
            Tile tile = tiles[UnityEngine.Random.Range(0, tiles.Count)];
            if (tile == null)
            {
                reason = "runtime-dungeon-null-tile";
                continue;
            }

            Bounds bounds = tile.Bounds;
            Vector3 sample = new Vector3(
                UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
                bounds.max.y + 1f,
                UnityEngine.Random.Range(bounds.min.z, bounds.max.z));

            if (!NavMesh.SamplePosition(sample, out NavMeshHit hit, teleportNavMeshSampleRadius, GetTeleportAreaMask()))
            {
                reason = $"tile-sample-failed:{tile.name}";
                continue;
            }

            if (!IsTeleportDistanceValid(hit.position))
            {
                reason = $"tile-too-close:{tile.name}";
                continue;
            }

            destination = hit.position;
            source = $"tile:{tile.name}";
            return true;
        }

        return false;
    }

    private bool IsTeleportDistanceValid(Vector3 candidate)
    {
        Vector3 delta = candidate - transform.position;
        delta.y = 0f;
        return delta.magnitude >= Mathf.Max(0f, teleportMinDistance);
    }

    private int GetTeleportAreaMask()
    {
        return agent != null && agent.enabled ? agent.areaMask : NavMesh.AllAreas;
    }

    private void LogTeleport(string message)
    {
        if (!teleportDebugLog)
            return;

        Debug.Log($"[ClownTestTeleport] {name} {message}", this);
    }

    private IEnumerable<Transform> GetRegistryPoints(string fieldName)
    {
        var field = typeof(global::DungeonPointRegistry).GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (field?.GetValue(null) is IEnumerable<Transform> points)
            return points.Where(point => point != null);

        return Enumerable.Empty<Transform>();
    }

    private Vector3 CalculateThrowVelocity(Vector3 origin, Vector3 targetPosition)
    {
        Vector3 flat = targetPosition - origin;
        float vertical = flat.y;
        flat.y = 0f;

        Vector3 direction = flat.sqrMagnitude > 0.0001f ? flat.normalized : transform.forward;
        Vector3 velocity = direction * Mathf.Max(1f, throwSpeed);
        velocity.y = upwardArc + Mathf.Clamp(vertical, -1.5f, 2.5f);
        return velocity;
    }

    private Transform FindThrowOrigin()
    {
        string[] preferred = { "hand_r", "ik_hand_gun", "ik_hand_r", "Hand.R" };
        Transform[] transforms = GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < preferred.Length; i++)
        {
            for (int j = 0; j < transforms.Length; j++)
            {
                if (string.Equals(transforms[j].name, preferred[i], StringComparison.OrdinalIgnoreCase))
                    return transforms[j];
            }
        }

        return transform;
    }

    private bool IsMonsterDead()
    {
        var property = typeof(MonsterHealth).GetProperty("IsDead");
        return property != null && property.GetValue(health) is bool isDead && isDead;
    }

    private bool IsBehaviorGraphDriving()
    {
        return behaviorGraphAgent != null && behaviorGraphAgent.enabled && behaviorGraphAgent.Graph != null;
    }

    private GameObject InvokeRangeDetector()
    {
        var method = typeof(RangeDetector).GetMethod("UpdateDetector", Type.EmptyTypes);
        return method?.Invoke(rangeDetector, null) as GameObject;
    }

    private GameObject InvokeLineOfSight(GameObject candidate)
    {
        var method = typeof(LineOfSightDetector).GetMethod("PerformDetection", new[] { typeof(GameObject) });
        return method?.Invoke(lineOfSightDetector, new object[] { candidate }) as GameObject;
    }
}
