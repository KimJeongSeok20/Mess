using System.Linq;
using System.Reflection;
using NUnit.Framework;

public sealed class ServerInventoryTransactionTests
{
    private static InventoryReceipt Trophy(string token, int rarity = 2, int price = 360)
        => new() { token = token, itemName = "Smily", price = price, rarity = rarity, category = (int)ItemCategory.Corpse };

    [Test]
    public void SaleThenDropCannotReuseConsumedItem()
    {
        var inventory = new ServerInventoryLedger();
        inventory.Grant(Trophy("one"));
        Assert.That(inventory.Consume("one", out var sold), Is.True);
        Assert.That(sold.price, Is.EqualTo(360));
        Assert.That(inventory.Consume("one", out _), Is.False);
        Assert.That(inventory.Count, Is.Zero);
    }

    [Test]
    public void AnotherPlayersTokenDoesNotAuthorizeConsumption()
    {
        var owner = new ServerInventoryLedger();
        var stranger = new ServerInventoryLedger();
        owner.Grant(Trophy("owner-item"));
        Assert.That(stranger.Consume("owner-item", out _), Is.False);
        Assert.That(owner.Count, Is.EqualTo(1));
    }

    [Test]
    public void ItemNamesAndMissingTokensAreNotOwnershipProof()
    {
        var inventory = new ServerInventoryLedger();
        inventory.Grant(Trophy("actual-token"));
        foreach (var token in new[] { null, "", "Smily", "Octopus", "Jerrycan", "invented" })
            Assert.That(inventory.Consume(token, out _), Is.False);
        Assert.That(inventory.Count, Is.EqualTo(1));
    }

    [Test]
    public void DropAndRepickupPreserveTheServersGradeAndPrice()
    {
        var first = new ServerInventoryLedger();
        var second = new ServerInventoryLedger();
        first.Grant(Trophy("picked-up", 2, 840));
        first.TryGet("picked-up", out var clientCopy);
        clientCopy.price = 99999;
        clientCopy.rarity = 4;
        Assert.That(first.Consume("picked-up", out var dropped), Is.True);
        dropped.token = "repicked";
        second.Grant(dropped);
        second.TryGet("repicked", out var result);
        Assert.That(result.price, Is.EqualTo(840));
        Assert.That(result.rarity, Is.EqualTo(2));
    }

    [Test]
    public void DuplicateGrantDoesNotOverwriteExistingValue()
    {
        var inventory = new ServerInventoryLedger();
        inventory.Grant(Trophy("same", 0, 100));
        Assert.That(inventory.Grant(Trophy("same", 4, 99999)), Is.False);
        inventory.TryGet("same", out var result);
        Assert.That(result.price, Is.EqualTo(100));
    }

    [Test]
    public void UpgradeMaterialMustBeADistinctOwnedItem()
    {
        var inventory = new ServerInventoryLedger();
        inventory.Grant(Trophy("main"));
        Assert.That(inventory.Replace("main", "main", Trophy("next")), Is.False);
        Assert.That(inventory.Replace("main", "missing", Trophy("next")), Is.False);
        Assert.That(inventory.Count, Is.EqualTo(1));
        Assert.That(inventory.TryGet("main", out _), Is.True);
    }

    [Test]
    public void AFailedRollAlsoRotatesTheTokenSoReplayCannotChargeAgain()
    {
        var inventory = new ServerInventoryLedger();
        var weapon = new InventoryReceipt { token = "main", itemName = "AK", upgradeTier = 7, ammo = 13 };
        inventory.Grant(weapon);
        inventory.Grant(new InventoryReceipt { token = "material", itemName = "AK" });
        weapon.token = "resolved";
        Assert.That(inventory.Replace("main", "material", weapon), Is.True);
        Assert.That(inventory.Replace("main", "material", Trophy("replayed")), Is.False);
        Assert.That(inventory.Count, Is.EqualTo(1));
        inventory.TryGet("resolved", out var result);
        Assert.That(result.upgradeTier, Is.EqualTo(7));
        Assert.That(result.ammo, Is.EqualTo(13));
    }

    [Test]
    public void ShotsCannotIncreaseAmmoButReloadConsumesTheMatchingAmmoItem()
    {
        var inventory = new ServerInventoryLedger();
        inventory.Grant(new InventoryReceipt { token = "gun", itemName = "AK", ammo = 5 });
        inventory.Grant(new InventoryReceipt { token = "ammo", itemName = "RifleAmmo" });
        Assert.That(inventory.ReduceAmmo("gun", 30), Is.False);
        Assert.That(inventory.ReduceAmmo("gun", -1), Is.False);
        Assert.That(inventory.ReduceAmmo("gun", 2), Is.True);
        Assert.That(inventory.Reload("gun", "ammo", "WrongAmmo", 30), Is.False);
        Assert.That(inventory.Reload("gun", "missing", "RifleAmmo", 30), Is.False);
        Assert.That(inventory.Reload("gun", "ammo", "RifleAmmo", 30), Is.True);
        Assert.That(inventory.Reload("gun", "ammo", "RifleAmmo", 30), Is.False);
        inventory.TryGet("gun", out var result);
        Assert.That(result.ammo, Is.EqualTo(30));
        Assert.That(inventory.Count, Is.EqualTo(1));
    }

    [Test]
    public void ResetInvalidatesAllOldReceipts()
    {
        var inventory = new ServerInventoryLedger();
        inventory.Grant(Trophy("old"));
        var snapshot = inventory.Snapshot();
        inventory.Clear();
        Assert.That(inventory.Consume(snapshot[0].token, out _), Is.False);
    }

    [Test]
    public void RemovedTrustBasedRpcEntryPointsStayRemoved()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        Assert.That(typeof(NetworkPlayer).GetMethod("RequestSpawnDroppedItemServerRpc", flags), Is.Null);
        Assert.That(typeof(CurrencyManager).GetMethod("AddCurrencyServerRpc", flags), Is.Null);
        Assert.That(typeof(CurrencyManager).GetMethod("SpendCurrencyServerRpc", flags), Is.Null);
        Assert.That(typeof(PlayerVitals).GetMethod("RequestReviveServerRpc", flags), Is.Null);
        Assert.That(typeof(Item).GetMethod("RequestDespawnAfterPickupServerRpc", flags), Is.Null);
        Assert.That(typeof(Item).GetMethod("SetPrice", flags).GetCustomAttributes(false)
            .Any(a => a.GetType().Name.Contains("ServerRpc")), Is.False);
    }
}
