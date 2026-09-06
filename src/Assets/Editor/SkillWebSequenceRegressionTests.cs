using System;
using System.Collections.Generic;
using System.Reflection;
using Esper.SkillWeb;
using Esper.SkillWeb.Graph;
using Esper.SkillWeb.Settings;
using NUnit.Framework;
using UnityEngine;
using SkillWebRuntime = Esper.SkillWeb.SkillWeb;
using Object = UnityEngine.Object;

public sealed class SkillWebSequenceRegressionTests
{
    private static readonly FieldInfo SettingsField = typeof(SkillWebRuntime).GetField(
        "settings", BindingFlags.Static | BindingFlags.NonPublic);

    private readonly List<Object> _created = new();
    private object _previousSettings;
    private int _previousPoints;
    private int _previousPlayerLevel;
    private Func<SkillNode, bool> _previousCanUpgrade;
    private Func<SkillNode, bool> _previousCanDowngrade;
    private Func<int> _previousPlayerLevelGetter;
    private Web _web;

    [SetUp]
    public void SetUp()
    {
        _previousSettings = SettingsField.GetValue(null);
        _previousPoints = SkillWebRuntime.skillPoints;
        _previousPlayerLevel = SkillWebRuntime.playerLevel;
        _previousCanUpgrade = SkillNode.canUpgrade;
        _previousCanDowngrade = SkillNode.canDowngrade;
        _previousPlayerLevelGetter = SkillNode.playerLevelGetter;

        var settings = Create<SkillWebSettings>();
        settings.enableDowngrading = true;
        settings.enablePlayerLevelRequirement = true;
        SettingsField.SetValue(null, settings);
        SkillWebRuntime.skillPoints = 8;
        SkillWebRuntime.playerLevel = 1;
        SkillNode.canUpgrade = _ => true;
        SkillNode.canDowngrade = _ => true;
        SkillNode.playerLevelGetter = () => SkillWebRuntime.playerLevel;

        var graph = Create<WebGraph>();
        graph.skillNodes.Add(CreateNode(0, "Health Boost I", false));
        graph.skillNodes.Add(CreateNode(1, "Health Boost II", true));
        graph.skillNodes.Add(CreateNode(2, "Iron Lungs", true));
        graph.skillNodes.Add(CreateNode(3, "Fleet Foot I", true));
        graph.connections.Add(new Connection(0, 1, 0, 0, 0));
        graph.connections.Add(new Connection(1, 2, 0, 0, 0));
        graph.connections.Add(new Connection(0, 3, 0, 0, 0));
        _web = new Web(graph);
    }

    [TearDown]
    public void TearDown()
    {
        SettingsField.SetValue(null, _previousSettings);
        SkillWebRuntime.skillPoints = _previousPoints;
        SkillWebRuntime.playerLevel = _previousPlayerLevel;
        SkillNode.canUpgrade = _previousCanUpgrade;
        SkillNode.canDowngrade = _previousCanDowngrade;
        SkillNode.playerLevelGetter = _previousPlayerLevelGetter;

        foreach (Object created in _created)
            Object.DestroyImmediate(created);
        _created.Clear();
    }

    [Test]
    public void FreshGraphRequiresRootThenThePreviousNodeOfEachBranch()
    {
        Assert.That(Node(0).IsLocked, Is.False);
        Assert.That(Node(1).TryUpgrade(), Is.False);
        Assert.That(Node(2).TryUpgrade(), Is.False);
        Assert.That(Node(3).TryUpgrade(), Is.False);
        Assert.That(SkillWebRuntime.skillPoints, Is.EqualTo(8));

        Assert.That(Node(0).TryUpgrade(), Is.True);
        Assert.That(Node(1).IsLocked, Is.False);
        Assert.That(Node(3).IsLocked, Is.False);
        Assert.That(Node(2).IsLocked, Is.True);
        Assert.That(Node(3).TryUpgrade(), Is.True);
        Assert.That(Node(2).TryUpgrade(), Is.False, "Buying another branch must not unlock Iron Lungs.");

        Assert.That(Node(1).TryUpgrade(), Is.True);
        Assert.That(Node(2).TryUpgrade(), Is.True);
        Assert.That(Node(2).IsObtained, Is.True);
        Assert.That(SkillWebRuntime.skillPoints, Is.EqualTo(4));
    }

    [Test]
    public void RefundingPredecessorRelocksAndRefundsItsDescendantOnly()
    {
        Assert.That(Node(0).TryUpgrade(), Is.True);
        Assert.That(Node(1).TryUpgrade(), Is.True);
        Assert.That(Node(2).TryUpgrade(), Is.True);
        Assert.That(Node(3).TryUpgrade(), Is.True);

        Assert.That(Node(1).TryDowngrade(), Is.True);

        Assert.That(Node(1).Level, Is.Zero);
        Assert.That(Node(2).Level, Is.Zero);
        Assert.That(Node(2).IsLocked, Is.True);
        Assert.That(Node(2).TryUpgrade(), Is.False);
        Assert.That(Node(0).IsObtained, Is.True);
        Assert.That(Node(3).IsObtained, Is.True);
        Assert.That(SkillWebRuntime.skillPoints, Is.EqualTo(6));
    }

    [Test]
    public void CachedUnlockedStateCannotBypassAnUnpurchasedPredecessor()
    {
        Node(2).state = Skill.State.Unlocked;
        Assert.That(Node(2).UnlockRequirementsMet(), Is.False);

        Assert.That(Node(2).TryUpgrade(), Is.False);

        Assert.That(Node(2).Level, Is.Zero);
        Assert.That(SkillWebRuntime.skillPoints, Is.EqualTo(8));
        Assert.That(_web.HasUnsavedChanges, Is.False);
    }

    [Test]
    public void CustomUpgradeVetoStillAppliesWhenPrerequisitesAreMet()
    {
        SkillNode.canUpgrade = _ => false;
        Assert.That(Node(0).UnlockRequirementsMet(), Is.True);
        Assert.That(Node(0).TryUpgrade(), Is.False);
        Assert.That(SkillWebRuntime.skillPoints, Is.EqualTo(8));

        SkillNode.canUpgrade = _ => true;
        Assert.That(Node(0).TryUpgrade(), Is.True);
    }

    private SkillNode Node(int id) => _web.skillNodes[id];

    private SkillNode CreateNode(int id, string name, bool requiresPredecessor)
    {
        var skill = Create<Skill>();
        skill.id = 900000 + id;
        skill.skillName = name;
        skill.maxLevel = 1;
        return new SkillNode(id, skill, Vector2.zero)
        {
            hasConnectionDependency = requiresPredecessor,
            dependencyCount = 1,
            maxedRequirementCount = 0
        };
    }

    private T Create<T>() where T : ScriptableObject
    {
        T created = ScriptableObject.CreateInstance<T>();
        created.hideFlags = HideFlags.HideAndDontSave;
        _created.Add(created);
        return created;
    }
}
