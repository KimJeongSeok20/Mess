using System.Reflection;
using Demo.Scripts.Runtime.Character;
using NUnit.Framework;
using UnityEngine;

public sealed class FPSMovementGroundingTests
{
    private static readonly MethodInfo UpdateGroundedState = typeof(FPSMovement).GetMethod(
        "UpdateGroundedState",
        BindingFlags.Instance | BindingFlags.NonPublic,
        null,
        new[] { typeof(bool), typeof(float) },
        null);

    [Test]
    public void BriefGroundLossDoesNotEnterInAirButSustainedLossDoes()
    {
        Assert.That(UpdateGroundedState, Is.Not.Null);

        GameObject player = null;
        try
        {
            player = new GameObject("FPSMovement_GroundGraceTest");
            FPSMovement movement = player.AddComponent<FPSMovement>();

            UpdateGroundedState.Invoke(movement, new object[] { false, 0.04f });
            Assert.That(movement.MovementState, Is.EqualTo(FPSMovementState.Idle));

            UpdateGroundedState.Invoke(movement, new object[] { false, 0.05f });
            Assert.That(movement.MovementState, Is.EqualTo(FPSMovementState.InAir));

            UpdateGroundedState.Invoke(movement, new object[] { true, 0.016f });
            Assert.That(movement.MovementState, Is.EqualTo(FPSMovementState.Idle));
        }
        finally
        {
            if (player != null)
                Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void UpwardImpulseEntersInAirWithoutGroundGraceDelay()
    {
        GameObject player = null;
        try
        {
            player = new GameObject("FPSMovement_ImpulseTest");
            FPSMovement movement = player.AddComponent<FPSMovement>();

            movement.ApplyExternalImpulse(Vector3.up);

            Assert.That(movement.MovementState, Is.EqualTo(FPSMovementState.InAir));
        }
        finally
        {
            if (player != null)
                Object.DestroyImmediate(player);
        }
    }
}
