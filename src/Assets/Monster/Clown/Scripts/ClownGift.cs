using System;
using System.Collections;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class ClownGift : NetworkBehaviour
{
    [Header("Explosion")]
    [SerializeField] private float fuseDelay = 1.5f;
    [SerializeField] private float explosionRadius = 3f;
    [SerializeField] private LayerMask playerMask = 1 << 6;
    [SerializeField] private GameObject explosionEffectPrefab;
    [SerializeField] private float explosionEffectLifetime = 3f;
    [SerializeField] private float explosionEffectBaseRadius = 8f;
    [SerializeField] private Color flashColor = new(1f, 1f, 1f, 1f);
    [SerializeField] private float flashMaxAlpha = 0.45f;
    [SerializeField] private float flashDuration = 0.25f;
    [SerializeField] private float minArmDelay = 0.18f;
    [SerializeField] private float minArmImpactSpeed = 2.5f;
    [SerializeField] private float playerImpactRadius = 0.75f;
    [SerializeField] private float playerImpactHoldDuration = 0.12f;
    [SerializeField] private LayerMask playerImpactLineOfSightMask = ~0;

    [Header("Weighted Effects")]
    [SerializeField] private ClownGiftEffectWeight[] weightedEffects = ClownGiftSettings.CreateDefaultEffectWeights();

    [Header("Damage")]
    [SerializeField] private int damage = 28;

    [Header("Knockback")]
    [SerializeField] private float knockbackDistance = 3.5f;
    [SerializeField] private float knockbackDuration = 0.25f;
    [SerializeField] private float knockbackImpulse = 12f;
    [SerializeField] private float knockbackUpwardImpulse = 2.5f;
    [SerializeField] private float knockbackDamping = 10f;

    [Header("Slow")]
    [SerializeField, Range(0.1f, 1f)] private float slowMultiplier = 0.45f;
    [SerializeField] private float slowDuration = 2.5f;

    [Header("Teleport")]
    [SerializeField] private float teleportVerticalOffset = 0.15f;
    [SerializeField] private float teleportNavMeshSampleRadius = 1.5f;

    [Header("Item Spawn")]
    [SerializeField] private Item[] randomItemPrefabs;
    [SerializeField] private ClownGiftItemDropWeight[] randomItemDropWeights = Array.Empty<ClownGiftItemDropWeight>();
    [SerializeField] private Vector3 randomItemSpawnOffset = new(0f, 0.2f, 0f);

    private readonly List<global::PlayerVitals> _targets = new();
    private readonly List<Collider> _ignoredThrowerColliders = new();
    private Rigidbody _rigidbody;
    private Collider _collider;
    private Collider[] _giftColliders = Array.Empty<Collider>();
    private ClownDeathSequence _deathSequence;
    private ClownMonsterActor _sourceActor;
    private bool _armed;
    private bool _exploded;
    private bool _stoppedOnPlayerImpact;
    private bool _playerImpactHoldActive;
    private bool _networkSpawned;
    private float _armAtTime;

    public bool HasStoppedOnPlayerImpact => _stoppedOnPlayerImpact;
    public bool IsPlayerImpactHoldActive => _playerImpactHoldActive;
    public bool HasExploded => _exploded;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _giftColliders = GetComponentsInChildren<Collider>(true);
        _collider = _giftColliders.Length > 0 ? _giftColliders[0] : null;
        ExcludePlayerPhysicalCollisions();

        if (_rigidbody != null)
        {
            _rigidbody.useGravity = true;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    protected override void OnSpawned()
    {
        base.OnSpawned();
        _networkSpawned = true;
    }

    public void ApplySettings(ClownGiftSettings settings, Item[] fallbackRandomItems = null)
    {
        if (settings == null)
        {
            if (fallbackRandomItems != null && fallbackRandomItems.Length > 0)
                randomItemPrefabs = fallbackRandomItems;
            return;
        }

        fuseDelay = Mathf.Max(0.05f, settings.fuseDelay);
        explosionRadius = Mathf.Max(0.1f, settings.explosionRadius);
        explosionEffectPrefab = settings.explosionEffectPrefab;
        explosionEffectLifetime = Mathf.Max(0.1f, settings.explosionEffectLifetime);
        explosionEffectBaseRadius = Mathf.Max(0.01f, settings.explosionEffectBaseRadius);
        flashColor = settings.flashColor;
        flashMaxAlpha = Mathf.Clamp01(settings.flashMaxAlpha);
        flashDuration = Mathf.Max(0.05f, settings.flashDuration);
        minArmDelay = Mathf.Max(0f, settings.minArmDelay);
        minArmImpactSpeed = Mathf.Max(0f, settings.minArmImpactSpeed);
        playerImpactHoldDuration = Mathf.Max(0.01f, settings.playerImpactHoldDuration);
        weightedEffects = CloneEffectWeights(settings.effectWeights);
        damage = Mathf.Max(1, settings.damage);
        knockbackDistance = Mathf.Max(0f, settings.knockbackDistance);
        knockbackDuration = Mathf.Max(0.05f, settings.knockbackDuration);
        knockbackImpulse = Mathf.Max(0f, settings.knockbackImpulse);
        knockbackUpwardImpulse = Mathf.Max(0f, settings.knockbackUpwardImpulse);
        knockbackDamping = Mathf.Max(0.01f, settings.knockbackDamping);
        slowMultiplier = Mathf.Clamp(settings.slowMultiplier, 0.1f, 1f);
        slowDuration = Mathf.Max(0.05f, settings.slowDuration);
        teleportVerticalOffset = settings.teleportVerticalOffset;
        teleportNavMeshSampleRadius = Mathf.Max(0.1f, settings.teleportNavMeshSampleRadius);
        randomItemDropWeights = CloneItemDropWeights(settings.randomItemDropWeights);

        if (fallbackRandomItems != null && fallbackRandomItems.Length > 0)
            randomItemPrefabs = fallbackRandomItems;
    }

    public void InitializeServer(Vector3 initialVelocity, ClownDeathSequence deathSequence, Item[] itemPrefabs = null, Collider[] throwerColliders = null, ClownMonsterActor sourceActor = null)
    {
        if (!HasServerAuthority())
            return;

        _deathSequence = deathSequence;
        _sourceActor = sourceActor;
        _armed = false;
        _exploded = false;
        _stoppedOnPlayerImpact = false;
        _playerImpactHoldActive = false;
        _armAtTime = Time.time + Mathf.Max(0f, minArmDelay);
        CancelInvoke(nameof(ReleaseImpactHold));

        if (itemPrefabs != null && itemPrefabs.Length > 0)
            randomItemPrefabs = itemPrefabs;

        ExcludePlayerPhysicalCollisions();
        IgnoreThrowerCollisions(throwerColliders);

        if (_rigidbody == null)
            _rigidbody = GetComponent<Rigidbody>();

        if (_rigidbody != null)
        {
            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = true;
            _rigidbody.constraints = RigidbodyConstraints.None;
            _rigidbody.linearVelocity = initialVelocity;
        }
    }

    private void ExcludePlayerPhysicalCollisions()
    {
        if (_giftColliders == null || _giftColliders.Length == 0)
            _giftColliders = GetComponentsInChildren<Collider>(true);

        int excludedPlayerLayers = playerMask.value;
        if (excludedPlayerLayers == 0)
            return;

        for (int i = 0; i < _giftColliders.Length; i++)
        {
            Collider giftCollider = _giftColliders[i];
            if (giftCollider == null)
                continue;

            giftCollider.excludeLayers = giftCollider.excludeLayers.value | excludedPlayerLayers;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!HasServerAuthority() || _exploded)
            return;

        bool hitPlayer = collision.collider != null && collision.collider.GetComponentInParent<global::PlayerVitals>() != null;
        if (_armed)
        {
            if (hitPlayer && !_stoppedOnPlayerImpact)
                StopMotionOnImpact();

            return;
        }

        if (Time.time < _armAtTime)
            return;

        if (collision.collider != null && _ignoredThrowerColliders.Contains(collision.collider))
            return;

        if (collision.relativeVelocity.magnitude < minArmImpactSpeed)
            return;

        if (hitPlayer && !_stoppedOnPlayerImpact)
            StopMotionOnImpact();

        ArmGift();
    }

    private void FixedUpdate()
    {
        if (!HasServerAuthority() || _exploded || _stoppedOnPlayerImpact || Time.time < _armAtTime)
            return;

        if (!TryFindPlayerImpactTarget(out _))
            return;

        StopMotionOnImpact();
        ArmGift();
    }

    private void IgnoreThrowerCollisions(Collider[] throwerColliders)
    {
        _ignoredThrowerColliders.Clear();

        if (throwerColliders == null || throwerColliders.Length == 0)
            return;

        if (_giftColliders == null || _giftColliders.Length == 0)
            _giftColliders = GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < throwerColliders.Length; i++)
        {
            Collider throwerCollider = throwerColliders[i];
            if (throwerCollider == null)
                continue;

            _ignoredThrowerColliders.Add(throwerCollider);

            for (int j = 0; j < _giftColliders.Length; j++)
            {
                Collider giftCollider = _giftColliders[j];
                if (giftCollider == null)
                    continue;

                Physics.IgnoreCollision(giftCollider, throwerCollider, true);
            }
        }
    }

    private void ArmGift()
    {
        if (_armed)
            return;

        _armed = true;

        Invoke(nameof(ExplodeServer), Mathf.Max(0.05f, fuseDelay));
    }

    private void StopMotionOnImpact()
    {
        if (_rigidbody == null || _playerImpactHoldActive)
            return;

        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        _rigidbody.useGravity = false;
        _rigidbody.constraints = RigidbodyConstraints.None;
        _rigidbody.isKinematic = true;
        _stoppedOnPlayerImpact = true;
        _playerImpactHoldActive = true;

        CancelInvoke(nameof(ReleaseImpactHold));
        Invoke(nameof(ReleaseImpactHold), Mathf.Max(0.01f, playerImpactHoldDuration));
    }

    private void ReleaseImpactHold()
    {
        if (!HasServerAuthority() || _exploded || _rigidbody == null)
            return;

        _rigidbody.isKinematic = false;
        _rigidbody.useGravity = true;
        _rigidbody.constraints = RigidbodyConstraints.None;
        _playerImpactHoldActive = false;
    }

    private bool TryFindPlayerImpactTarget(out global::PlayerVitals vitals)
    {
        vitals = null;

        float radius = Mathf.Max(0.05f, playerImpactRadius);
        Collider[] hits = Physics.OverlapSphere(transform.position, radius, playerMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            var candidate = hits[i].GetComponentInParent<global::PlayerVitals>();
            if (candidate == null || candidate.IsDead)
                continue;

            if (!HasClearPlayerImpactPath(candidate))
                continue;

            vitals = candidate;
            return true;
        }

        return false;
    }

    private bool HasClearPlayerImpactPath(global::PlayerVitals vitals)
    {
        if (vitals == null)
            return false;

        return HasClearLineOfSight(transform.position, ResolvePlayerImpactAimPosition(vitals), vitals);
    }

    private bool HasClearExplosionPath(Vector3 center, global::PlayerVitals vitals)
    {
        if (vitals == null)
            return false;

        return HasClearLineOfSight(center, ResolvePlayerImpactAimPosition(vitals), vitals);
    }

    private bool HasClearLineOfSight(Vector3 origin, Vector3 target, global::PlayerVitals vitals)
    {
        Vector3 direction = target - origin;
        float distance = direction.magnitude;
        if (distance <= 0.01f)
            return true;

        RaycastHit[] hits = Physics.RaycastAll(origin, direction / distance, distance, playerImpactLineOfSightMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return true;

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null)
                continue;

            if (IsGiftCollider(hitCollider) || _ignoredThrowerColliders.Contains(hitCollider))
                continue;

            return hitCollider.GetComponentInParent<global::PlayerVitals>() == vitals;
        }

        return true;
    }

    private bool IsGiftCollider(Collider candidate)
    {
        if (candidate == null || _giftColliders == null)
            return false;

        for (int i = 0; i < _giftColliders.Length; i++)
        {
            if (_giftColliders[i] == candidate)
                return true;
        }

        return false;
    }

    private static Vector3 ResolvePlayerImpactAimPosition(global::PlayerVitals vitals)
    {
        Collider[] colliders = vitals.GetComponentsInChildren<Collider>(true);
        bool hasBounds = false;
        Bounds bounds = default;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds ? bounds.center : vitals.transform.position + Vector3.up;
    }

    private void ExplodeServer()
    {
        if (!HasServerAuthority() || _exploded)
            return;

        _exploded = true;
        CancelInvoke(nameof(ReleaseImpactHold));

        Vector3 center = transform.position;
        GiftEffectType effect = RollEffect();
        Vector3 presentationTeleportDestination = Vector3.zero;

        CollectTargets(center);

        if (effect == GiftEffectType.RandomItem)
            SpawnRandomItem(center);

        for (int i = 0; i < _targets.Count; i++)
        {
            Vector3 targetTeleportDestination = Vector3.zero;
            if (effect == GiftEffectType.Teleport && _targets[i] != null)
            {
                targetTeleportDestination = PickTeleportDestination(_targets[i].transform.position);
                if (presentationTeleportDestination == Vector3.zero)
                    presentationTeleportDestination = targetTeleportDestination;
            }

            ApplyEffectServer(_targets[i], effect, center, targetTeleportDestination);
        }

        if (_sourceActor != null && _sourceActor.CanBroadcastNetworkPresentation)
        {
            _sourceActor.PlayGiftExplosionObserversRpc(center, effect, presentationTeleportDestination);
            ApplyHostExplosionFeedbackLocal(center, effect, presentationTeleportDestination);
        }
        else if (_networkSpawned)
        {
            if (explosionEffectPrefab != null)
                SpawnExplosionEffectObserversRpc(center, effect, presentationTeleportDestination);
            else
                ApplyExplosionFeedbackObserversRpc(center, effect, presentationTeleportDestination);
        }
        else
        {
            ApplyExplosionFeedbackLocal(center, effect, presentationTeleportDestination);
        }

        Destroy(gameObject);
    }

    private bool HasServerAuthority()
    {
        if (isSpawned)
            return isServer;

        NetworkManager nm = NetworkManager.main;
        return nm == null || nm.isServer;
    }

    private void CollectTargets(Vector3 center)
    {
        _targets.Clear();

        Collider[] hits = Physics.OverlapSphere(center, explosionRadius, playerMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            global::PlayerVitals vitals = hits[i].GetComponentInParent<global::PlayerVitals>();
            if (vitals == null || vitals.IsDead || _targets.Contains(vitals))
                continue;

            if (!HasClearExplosionPath(center, vitals))
                continue;

            _targets.Add(vitals);
        }
    }

    private void ApplyEffectServer(global::PlayerVitals vitals, GiftEffectType effect, Vector3 center, Vector3 teleportDestination)
    {
        if (vitals == null)
            return;

        switch (effect)
        {
            case GiftEffectType.Knockback:
                ApplyDamageToPlayer(vitals, damage, _deathSequence);
                ApplyServerDisplacement(vitals.gameObject, center);
                break;

            case GiftEffectType.Damage:
                ApplyDamageToPlayer(vitals, damage, _deathSequence);
                break;

            case GiftEffectType.Slow:
                ApplyDamageToPlayer(vitals, damage, _deathSequence);
                ApplyServerSlow(vitals.gameObject);
                break;

            case GiftEffectType.Teleport:
                if (teleportDestination != Vector3.zero)
                    TeleportCharacter(vitals.gameObject, teleportDestination + Vector3.up * teleportVerticalOffset);
                break;

            case GiftEffectType.RandomItem:
                break;
        }
    }

    private void ApplyServerDisplacement(GameObject target, Vector3 center)
    {
        Vector3 impulse = GetKnockbackImpulse(center, target.transform.position, target.transform.forward);
        if (TryApplyMovementImpulse(target, impulse, knockbackDuration, knockbackDamping))
            return;

        Vector3 destination = target.transform.position + new Vector3(impulse.x, 0f, impulse.z) * Mathf.Max(0.05f, knockbackDuration);
        TeleportCharacter(target, destination);
    }

    private void ApplyServerSlow(GameObject target)
    {
        if (target == null)
            return;

        var controller = target.GetComponent<global::Demo.Scripts.Runtime.Character.FPSController>();
        if (controller == null)
            controller = target.GetComponentInParent<global::Demo.Scripts.Runtime.Character.FPSController>();

        if (controller != null)
            controller.ApplyServerMovementInputMultiplier(slowMultiplier, slowDuration);
    }

    private Vector3 GetKnockbackImpulse(Vector3 center, Vector3 targetPosition, Vector3 fallbackForward)
    {
        Vector3 direction = targetPosition - center;
        direction.y = 0f;
        float distanceFromCenter = direction.magnitude;

        if (distanceFromCenter <= 0.0001f)
        {
            direction = fallbackForward;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.forward;
        else
            direction.Normalize();

        float falloff = 1f - Mathf.Clamp01(distanceFromCenter / Mathf.Max(0.01f, explosionRadius));
        falloff = Mathf.Max(0.1f, falloff);
        float horizontalImpulse = Mathf.Max(0f, knockbackImpulse) * falloff;
        float upwardImpulse = Mathf.Max(0f, knockbackUpwardImpulse) * falloff;
        return direction * horizontalImpulse + Vector3.up * upwardImpulse;
    }

    private void TeleportCharacter(GameObject target, Vector3 destination)
    {
        if (target == null)
            return;

        CharacterController controller = target.GetComponent<CharacterController>();
        bool restore = controller != null && controller.enabled;
        if (restore)
            controller.enabled = false;

        target.transform.position = destination;

        if (restore)
            controller.enabled = true;
    }

    private void SpawnRandomItem(Vector3 center)
    {
        Item selected = PickRandomItem();
        if (selected == null)
            return;

        GameObject spawned = Instantiate(selected.gameObject, center + randomItemSpawnOffset, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
        NetworkIdentity identity = spawned.GetComponent<NetworkIdentity>();
        NetworkManager networkManager = NetworkManager.main;
        if (identity != null && !identity.isSpawned && networkManager != null && networkManager.isServer)
            identity.Spawn(selected.gameObject);
    }

    private Item PickRandomItem()
    {
        if (randomItemDropWeights != null && randomItemDropWeights.Length > 0)
        {
            float total = 0f;
            for (int i = 0; i < randomItemDropWeights.Length; i++)
            {
                if (randomItemDropWeights[i]?.item == null)
                    continue;

                total += Mathf.Max(0f, randomItemDropWeights[i].weight);
            }

            if (total > 0f)
            {
                float roll = UnityEngine.Random.value * total;
                for (int i = 0; i < randomItemDropWeights.Length; i++)
                {
                    ClownGiftItemDropWeight entry = randomItemDropWeights[i];
                    if (entry?.item == null)
                        continue;

                    roll -= Mathf.Max(0f, entry.weight);
                    if (roll <= 0f)
                        return entry.item;
                }
            }
        }

        if (randomItemPrefabs == null || randomItemPrefabs.Length == 0)
            return null;

        return randomItemPrefabs[UnityEngine.Random.Range(0, randomItemPrefabs.Length)];
    }

    private Vector3 PickTeleportDestination(Vector3 originPosition)
    {
        if (_sourceActor != null && _sourceActor.TryPickTeleportDestinationFrom(originPosition, out Vector3 actorDestination, out _, out _))
            return actorDestination;

        List<Transform> candidates = GetRegistryPoints();

        if (candidates.Count == 0)
            return Vector3.zero;

        int tries = Mathf.Min(candidates.Count, 8);
        for (int i = 0; i < tries; i++)
        {
            Transform picked = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            if (picked == null)
                continue;

            if (TrySampleOwnedNavMesh(picked.position, teleportNavMeshSampleRadius, out NavMeshHit navHit))
                return navHit.position;
        }

        return Vector3.zero;
    }

    private GiftEffectType RollEffect()
    {
        if (weightedEffects == null || weightedEffects.Length == 0)
            return GiftEffectType.Knockback;

        float total = 0f;
        for (int i = 0; i < weightedEffects.Length; i++)
            total += Mathf.Max(0f, weightedEffects[i].weight);

        if (total <= 0f)
            return GiftEffectType.Knockback;

        float roll = UnityEngine.Random.value * total;
        for (int i = 0; i < weightedEffects.Length; i++)
        {
            roll -= Mathf.Max(0f, weightedEffects[i].weight);
            if (roll <= 0f)
                return weightedEffects[i].effect;
        }

        return weightedEffects[weightedEffects.Length - 1].effect;
    }

    [ObserversRpc]
    private void SpawnExplosionEffectObserversRpc(Vector3 center, GiftEffectType effect, Vector3 teleportDestination)
    {
        SpawnExplosionVisual(center);
        ApplyExplosionFeedbackObserversRpc(center, effect, teleportDestination);
    }

    [ObserversRpc]
    private void ApplyExplosionFeedbackObserversRpc(Vector3 center, GiftEffectType effect, Vector3 teleportDestination)
    {
        if (HasServerAuthority())
        {
            ApplyHostExplosionFeedbackLocal(center, effect, teleportDestination);
            return;
        }

        ApplyExplosionFeedbackLocal(center, effect, teleportDestination);
    }

    private void ApplyExplosionFeedbackLocal(Vector3 center, GiftEffectType effect, Vector3 teleportDestination)
    {
        ApplyRemoteExplosionFeedback(
            explosionEffectPrefab,
            explosionEffectLifetime,
            explosionEffectBaseRadius,
            center,
            effect,
            teleportDestination,
            flashColor,
            flashMaxAlpha,
            flashDuration,
            explosionRadius,
            knockbackImpulse,
            knockbackUpwardImpulse,
            knockbackDuration,
            knockbackDamping,
            teleportVerticalOffset);
    }

    private void ApplyCosmeticExplosionFeedbackLocal(Vector3 center)
    {
        SpawnExplosionVisual(center);

        global::Demo.Scripts.Runtime.Character.FPSMovement localMovement = GetLocalPlayerMovement();
        if (localMovement == null)
            return;

        GameObject localPlayer = localMovement.gameObject;
        if ((localPlayer.transform.position - center).sqrMagnitude > explosionRadius * explosionRadius)
            return;

        ClownGiftLocalEffectReceiver receiver = localPlayer.GetComponent<ClownGiftLocalEffectReceiver>();
        if (receiver == null)
            receiver = localPlayer.AddComponent<ClownGiftLocalEffectReceiver>();

        receiver.ApplyFlash(flashColor, flashMaxAlpha, flashDuration);
    }

    private void ApplyHostExplosionFeedbackLocal(Vector3 center, GiftEffectType effect, Vector3 teleportDestination)
    {
        ApplyCosmeticExplosionFeedbackLocal(center);
    }

    private void SpawnExplosionVisual(Vector3 center)
    {
        if (explosionEffectPrefab != null)
        {
            GameObject fx = Instantiate(explosionEffectPrefab, center, Quaternion.identity);
            ScaleExplosionEffect(fx, explosionRadius, explosionEffectBaseRadius);
            Destroy(fx, Mathf.Max(0.1f, explosionEffectLifetime));
        }
    }

    public static void ApplyRemoteExplosionFeedback(
        GameObject effectPrefab,
        float effectLifetime,
        float effectBaseRadius,
        Vector3 center,
        GiftEffectType effect,
        Vector3 teleportDestination,
        Color flashColor,
        float flashMaxAlpha,
        float flashDuration,
        float explosionRadius,
        float knockbackImpulse,
        float knockbackUpwardImpulse,
        float knockbackDuration,
        float knockbackDamping,
        float teleportVerticalOffset)
    {
        if (effectPrefab != null)
        {
            GameObject fx = Instantiate(effectPrefab, center, Quaternion.identity);
            ScaleExplosionEffect(fx, explosionRadius, effectBaseRadius);
            Destroy(fx, Mathf.Max(0.1f, effectLifetime));
        }

        global::Demo.Scripts.Runtime.Character.FPSMovement localMovement = GetLocalPlayerMovement();
        if (localMovement == null)
            return;

        GameObject localPlayer = localMovement.gameObject;
        if ((localPlayer.transform.position - center).sqrMagnitude > explosionRadius * explosionRadius)
            return;

        ClownGiftLocalEffectReceiver receiver = localPlayer.GetComponent<ClownGiftLocalEffectReceiver>();
        if (receiver == null)
            receiver = localPlayer.AddComponent<ClownGiftLocalEffectReceiver>();

        receiver.ApplyFlash(flashColor, flashMaxAlpha, flashDuration);

        switch (effect)
        {
            case GiftEffectType.Knockback:
                receiver.ApplyKnockback(center, explosionRadius, knockbackImpulse, knockbackUpwardImpulse, knockbackDuration, knockbackDamping);
                break;

            case GiftEffectType.Teleport:
                // Teleport is server-authoritative. A single broadcast destination is
                // only useful for presentation and is not safe for local prediction.
                break;
        }
    }

    private static bool TrySampleClownNavMesh(Vector3 sourcePosition, float maxDistance, out NavMeshHit hit)
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = ClownNavMeshConfig.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };

        return NavMesh.SamplePosition(sourcePosition, out hit, maxDistance, filter);
    }

    private bool TrySampleOwnedNavMesh(Vector3 sourcePosition, float maxDistance, out NavMeshHit hit)
    {
        if (_sourceActor != null && _sourceActor.TrySampleOwnedNavMesh(sourcePosition, maxDistance, out hit))
            return true;

        return TrySampleClownNavMesh(sourcePosition, maxDistance, out hit);
    }

    private static void ApplyDamageToPlayer(global::PlayerVitals vitals, int amount, ClownDeathSequence deathSequence)
    {
        if (vitals == null)
            return;

        int before = vitals.CurrentHealth;
        vitals.ApplyDamage(amount, deathSequence);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (vitals.CurrentHealth != before)
            Debug.Log($"[ClownGift] Damage applied to {vitals.name}: {before} -> {vitals.CurrentHealth} (amount={amount})");
#endif
    }

    private static void ScaleExplosionEffect(GameObject fx, float explosionRadius, float effectBaseRadius)
    {
        if (fx == null)
            return;

        ParticleSystem[] particleSystems = fx.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null)
                continue;

            ParticleSystem.MainModule main = particleSystem.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }

        float scale = Mathf.Max(0.01f, explosionRadius / Mathf.Max(0.01f, effectBaseRadius));
        fx.transform.localScale = Vector3.Scale(fx.transform.localScale, new Vector3(scale, scale, scale));
    }

    private static bool TryApplyMovementImpulse(GameObject target, Vector3 impulse, float controlLockTime, float damping)
    {
        if (target == null)
            return false;

        var movement = target.GetComponent<global::Demo.Scripts.Runtime.Character.FPSMovement>();
        if (movement == null)
            return false;

        movement.ApplyExternalImpulse(impulse, controlLockTime, damping);

        return true;
    }

    private static List<Transform> GetRegistryPoints()
    {
        var points = new List<Transform>(global::DungeonPointRegistry.PatrolPoints.Count + global::DungeonPointRegistry.SpawnPoints.Count);

        for (int i = 0; i < global::DungeonPointRegistry.PatrolPoints.Count; i++)
        {
            Transform point = global::DungeonPointRegistry.PatrolPoints[i];
            if (point != null)
                points.Add(point);
        }

        for (int i = 0; i < global::DungeonPointRegistry.SpawnPoints.Count; i++)
        {
            Transform point = global::DungeonPointRegistry.SpawnPoints[i];
            if (point != null)
                points.Add(point);
        }

        return points;
    }

    private static global::Demo.Scripts.Runtime.Character.FPSMovement GetLocalPlayerMovement()
    {
        return global::Demo.Scripts.Runtime.Character.FPSMovement.LocalPlayerPosition;
    }

    private sealed class ClownGiftLocalEffectReceiver : MonoBehaviour
    {
        private CharacterController _controller;
        private global::Demo.Scripts.Runtime.Character.FPSMovement _movement;
        private global::PlayerDeath _playerDeath;
        private Coroutine _flashRoutine;
        private bool _hasOverlayBaseline;
        private bool _baselineOverlayActive;
        private Color _baselineOverlayColor;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _movement = GetComponent<global::Demo.Scripts.Runtime.Character.FPSMovement>();
            _playerDeath = GetComponent<global::PlayerDeath>();
        }

        private void OnDisable()
        {
            RestoreOverlay();
        }

        public void ApplyKnockback(Vector3 center, float radius, float horizontalImpulse, float upwardImpulse, float controlLockTime, float damping)
        {
            float safeRadius = Mathf.Max(0.01f, radius);
            Vector3 direction = transform.position - center;
            direction.y = 0f;
            float distanceFromCenter = direction.magnitude;
            if (distanceFromCenter < 0.0001f)
                direction = transform.forward;
            else
                direction /= distanceFromCenter;

            float falloff = 1f - Mathf.Clamp01(distanceFromCenter / safeRadius);
            falloff = Mathf.Max(0.1f, falloff);
            Vector3 impulse = direction * (Mathf.Max(0f, horizontalImpulse) * falloff) + Vector3.up * (Mathf.Max(0f, upwardImpulse) * falloff);
            if (_movement != null)
                _movement.ApplyExternalImpulse(impulse, controlLockTime, damping);
        }

        public void ApplyTeleport(Vector3 destination)
        {
            bool restore = _controller != null && _controller.enabled;
            if (restore)
                _controller.enabled = false;

            transform.position = destination;

            if (restore)
                _controller.enabled = true;
        }

        public void ApplyFlash(Color color, float maxAlpha, float duration)
        {
            Image overlay = GetOverlayImage();
            if (_playerDeath == null || overlay == null)
                return;

            CaptureOverlayBaseline(overlay);

            if (_flashRoutine != null)
                StopCoroutine(_flashRoutine);

            _flashRoutine = StartCoroutine(FlashRoutine(overlay, color, maxAlpha, duration));
        }

        private Image GetOverlayImage()
        {
            return _playerDeath != null ? _playerDeath.OverlayImage : null;
        }

        private void CaptureOverlayBaseline(Image overlay)
        {
            if (_hasOverlayBaseline || overlay == null)
                return;

            _baselineOverlayActive = overlay.gameObject.activeSelf;
            _baselineOverlayColor = overlay.color;
            _hasOverlayBaseline = true;
        }

        private void RestoreOverlay()
        {
            Image overlay = GetOverlayImage();
            if (!_hasOverlayBaseline || overlay == null)
                return;

            overlay.color = _baselineOverlayColor;
            overlay.gameObject.SetActive(_baselineOverlayActive);
            _hasOverlayBaseline = false;
            _flashRoutine = null;
        }

        private IEnumerator FlashRoutine(Image overlay, Color color, float maxAlpha, float duration)
        {
            if (overlay == null)
            {
                _flashRoutine = null;
                yield break;
            }

            overlay.gameObject.SetActive(true);
            float safeDuration = Mathf.Max(0.05f, duration);
            float elapsed = 0f;

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                float alpha = Mathf.Lerp(maxAlpha, 0f, t);
                overlay.color = new Color(color.r, color.g, color.b, alpha);
                yield return null;
            }

            _flashRoutine = null;
            RestoreOverlay();
        }
    }

    private static ClownGiftEffectWeight[] CloneEffectWeights(ClownGiftEffectWeight[] source)
    {
        if (source == null || source.Length == 0)
            return ClownGiftSettings.CreateDefaultEffectWeights();

        var clone = new ClownGiftEffectWeight[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var entry = source[i];
            clone[i] = new ClownGiftEffectWeight
            {
                effect = entry != null ? entry.effect : GiftEffectType.Damage,
                weight = entry != null ? Mathf.Max(0f, entry.weight) : 0f
            };
        }

        return clone;
    }

    private static ClownGiftItemDropWeight[] CloneItemDropWeights(ClownGiftItemDropWeight[] source)
    {
        if (source == null || source.Length == 0)
            return Array.Empty<ClownGiftItemDropWeight>();

        var clone = new ClownGiftItemDropWeight[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var entry = source[i];
            clone[i] = new ClownGiftItemDropWeight
            {
                item = entry != null ? entry.item : null,
                weight = entry != null ? Mathf.Max(0f, entry.weight) : 0f
            };
        }

        return clone;
    }
}
