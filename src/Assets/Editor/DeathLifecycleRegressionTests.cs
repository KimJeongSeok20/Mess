using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>Isolated death-state fixtures; network delivery still requires a peer-level check.</summary>
public sealed class DeathLifecycleRegressionTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<GameObject> _created = new();

    private GameObject Make(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        _created.Add(go);
        return go;
    }

    private static void Set(object target, string field, object value)
        => target.GetType().GetField(field, PrivateInstance).SetValue(target, value);

    private static T Get<T>(object target, string field)
        => (T)target.GetType().GetField(field, PrivateInstance).GetValue(target);

    [TearDown]
    public void Cleanup()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i] != null) Object.DestroyImmediate(_created[i]);
        _created.Clear();
    }

    [Test]
    public void ProxyReviveRestoresCollisionAndBodyWithoutMovingThePlayer()
    {
        var go = Make("DeathProxyFixture");
        var death = go.AddComponent<PlayerDeath>();
        var controller = go.AddComponent<CharacterController>();
        var renderer = go.AddComponent<MeshRenderer>();
        var collider = go.AddComponent<BoxCollider>();
        Vector3 originalPosition = new(21234f, 35f, 123f);
        go.transform.position = originalPosition;
        controller.enabled = renderer.enabled = collider.enabled = false;
        Set(death, "_isDead", true);
        Set(death, "_waitingForRevive", true);
        Set(death, "_characterController", controller);
        Get<List<Renderer>>(death, "_hiddenBodyRenderers").Add(renderer);
        Get<List<Collider>>(death, "_disabledColliders").Add(collider);

        death.ReviveFromNetwork();

        Assert.That(death.IsDead, Is.False);
        Assert.That(death.IsWaitingForRevive, Is.False);
        Assert.That(controller.enabled && renderer.enabled && collider.enabled, Is.True);
        Assert.That(go.transform.position, Is.EqualTo(originalPosition));
    }

    [Test]
    public void LateDeathSequenceCompletionCannotLockAnAlreadyRevivedPlayer()
    {
        var death = Make("LateDeathCompletionFixture").AddComponent<PlayerDeath>();
        Set(death, "_isDead", true);
        death.ReviveFromNetwork();

        // Reproduces a presentation callback arriving after a paid/day-reset revive.
        death.CompleteDeathSequence();

        Assert.That(death.IsDead, Is.False);
        Assert.That(death.IsSequenceComplete, Is.False);
        Assert.That(Get<bool>(death, "_waitingForRevive"), Is.False,
            "A stale completion must not enter the waiting room or lock movement.");
    }

    [TestCase(true, false, false)]
    [TestCase(true, true, true)]
    [TestCase(false, false, true)]
    public void WipeRequiresEveryTeammateToBeDeadWithoutPendingRecovery(bool dead, bool pending, bool expectedRecovery)
    {
        var first = Make("DeadTeammateFixture").AddComponent<PlayerVitals>();
        var second = Make("RecoveringTeammateFixture").AddComponent<PlayerVitals>();
        Set(first, "_isDead", true);
        Set(second, "_isDead", dead);
        Set(second, "_lastStandPending", pending);

        var method = typeof(PlayerVitals).GetMethod("CanTeamRecover", BindingFlags.NonPublic | BindingFlags.Static);
        bool canRecover = (bool)method.Invoke(null, new object[] { new[] { first, second } });

        Assert.That(canRecover, Is.EqualTo(expectedRecovery));
    }

    [Test]
    public void ReviveClearsThePreviousDeathsPendingLastStand()
    {
        var vitals = Make("CancelledLastStandFixture").AddComponent<PlayerVitals>();
        Set(vitals, "_isDead", true);
        Set(vitals, "_lastStandPending", true);

        vitals.ReviveToFull();

        Assert.That(vitals.IsDead, Is.False);
        Assert.That(Get<bool>(vitals, "_lastStandPending"), Is.False);
        Assert.That(vitals.CurrentHealth, Is.EqualTo(vitals.MaxHealth));
    }
}
