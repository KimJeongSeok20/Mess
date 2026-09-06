using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public sealed class OctopusSwarmProductionTests
{
    private const string ProductionPrefabPath = "Assets/Monster/Octopus/OctopusSwarm.prefab";
    private const string NetworkPrefabsPath = "Assets/Prefabs/DoorNextToDungeonPrefab.asset";
    private static readonly MethodInfo ApplyExplosionDamage = typeof(NetworkProjectile).GetMethod(
        "ApplyExplosionDamage",
        BindingFlags.Static | BindingFlags.NonPublic);

    [Test]
    public void ProductionPrefabHasSevenIndependentNetworkedOneHpMembers()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProductionPrefabPath);
        Assert.That(prefab, Is.Not.Null, $"Missing production prefab: {ProductionPrefabPath}");
        OctopusSwarmController controller = prefab.GetComponent<OctopusSwarmController>();
        Assert.That(controller, Is.Not.Null);
        Assert.That(prefab.GetComponent<MonsterDoorTraversalGroup>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<MonsterHealth>(), Is.Null,
            "A root MonsterHealth would collapse all member hits into shared health.");

        NetworkPrefabs networkPrefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabs>(NetworkPrefabsPath);
        Assert.That(networkPrefabs, Is.Not.Null);
        Assert.That(networkPrefabs.prefabs.Exists(entry => entry.prefab == prefab), Is.True,
            "The server can instantiate the swarm, but clients need it registered in PurrNet NetworkPrefabs.");

        OctopusSwarmMember[] members = prefab.GetComponentsInChildren<OctopusSwarmMember>(true);
        Assert.That(members, Has.Length.EqualTo(7));

        int monsterLayer = LayerMask.NameToLayer("Monster");
        int playerLayer = LayerMask.NameToLayer("Player");
        Assert.That(playerLayer, Is.GreaterThanOrEqualTo(0), "Player physics layer is required.");
        HashSet<int> stableIds = new();
        for (int i = 0; i < members.Length; i++)
        {
            OctopusSwarmMember member = members[i];
            Assert.That(member.MaxHealth, Is.EqualTo(1), member.name);
            Assert.That(member.AttackDamage, Is.EqualTo(1), member.name);
            Assert.That(stableIds.Add(member.StableMemberId), Is.True,
                $"Duplicate stable member id {member.StableMemberId}");
            Assert.That(member.DamageCollider, Is.Not.Null, member.name);
            Assert.That(member.DamageCollider.enabled, Is.True, member.name);
            Assert.That(member.DamageCollider.isTrigger, Is.False, member.name);
            Assert.That(member.DamageCollider.gameObject.layer, Is.EqualTo(monsterLayer), member.name);
            Assert.That(
                member.DamageCollider.excludeLayers.value & (1 << playerLayer),
                Is.Not.Zero,
                $"{member.name}: body hurtbox must not physically contact Player colliders");
            Assert.That(member.GetComponent<NetworkTransform>(), Is.Not.Null, member.name);
            Assert.That(member.GetComponent<NetworkAnimator>(), Is.Not.Null, member.name);
            Assert.That(member.GetComponent<DoorAutoOpener>(), Is.Not.Null, member.name);

            SerializedObject movementSettings = new SerializedObject(member);
            float returnEnter = movementSettings.FindProperty("slotReturnEnterDistance").floatValue;
            float returnExit = movementSettings.FindProperty("slotReturnExitDistance").floatValue;
            float patrolSpeedMultiplier = movementSettings.FindProperty("patrolSpeedMultiplier").floatValue;
            Assert.That(returnEnter, Is.GreaterThan(returnExit), $"{member.name}: soft-slot hysteresis");
            Assert.That(patrolSpeedMultiplier, Is.InRange(0.1f, 0.99f), $"{member.name}: patrol speed");
            Assert.That(
                movementSettings.FindProperty("formationDestinationRefreshDistance").floatValue,
                Is.GreaterThanOrEqualTo(0.5f),
                $"{member.name}: relaxed formation refresh threshold");

            NavMeshAgent agent = member.GetComponent<NavMeshAgent>();
            Assert.That(agent, Is.Not.Null, member.name);
            Assert.That(agent.agentTypeID, Is.EqualTo(ClownNavMeshConfig.AgentTypeId), member.name);
            Assert.That(agent.autoTraverseOffMeshLink, Is.False, member.name);
        }
    }

    [Test]
    public void RemovingMemberKeepsSurvivorsInTheirStableFormationSectors()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProductionPrefabPath);
        Assert.That(prefab, Is.Not.Null, $"Missing production prefab: {ProductionPrefabPath}");
        GameObject instance = null;
        try
        {
            instance = Object.Instantiate(prefab);
            OctopusSwarmController controller = instance.GetComponent<OctopusSwarmController>();
            controller.RebuildMembers();

            OctopusSwarmMember[] members = instance.GetComponentsInChildren<OctopusSwarmMember>(true);
            Assert.That(members, Has.Length.EqualTo(7));
            for (int i = 0; i < members.Length; i++)
                Assert.That(members[i].MemberIndex, Is.EqualTo(members[i].StableMemberId), members[i].name);

            OctopusSwarmMember removed = members[3];
            controller.Unregister(removed);

            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] == removed)
                    continue;

                Assert.That(members[i].MemberIndex, Is.EqualTo(members[i].StableMemberId),
                    $"{members[i].name} should not rush into another member's slot after a death.");
            }
        }
        finally
        {
            if (instance != null)
                Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void PositiveDamageKillsOnlyTheHitMemberAndIsIdempotent()
    {
        GameObject first = null;
        GameObject second = null;
        try
        {
            OctopusSwarmMember firstMember = CreateMember("Octopus_A", Vector3.zero, out first);
            OctopusSwarmMember secondMember = CreateMember("Octopus_B", Vector3.right * 2f, out second);

            firstMember.TakeDamage(1);
            firstMember.TakeDamage(25);

            Assert.That(firstMember.IsDead, Is.True);
            Assert.That(firstMember.CurrentHealth, Is.Zero);
            Assert.That(firstMember.DamageCollider.enabled, Is.False);
            Assert.That(secondMember.IsDead, Is.False);
            Assert.That(secondMember.CurrentHealth, Is.EqualTo(1));
        }
        finally
        {
            if (first != null)
                Object.DestroyImmediate(first);
            if (second != null)
                Object.DestroyImmediate(second);
        }
    }

    [Test]
    public void RpgExplosionKillsMultipleMembersOncePerReceiverAndLeavesOutsideMemberAlive()
    {
        GameObject near = null;
        GameObject middle = null;
        GameObject outside = null;
        try
        {
            OctopusSwarmMember nearMember = CreateMember("Octopus_Near", Vector3.zero, out near);
            OctopusSwarmMember middleMember = CreateMember("Octopus_Middle", Vector3.right * 2f, out middle);
            OctopusSwarmMember outsideMember = CreateMember("Octopus_Outside", Vector3.right * 7f, out outside);

            GameObject duplicateHurtbox = new GameObject("DuplicateHurtbox");
            duplicateHurtbox.layer = near.layer;
            duplicateHurtbox.transform.SetParent(near.transform, false);
            duplicateHurtbox.AddComponent<SphereCollider>().radius = 0.25f;

            Physics.SyncTransforms();
            Assert.That(ApplyExplosionDamage, Is.Not.Null);
            int monsterLayer = LayerMask.NameToLayer("Monster");
            int damagedTargets = (int)ApplyExplosionDamage.Invoke(
                null,
                new object[] { Vector3.zero, 5f, 150, (LayerMask)(1 << monsterLayer) });

            Assert.That(damagedTargets, Is.EqualTo(2), "RPG damage must be deduplicated per member, not per collider.");
            Assert.That(nearMember.IsDead, Is.True);
            Assert.That(middleMember.IsDead, Is.True);
            Assert.That(outsideMember.IsDead, Is.False);
        }
        finally
        {
            if (near != null)
                Object.DestroyImmediate(near);
            if (middle != null)
                Object.DestroyImmediate(middle);
            if (outside != null)
                Object.DestroyImmediate(outside);
        }
    }

    private static OctopusSwarmMember CreateMember(string name, Vector3 position, out GameObject gameObject)
    {
        gameObject = new GameObject(name);
        gameObject.transform.position = position;
        int monsterLayer = LayerMask.NameToLayer("Monster");
        if (monsterLayer >= 0)
            gameObject.layer = monsterLayer;

        CapsuleCollider collider = gameObject.AddComponent<CapsuleCollider>();
        collider.center = Vector3.up * 0.5f;
        collider.height = 1f;
        collider.radius = 0.3f;
        collider.isTrigger = false;
        return gameObject.AddComponent<OctopusSwarmMember>();
    }
}
