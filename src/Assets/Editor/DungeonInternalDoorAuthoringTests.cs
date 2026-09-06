using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class DungeonInternalDoorAuthoringTests
{
    [Test]
    public void AdministrativePrefabsHaveExactlyEighteenExplicitInternalPortals()
    {
        string report = DungeonInternalDoorAuthoring.ValidateAdministrativeInternalDoorsCli();
        Assert.That(report, Does.StartWith("PASS"), report);
    }

    [Test]
    public void BindingResolvesOnlyFromItsAuthoredPassageCollider()
    {
        var root = new GameObject("InternalDoorTest");
        try
        {
            BoxCollider collider = root.AddComponent<BoxCollider>();
            Door door = root.AddComponent<Door>();
            MonsterDoorLinkBinding binding = root.AddComponent<MonsterDoorLinkBinding>();
            binding.ConfigureContinuousDoor(door, null, new Collider[] { collider });

            Assert.That(MonsterDoorLinkBinding.TryResolve(collider, out MonsterDoorLinkBinding resolved), Is.True);
            Assert.That(resolved, Is.SameAs(binding));
            Assert.That(resolved.RegularDoor, Is.SameAs(door));
            Assert.That(resolved.PassageColliders, Does.Contain(collider));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void DoorNetworkKeyDoesNotChangeWhenDoorRotatesOpen()
    {
        var root = new GameObject("StableDoorKeyTest");
        try
        {
            root.AddComponent<BoxCollider>();
            Door door = root.AddComponent<Door>();
            root.transform.SetPositionAndRotation(
                new Vector3(12.34f, -2f, 56.78f),
                Quaternion.Euler(0f, 35f, 0f));

            string closedKey = door.StableNetworkKey;
            root.transform.rotation = Quaternion.Euler(0f, 125f, 0f);

            Assert.That(door.StableNetworkKey, Is.EqualTo(closedKey));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void DoorPassageRequiresPathToContinueThroughOppositeSide()
    {
        var root = new GameObject("DoorPathIntentTest");
        try
        {
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.2f, 2.4f, 0.1f);
            Door door = root.AddComponent<Door>();
            MonsterDoorLinkBinding binding = root.AddComponent<MonsterDoorLinkBinding>();
            binding.ConfigureContinuousDoor(door, null, new Collider[] { collider });

            Vector3[] through = { new Vector3(0f, 0f, -2f), new Vector3(0f, 0f, 2f) };
            Vector3[] stopsBeforeDoor = { new Vector3(0f, 0f, -2f), new Vector3(0f, 0f, -0.2f) };
            Vector3[] crossesOutsideOpening = { new Vector3(2f, 0f, -2f), new Vector3(2f, 0f, 2f) };
            Vector3[] runsAlongDoor = { new Vector3(-2f, 0f, -0.3f), new Vector3(2f, 0f, -0.3f) };

            Assert.That(binding.IsPassageOnPath(through, through.Length, 0.08f, 5f, 0.25f), Is.True);
            Assert.That(binding.IsPassageOnPath(stopsBeforeDoor, stopsBeforeDoor.Length, 0.08f, 5f, 0.25f), Is.False);
            Assert.That(binding.IsPassageOnPath(crossesOutsideOpening, crossesOutsideOpening.Length, 0.08f, 5f, 0.25f), Is.False);
            Assert.That(binding.IsPassageOnPath(runsAlongDoor, runsAlongDoor.Length, 0.08f, 5f, 0.25f), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
