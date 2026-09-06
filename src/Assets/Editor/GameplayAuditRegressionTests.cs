using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using UnityEngine;

/// <summary>Small isolated fixtures; these tests do not certify multiplayer or player enjoyment.</summary>
public sealed class GameplayAuditRegressionTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<GameObject> _created = new();

    private GameObject Make(string name)
    {
        var go = new GameObject(name);
        _created.Add(go);
        return go;
    }

    [TearDown]
    public void Cleanup()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i] != null) Object.DestroyImmediate(_created[i]);
        _created.Clear();
    }

    [Test]
    public void UncollectedCorpseFreesSpawnSlotButRemainsTrackedForDayCleanup()
    {
        var spawner = Make("AuditSpawner").AddComponent<DungeonMonsterSpawner>();
        var monster = Make("AuditCorpse").AddComponent<MonsterHealth>();
        var tracked = (List<NetworkIdentity>)typeof(DungeonMonsterSpawner)
            .GetField("_alive", PrivateInstance).GetValue(spawner);
        tracked.Add(monster);
        Assert.That(spawner.AliveCount, Is.EqualTo(1));

        // Set only the fixture's replicated dead flag; no network or production player is touched.
        var dead = (SyncVar<bool>)typeof(MonsterHealth).GetField("_netDead", PrivateInstance).GetValue(monster);
        dead.value = true;
        Assert.That(spawner.AliveCount, Is.Zero, "An uncollected corpse must not block a replacement spawn.");
        Assert.That(tracked, Does.Contain(monster), "Day cleanup still needs to despawn the corpse.");
        Assert.That(monster, Is.Not.Null, "Counting must not destroy loot.");
    }

    [Test]
    public void DestroyedMonsterDoesNotConsumeSpawnSlot()
    {
        var spawner = Make("AuditSpawner").AddComponent<DungeonMonsterSpawner>();
        var monster = Make("AuditMonster").AddComponent<MonsterHealth>();
        var tracked = (List<NetworkIdentity>)typeof(DungeonMonsterSpawner)
            .GetField("_alive", PrivateInstance).GetValue(spawner);
        tracked.Add(monster);
        Object.DestroyImmediate(monster.gameObject);
        Assert.That(spawner.AliveCount, Is.Zero);
    }

    [Test]
    public void DefeatedSwarmDoesNotConsumeSpawnSlot()
    {
        var spawner = Make("AuditSpawner").AddComponent<DungeonMonsterSpawner>();
        var go = Make("AuditEmptySwarm");
        go.SetActive(false);
        var swarm = go.AddComponent<OctopusSwarmController>();
        var tracked = (List<NetworkIdentity>)typeof(DungeonMonsterSpawner)
            .GetField("_alive", PrivateInstance).GetValue(spawner);
        tracked.Add(swarm);
        Assert.That(swarm.CurrentIntent, Is.EqualTo(MonsterIntent.Dead));
        Assert.That(spawner.AliveCount, Is.Zero);
        Assert.That(tracked, Does.Contain(swarm));
    }

    [Test]
    public void LootScentPassesRangeGateButDoesNotBypassSightRange()
    {
        Assert.That(PlayerPawn.All.Count, Is.Zero, "Run in isolated Edit Mode, not a live session.");
        var monster = Make("AuditDetector");
        // Keep fixture away from the loaded scene's geometry and the darkness cache's default bucket.
        monster.transform.position = new Vector3(20000f, 20000f, 20000f);
        var range = monster.AddComponent<RangeDetector>();
        var sight = monster.AddComponent<LineOfSightDetector>();
        var player = Make("AuditScentCarrier");
        player.transform.position = monster.transform.position + Vector3.forward * 18f;
        var capsule = player.AddComponent<CapsuleCollider>();
        capsule.center = Vector3.up;
        capsule.height = 2f;
        var pawn = player.AddComponent<PlayerPawn>();
        if (!PlayerPawn.All.Contains(pawn)) PlayerPawn.All.Add(pawn);
        var value = (SyncVar<int>)typeof(PlayerPawn).GetField("_carriedValue", PrivateInstance).GetValue(pawn);
        Physics.SyncTransforms();

        Assert.That(range.UpdateDetector(), Is.Null, "18 - isolation 5 exceeds base range 10.");
        value.value = 3000;
        Assert.That(range.UpdateDetector(), Is.EqualTo(player), "18 - isolation 5 - scent 6 enters range 10.");
        Assert.That(sight.PerformDetection(player), Is.Null,
            "Characterization: the separate sight gate still uses its original 10 m limit.");
    }
}
