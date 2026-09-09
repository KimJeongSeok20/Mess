using PurrNet;
using UnityEngine;

/// <summary>A shared world pickup. Only the server chooses and rewards its collector.</summary>
public sealed class SkillPointOrb : NetworkBehaviour
{
    [SerializeField] private Transform visual;
    [SerializeField, Min(1)] private int skillPoints = 1;
    [SerializeField, Min(0.1f)] private float pickupRadius = 1.5f;
    [SerializeField, Min(0f)] private float pickupDelay = 0.65f;
    [SerializeField, Min(1f)] private float lifetime = 180f;

    private float _age;
    private float _nextScan;
    private bool _collected;
    private Vector3 _velocity;
    private bool _inFlight;
    public event System.Action<Vector3, Vector3> Landed;

    public static SkillPointOrb Spawn(SkillPointOrb prefab, Vector3 position, int points = 1,
        Vector3 launchVelocity = default)
    {
        if (prefab == null || points <= 0) return null;
        var manager = NetworkManager.main;
        if (manager != null && (!manager.isServer || manager.prefabProvider == null
            || !manager.prefabProvider.TryGetPrefabData(prefab.gameObject, out _))) return null;

        // Keep the pickup independent of the monster, including exploded/despawned bodies.
        var instance = UnityProxy.InstantiateDirectly(prefab.gameObject,
            position, Quaternion.identity).GetComponent<SkillPointOrb>();
        // Only the server awards points; clients use the same visual for every trophy value.
        instance.skillPoints = points;
        instance._velocity = launchVelocity;
        instance._inFlight = launchVelocity.sqrMagnitude > 0f;
        if (manager != null)
        {
            instance.Spawn(prefab.gameObject);
            if (!instance.isSpawned)
            {
                Destroy(instance.gameObject);
                return null;
            }
        }
        return instance;
    }

    private void FixedUpdate()
    {
        if (!_inFlight || _collected || (isSpawned ? !isServer : NetworkManager.main != null)) return;

        _velocity += Physics.gravity * Time.fixedDeltaTime;
        Vector3 step = _velocity * Time.fixedDeltaTime;
        // The server moves ejected rewards; NetworkTransform carries that motion to clients.
        if (Physics.SphereCast(transform.position, 0.25f, step.normalized, out var hit,
            step.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            transform.position = hit.point + hit.normal * 0.26f;
            _velocity = Vector3.ProjectOnPlane(_velocity, hit.normal);
            if (hit.normal.y > 0.5f)
            {
                _inFlight = false;
                Landed?.Invoke(hit.point, hit.normal);
                Landed = null;
            }
        }
        else transform.position += step;
    }

    private void Update()
    {
        _age += Time.deltaTime;
        if (visual != null)
        {
            visual.localPosition = Vector3.up * (Mathf.Sin(_age * 3f) * 0.12f);
            visual.localRotation = Quaternion.Euler(0f, _age * 65f, 0f);
        }

        if (_collected || (isSpawned ? !isServer : NetworkManager.main != null)) return;
        if (_age >= lifetime)
        {
            Remove();
            return;
        }
        if (_inFlight || _age < pickupDelay || _age < _nextScan) return;
        _nextScan = _age + 0.1f;

        PlayerVitals closest = null;
        float bestDistance = pickupRadius * pickupRadius;
        foreach (var pawn in PlayerPawn.All)
        {
            if (pawn == null || !pawn.isActiveAndEnabled || (isSpawned && !pawn.isSpawned)
                || !pawn.TryGetComponent<PlayerVitals>(out var vitals) || vitals.IsDead
                || (isSpawned && (!vitals.isSpawned || !vitals.owner.HasValue))) continue;
            float distance = (pawn.transform.position + Vector3.up * 0.65f - transform.position).sqrMagnitude;
            if (distance > bestDistance) continue;
            closest = vitals;
            bestDistance = distance;
        }
        if (closest == null) return;

        _collected = true;
        closest.GrantSkillPoints(skillPoints, "Monster orb");
        Remove();
    }

    private void Remove()
    {
        if (isSpawned) Despawn();
        else Destroy(gameObject);
    }
}
