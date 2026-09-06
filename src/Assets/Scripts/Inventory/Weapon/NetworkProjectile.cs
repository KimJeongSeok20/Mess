using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 서버 권위 발사체 (RPG 등 Projectile 타입 무기용)
///
/// 동작 흐름:
/// 1. FPSController.DoProjectileServer()에서 서버 측에 Instantiate
/// 2. InitializeServer()로 WeaponData 값 주입 + Rigidbody 초기 속도 설정
/// 3. 서버에서만 물리 충돌 판정 (OnCollisionEnter / OnTriggerEnter)
/// 4. 충돌 시 → 폭발 범위 데미지(서버) + ExplodeVfxRpc(모든 클라이언트 VFX)
/// 5. 자동 파괴 타이머 (수명 초과 시 공중 폭발)
///
/// 프리팹 요구사항:
/// - Rigidbody (isKinematic = false)
/// - Collider (CapsuleCollider 또는 SphereCollider)
/// - 이 스크립트 (NetworkProjectile)
/// - PurrNet NetworkIdentity는 불필요 (서버 전용 오브젝트)
///
/// 시각적 발사체는 FPSController.SpawnProjectileVisualRpc()에서
/// 모든 클라이언트에 별도 생성합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class NetworkProjectile : MonoBehaviour
{
    private const int InitialExplosionHitCapacity = 128;
    private const int MaxExplosionHitCapacity = 2048;

    private static Collider[] s_ExplosionHits = new Collider[InitialExplosionHitCapacity];

    private readonly struct ExplosionDamageCandidate
    {
        public ExplosionDamageCandidate(Collider collider, int damage)
        {
            Collider = collider;
            Damage = damage;
        }

        public Collider Collider { get; }
        public int Damage { get; }
    }

    // ============ Runtime State (서버가 스폰 시 주입) ============
    private int _damage;
    private float _explosionRadius;
    private int _explosionDamage;
    private LayerMask _explosionHitMask;
    private GameObject _explosionEffectPrefab;
    private float _explosionEffectLifetime;

    private Rigidbody _rb;
    private bool _exploded;

    // ============ 폭발 콜백 (FPSController가 구독) ============
    /// <summary>
    /// 폭발 시 (폭발 위치, 법선) 전달.
    /// FPSController가 이 이벤트를 받아 ObserversRpc로 VFX 전파.
    /// </summary>
    public event System.Action<Vector3, Vector3> OnExploded;

    /// <summary>
    /// 서버에서 스폰 직후 호출. WeaponData 값을 주입하고 Rigidbody 초기 속도 설정.
    /// </summary>
    public void InitializeServer(WeaponData data, Vector3 direction)
    {
        _damage = data.damage;
        _explosionRadius = data.explosionRadius;
        _explosionDamage = data.explosionDamage;
        _explosionHitMask = data.explosionHitMask;
        _explosionEffectPrefab = data.explosionEffectPrefab;
        _explosionEffectLifetime = data.explosionEffectLifetime;

        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = data.projectileUseGravity;
        _rb.linearVelocity = direction.normalized * data.projectileSpeed;

        // 자동 파괴 (안전장치 — 수명 초과 시 공중 폭발)
        Invoke(nameof(SelfDestruct), data.projectileLifetime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_exploded) return;

        Vector3 contactPoint = collision.contacts.Length > 0
            ? collision.contacts[0].point
            : transform.position;
        Vector3 contactNormal = collision.contacts.Length > 0
            ? collision.contacts[0].normal
            : Vector3.up;

        Explode(contactPoint, contactNormal, collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_exploded) return;
        Explode(transform.position, Vector3.up, other);
    }

    private void Explode(Vector3 point, Vector3 normal, Collider directTarget = null)
    {
        _exploded = true;

        // ── 1) 폭발 범위 데미지 (서버 권위) ──
        if (_explosionRadius > 0f)
        {
            int damagedTargetCount = ApplyExplosionDamage(point, _explosionRadius, _explosionDamage, _explosionHitMask);

            Debug.Log($"[NetworkProjectile] Explosion at {point}, radius={_explosionRadius}, " +
                      $"damaged {damagedTargetCount} unique targets");
        }
        else
        {
            if (directTarget != null)
                ApplyDamage(directTarget, _damage, point);

            Debug.Log($"[NetworkProjectile] Direct hit at {point}, damage={_damage}");
        }

        // ── 2) 폭발 이벤트 발생 → FPSController가 받아서 ObserversRpc 전파 ──
        OnExploded?.Invoke(point, normal);

        // ── 3) 서버 오브젝트 파괴 ──
        Destroy(gameObject);
    }

    private void SelfDestruct()
    {
        if (_exploded) return;
        Debug.Log("[NetworkProjectile] Lifetime expired — self-destructing");
        Explode(transform.position, Vector3.up);
    }

    internal static int ApplyExplosionDamage(Vector3 point, float radius, int explosionDamage, LayerMask hitMask)
    {
        if (radius <= 0f || explosionDamage <= 0)
            return 0;

        int hitCount = CollectExplosionHits(point, radius, hitMask);
        Dictionary<int, ExplosionDamageCandidate> candidates = new();
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = s_ExplosionHits[i];
            if (hit == null)
                continue;

            int receiverId = ResolveDamageReceiverId(hit);
            if (receiverId == 0)
                continue;

            float distance = Vector3.Distance(point, GetClosestPointForExplosion(hit, point));
            float falloff = 1f - Mathf.Clamp01(distance / radius);
            int damage = Mathf.RoundToInt(explosionDamage * falloff);
            if (damage <= 0)
                continue;

            if (candidates.TryGetValue(receiverId, out ExplosionDamageCandidate previous) && previous.Damage >= damage)
                continue;

            candidates[receiverId] = new ExplosionDamageCandidate(hit, damage);
        }

        ClearExplosionHitReferences(hitCount);

        int damagedTargets = 0;
        foreach (ExplosionDamageCandidate candidate in candidates.Values)
        {
            if (ApplyDamage(candidate.Collider, candidate.Damage, point))
                damagedTargets++;
        }

        return damagedTargets;
    }

    private static int CollectExplosionHits(Vector3 point, float radius, LayerMask hitMask)
    {
        while (true)
        {
            int hitCount = Physics.OverlapSphereNonAlloc(
                point,
                radius,
                s_ExplosionHits,
                hitMask,
                QueryTriggerInteraction.Collide);

            if (hitCount < s_ExplosionHits.Length || s_ExplosionHits.Length >= MaxExplosionHitCapacity)
                return hitCount;

            int nextCapacity = Mathf.Min(s_ExplosionHits.Length * 2, MaxExplosionHitCapacity);
            System.Array.Resize(ref s_ExplosionHits, nextCapacity);
        }
    }

    private static void ClearExplosionHitReferences(int hitCount)
    {
        for (int i = 0; i < hitCount; i++)
            s_ExplosionHits[i] = null;
    }

    private static Vector3 GetClosestPointForExplosion(Collider collider, Vector3 point)
    {
        if (collider is BoxCollider ||
            collider is SphereCollider ||
            collider is CapsuleCollider ||
            collider is MeshCollider { convex: true })
        {
            return collider.ClosestPoint(point);
        }

        // Unity's Collider.ClosestPoint logs a warning for non-convex MeshCollider,
        // TerrainCollider, and other unsupported collider types. Bounds provides a
        // stable falloff approximation without spamming the console during explosions.
        return collider.bounds.ClosestPoint(point);
    }

    private static int ResolveDamageReceiverId(Collider target)
    {
        if (target == null)
            return 0;

        OctopusSwarmMember octopus = target.GetComponentInParent<OctopusSwarmMember>();
        if (octopus != null)
            return octopus.GetInstanceID();

        MonsterHealth monsterHealth = target.GetComponentInParent<MonsterHealth>();
        if (monsterHealth != null)
            return monsterHealth.GetInstanceID();

        Transform receiverRoot = target.attachedRigidbody != null
            ? target.attachedRigidbody.transform
            : target.transform.root;
        return receiverRoot != null ? receiverRoot.GetInstanceID() : target.GetInstanceID();
    }

    private static bool ApplyDamage(Collider target, int damage, Vector3 hitPoint)
    {
        if (target == null || damage <= 0)
            return false;

        OctopusSwarmMember octopus = target.GetComponentInParent<OctopusSwarmMember>();
        if (octopus != null)
        {
            Vector3 direction = (octopus.transform.position - hitPoint).normalized;
            octopus.TakeDamage(DamageRequest.Explosive(damage, hitPoint, direction));
            return true;
        }

        MonsterHealth monsterHealth = target.GetComponentInParent<MonsterHealth>();
        if (monsterHealth != null)
        {
            Vector3 direction = (monsterHealth.transform.position - hitPoint).normalized;
            monsterHealth.TakeDamage(DamageRequest.Explosive(damage, hitPoint, direction));
            return true;
        }

        PlayerVitals playerVitals = target.GetComponentInParent<PlayerVitals>();
        if (playerVitals != null)
        {
            Vector3 direction = (playerVitals.transform.position - hitPoint).normalized;
            playerVitals.TakeDamage(DamageRequest.Explosive(damage, hitPoint, direction));
            return true;
        }

        target.SendMessageUpwards("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
        // SendMessage does not report whether a receiver existed. Preserve the
        // legacy damage fallback, but do not count an arbitrary scenery root as
        // a confirmed damaged target in explosion diagnostics.
        return false;
    }
}
