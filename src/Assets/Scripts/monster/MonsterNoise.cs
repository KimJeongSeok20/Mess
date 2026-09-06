using System;
using UnityEngine;

public enum NoiseKind
{
    Gunfire,
    Footsteps,
    Machinery,
}

/// <summary>
/// Server-side noise feed. Loud player actions (gunfire, sprinting) report here and monsters that
/// are not already chasing someone go and investigate the spot. Only server code reports, so the
/// feed is authoritative; listeners must still guard with their own server-authority check.
/// </summary>
public static class MonsterNoise
{
    public const float GunfireRadius = 30f;
    public const float SprintRadius = 12f;

    public static event Action<Vector3, float, NoiseKind> Reported;

    public static void Report(Vector3 position, float radius, NoiseKind kind)
    {
        if (radius <= 0f)
            return;

        Reported?.Invoke(position, radius, kind);
    }
}
