using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class SkillPerkEffectRegressionTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private GameObject _player;
    private PlayerVitals _vitals;
    private PlayerPerks _perks;
    private Dictionary<PlayerPerkKind, float> _totals;

    [SetUp]
    public void SetUp()
    {
        _player = new GameObject("SkillPerkEffectRegressionTest");
        _player.AddComponent<Camera>();
        _vitals = _player.AddComponent<PlayerVitals>();
        _perks = _player.GetComponent<PlayerPerks>() ?? _player.AddComponent<PlayerPerks>();

        // Invoke the lifecycle explicitly so the fixture works without entering Play Mode.
        Invoke(_perks, "OnDisable");
        SetField(_perks, "_vitals", _vitals);
        SetField(_perks, "_networkPlayer", null);
        Invoke(_vitals, "RecalculateDerivedStats", true);
        Invoke(_perks, "OnEnable");
        _totals = (Dictionary<PlayerPerkKind, float>)GetField(_perks, "_totals");
        _totals[PlayerPerkKind.Adrenaline] = 0.25f;
    }

    [TearDown]
    public void TearDown()
    {
        if (_player != null)
            Object.DestroyImmediate(_player);
    }

    [Test]
    public void MaximumHealthIncreaseFillsNewCapacityWithoutTriggeringAdrenaline()
    {
        _totals[PlayerPerkKind.MaxHealth] = 25f;

        Invoke(_perks, "ApplyTotals");

        Assert.That(_vitals.CurrentHealth, Is.EqualTo(125));
        Assert.That(_vitals.MaxHealth, Is.EqualTo(125));
        Assert.That(AdrenalineBonus, Is.Zero);
    }

    [Test]
    public void MaximumHealthReductionClampDoesNotTriggerAdrenaline()
    {
        _totals[PlayerPerkKind.MaxHealth] = 25f;
        Invoke(_perks, "ApplyTotals");
        Assert.That(_vitals.CurrentHealth, Is.EqualTo(125));

        _totals.Remove(PlayerPerkKind.MaxHealth);
        Invoke(_perks, "ApplyTotals");

        Assert.That(_vitals.CurrentHealth, Is.EqualTo(100));
        Assert.That(_vitals.MaxHealth, Is.EqualTo(100));
        Assert.That(AdrenalineBonus, Is.Zero);
    }

    [Test]
    public void MaximumHealthIncreaseFullyHealsAnInjuredLivingPlayer()
    {
        _vitals.ApplyDamage(40, null);
        ExpireAdrenaline();
        Assert.That(_vitals.CurrentHealth, Is.EqualTo(60));

        _totals[PlayerPerkKind.MaxHealth] = 25f;
        Invoke(_perks, "ApplyTotals");

        Assert.That(_vitals.MaxHealth, Is.EqualTo(125));
        Assert.That(_vitals.CurrentHealth, Is.EqualTo(125));
        Assert.That(AdrenalineBonus, Is.Zero);
    }

    [Test]
    public void ReapplyingTheSameCapacityOrOtherBonusesDoesNotHealAgain()
    {
        _totals[PlayerPerkKind.MaxHealth] = 25f;
        Invoke(_perks, "ApplyTotals");
        _vitals.ApplyDamage(20, null);
        ExpireAdrenaline();

        Invoke(_perks, "ApplyTotals");
        Assert.That(_vitals.CurrentHealth, Is.EqualTo(105));

        _totals[PlayerPerkKind.MaxStamina] = 30f;
        Invoke(_perks, "ApplyTotals");
        Assert.That(_vitals.MaxStamina, Is.EqualTo(130f));
        Assert.That(_vitals.CurrentHealth, Is.EqualTo(105));

        _totals[PlayerPerkKind.DamageReduction] = 0.1f;
        Invoke(_perks, "ApplyTotals");
        Assert.That(_vitals.MaxHealth, Is.EqualTo(125));
        Assert.That(_vitals.CurrentHealth, Is.EqualTo(105));
        Assert.That(AdrenalineBonus, Is.Zero);
    }

    [Test]
    public void RefundingHealthCapacityDoesNotHealRemainingDamage()
    {
        _totals[PlayerPerkKind.MaxHealth] = 25f;
        Invoke(_perks, "ApplyTotals");
        _vitals.ApplyDamage(40, null);
        ExpireAdrenaline();

        _totals.Remove(PlayerPerkKind.MaxHealth);
        Invoke(_perks, "ApplyTotals");

        Assert.That(_vitals.MaxHealth, Is.EqualTo(100));
        Assert.That(_vitals.CurrentHealth, Is.EqualTo(85));
        Assert.That(AdrenalineBonus, Is.Zero);
    }

    [Test]
    public void MaximumHealthIncreaseDoesNotReviveADeadPlayer()
    {
        _vitals.ApplyDamage(100, null);
        Assert.That(_vitals.IsDead, Is.True);
        Assert.That(_vitals.CurrentHealth, Is.Zero);

        _totals[PlayerPerkKind.MaxHealth] = 25f;
        Invoke(_perks, "ApplyTotals");

        Assert.That(_vitals.MaxHealth, Is.EqualTo(125));
        Assert.That(_vitals.CurrentHealth, Is.Zero);
        Assert.That(_vitals.IsDead, Is.True);
    }

    [Test]
    public void GenuineDamageTriggersAgainAfterHealingToFull()
    {
        _vitals.ApplyDamage(10, null);
        Assert.That(AdrenalineBonus, Is.EqualTo(0.25f));

        ExpireAdrenaline();
        _vitals.Heal(10);
        Assert.That(_vitals.CurrentHealth, Is.EqualTo(100));
        Assert.That(AdrenalineBonus, Is.Zero);

        _vitals.ApplyDamage(5, null);

        Assert.That(_vitals.CurrentHealth, Is.EqualTo(95));
        Assert.That(AdrenalineBonus, Is.EqualTo(0.25f));
    }

    [Test]
    public void ExpirationDoesNotRearmWhileHealthRemainsBelowMaximum()
    {
        _totals[PlayerPerkKind.MaxHealth] = 25f;
        Invoke(_perks, "ApplyTotals");
        _vitals.ApplyDamage(10, null);
        Assert.That(AdrenalineBonus, Is.EqualTo(0.25f));

        ExpireAdrenaline();
        Invoke(_perks, "ApplyTotals");

        Assert.That(_vitals.CurrentHealth, Is.EqualTo(115));
        Assert.That(_vitals.MaxHealth, Is.EqualTo(125));
        Assert.That(AdrenalineBonus, Is.Zero);
        Assert.That((float)GetField(_perks, "_adrenalineUntil"), Is.LessThan(Time.time));
    }

    [Test]
    public void InitialHealthEventBeforeVitalsInitializationEstablishesBaseline()
    {
        Invoke(_perks, "OnDisable");
        SetField(_vitals, "_effectiveMaxHealth", 0);
        SetField(_vitals, "_currentHealth", 0);
        Invoke(_perks, "OnEnable");

        Invoke(_vitals, "RecalculateDerivedStats", true);

        Assert.That(AdrenalineBonus, Is.Zero);
        _vitals.ApplyDamage(10, null);
        Assert.That(AdrenalineBonus, Is.EqualTo(0.25f));
    }

    [Test]
    public void RestoredHealthBaselineDoesNotTriggerAdrenalineButLaterDamageDoes()
    {
        _totals[PlayerPerkKind.MaxHealth] = 25f;
        Invoke(_perks, "ApplyTotals");
        Assert.That(_vitals.CurrentHealth, Is.EqualTo(125));

        // Match checkpoint ordering: apply the saved HP, establish its baseline, then notify.
        SetField(_vitals, "_currentHealth", 72);
        _perks.ResetHealthSnapshot(_vitals.CurrentHealth, _vitals.MaxHealth);
        Invoke(_vitals, "RaiseHealthChanged");

        Assert.That(_vitals.CurrentHealth, Is.EqualTo(72));
        Assert.That(AdrenalineBonus, Is.Zero);

        _vitals.ApplyDamage(10, null);

        Assert.That(_vitals.CurrentHealth, Is.EqualTo(62));
        Assert.That(AdrenalineBonus, Is.EqualTo(0.25f));
    }

    private float AdrenalineBonus => (float)GetField(_perks, "_adrenalineBonus");

    private void ExpireAdrenaline()
    {
        SetField(_perks, "_adrenalineUntil", Time.time - 1f);
        Invoke(_perks, "TickAdrenaline");
    }

    private static object GetField(object target, string name)
        => target.GetType().GetField(name, PrivateInstance).GetValue(target);

    private static void SetField(object target, string name, object value)
        => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);

    private static void Invoke(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, PrivateInstance).Invoke(target, args);
}
