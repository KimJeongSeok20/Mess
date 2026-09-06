using UnityEngine;

public enum DamageType
{
    Bullet,
    Melee,
    Explosive
}

public readonly struct DamageRequest
{
    public DamageRequest(
        int amount,
        DamageType damageType,
        Vector3 hitPoint = default,
        Vector3 hitDirection = default,
        bool hasHitPoint = false)
    {
        Amount = amount;
        DamageType = damageType;
        HitPoint = hitPoint;
        HitDirection = hitDirection;
        HasHitPoint = hasHitPoint;
    }

    public int Amount { get; }
    public DamageType DamageType { get; }
    public Vector3 HitPoint { get; }
    public Vector3 HitDirection { get; }
    public bool HasHitPoint { get; }

    public static DamageRequest Bullet(int amount, Vector3 hitPoint, Vector3 hitDirection = default)
    {
        return new DamageRequest(amount, DamageType.Bullet, hitPoint, hitDirection, true);
    }

    public static DamageRequest Melee(int amount, Vector3 hitPoint = default, Vector3 hitDirection = default)
    {
        return new DamageRequest(amount, DamageType.Melee, hitPoint, hitDirection, hitPoint != default);
    }

    public static DamageRequest Explosive(int amount, Vector3 hitPoint, Vector3 hitDirection = default)
    {
        return new DamageRequest(amount, DamageType.Explosive, hitPoint, hitDirection, true);
    }
}
