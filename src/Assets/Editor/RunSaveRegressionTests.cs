using System;
using System.IO;
using NUnit.Framework;

public sealed class RunSaveRegressionTests
{
    private string _path;

    [SetUp]
    public void Setup() => _path = Path.Combine(Path.GetTempPath(), "StillWorking-checkpoint-test-" + Guid.NewGuid().ToString("N") + ".json");

    [TearDown]
    public void Cleanup()
    {
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp");
    }

    private static RunSaveData Example() => new()
    {
        savedUtc = DateTime.UtcNow.ToString("O"), day = 7, currency = 12000, unlockedCells = 8,
        health = 72, stamina = 46.5f,
        dueThroughCycle = 2, paidThroughCycle = 2, civicRank = 4, pendingDiscount = 0.2f,
        skillPointsEarned = 9, skillPoints = 3, skillWebName = "Company Perks",
        contract = new RunContractSave { templateIndex = 1, lastTemplateIndex = 1, target = 3, progress = 3, day = 7, state = 2 },
        skills = new[] { new RunSkillSave { nodeId = 17, guid = "authored-node-guid", level = 2 } },
        inventory = new[] { new RunInventorySave { slot = 6, receipt = new InventoryReceipt
        { token = "unique-server-receipt", itemName = "Rifle", weaponDataName = "RifleData", price = 950, rarity = 4, upgradeTier = 3, ammo = 11, category = 0 } } }
    };

    [Test]
    public void RoundtripPreservesSpentSkillPointsTaxDiscountAndUpgradedWeaponAmmo()
    {
        Assert.That(RunSaveStorage.TryWrite(_path, Example(), out var error), Is.True, error);
        Assert.That(RunSaveStorage.TryRead(_path, out var loaded, out error), Is.True, error);
        Assert.That(loaded.skillPoints, Is.EqualTo(3));
        Assert.That(loaded.health, Is.EqualTo(72));
        Assert.That(loaded.stamina, Is.EqualTo(46.5f));
        Assert.That(loaded.skillPointsEarned, Is.EqualTo(9));
        Assert.That(loaded.skills[0].level, Is.EqualTo(2));
        Assert.That(loaded.paidThroughCycle, Is.EqualTo(2));
        Assert.That(loaded.pendingDiscount, Is.EqualTo(0.2f));
        Assert.That(loaded.contract.state, Is.EqualTo(2));
        Assert.That(loaded.inventory[0].slot, Is.EqualTo(6));
        Assert.That(loaded.inventory[0].receipt.upgradeTier, Is.EqualTo(3));
        Assert.That(loaded.inventory[0].receipt.ammo, Is.EqualTo(11));
    }

    [Test]
    public void CorruptCheckpointIsRejectedWithoutReturningPartialData()
    {
        Assert.That(RunSaveStorage.TryWrite(_path, Example(), out _), Is.True);
        string contents = File.ReadAllText(_path);
        File.WriteAllText(_path, contents.Replace("12000", "90000"));
        Assert.That(RunSaveStorage.TryRead(_path, out var loaded, out var error), Is.False);
        Assert.That(loaded, Is.Null);
        Assert.That(error, Does.Contain("corrupt"));
    }

    [Test]
    public void InvalidReplacementLeavesPreviousValidCheckpointUntouched()
    {
        Assert.That(RunSaveStorage.TryWrite(_path, Example(), out _), Is.True);
        string before = File.ReadAllText(_path);
        var invalid = Example();
        invalid.version = RunSaveData.CurrentVersion + 1;
        Assert.That(RunSaveStorage.TryWrite(_path, invalid, out _), Is.False);
        Assert.That(File.ReadAllText(_path), Is.EqualTo(before));
    }

    [Test]
    public void SuccessfulReplacementPublishesOnlyTheNewCompleteCheckpoint()
    {
        Assert.That(RunSaveStorage.TryWrite(_path, Example(), out _), Is.True);
        var next = Example();
        next.currency = 123;
        Assert.That(RunSaveStorage.TryWrite(_path, next, out var error), Is.True, error);
        Assert.That(RunSaveStorage.TryRead(_path, out var loaded, out error), Is.True, error);
        Assert.That(loaded.currency, Is.EqualTo(123));
        Assert.That(File.Exists(_path + ".tmp"), Is.False);
    }

    [Test]
    public void DuplicateReceiptsAndLockedSlotsCannotLoadAsExtraItems()
    {
        var duplicate = Example();
        duplicate.inventory = new[] { duplicate.inventory[0], duplicate.inventory[0] };
        Assert.That(duplicate.Validate(out _), Is.False);
        var locked = Example();
        locked.inventory[0].slot = 4 + locked.unlockedCells;
        Assert.That(locked.Validate(out _), Is.False);
    }
}
