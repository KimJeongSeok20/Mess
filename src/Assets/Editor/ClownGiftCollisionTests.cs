using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class ClownGiftCollisionTests
{
    private static readonly MethodInfo ExcludePlayerPhysicalCollisions = typeof(ClownGift).GetMethod(
        "ExcludePlayerPhysicalCollisions",
        BindingFlags.Instance | BindingFlags.NonPublic);

    [Test]
    public void GiftColliderExcludesPlayerPhysicsAndPreservesExistingExclusions()
    {
        int playerLayer = LayerMask.NameToLayer("Player");
        int monsterLayer = LayerMask.NameToLayer("Monster");
        Assert.That(playerLayer, Is.GreaterThanOrEqualTo(0), "Player physics layer is required.");
        Assert.That(monsterLayer, Is.GreaterThanOrEqualTo(0), "Monster physics layer is required.");
        Assert.That(ExcludePlayerPhysicalCollisions, Is.Not.Null);

        GameObject giftObject = null;
        try
        {
            giftObject = new GameObject("ClownGift_CollisionTest");
            giftObject.AddComponent<Rigidbody>();
            BoxCollider collider = giftObject.AddComponent<BoxCollider>();
            collider.excludeLayers = 1 << monsterLayer;
            ClownGift gift = giftObject.AddComponent<ClownGift>();

            ExcludePlayerPhysicalCollisions.Invoke(gift, null);

            int exclusions = collider.excludeLayers.value;
            Assert.That(exclusions & (1 << playerLayer), Is.Not.Zero,
                "Thrown gifts must not physically contact or push Player colliders.");
            Assert.That(exclusions & (1 << monsterLayer), Is.Not.Zero,
                "Adding the Player exclusion must preserve existing collider exclusions.");
            Assert.That(collider.isTrigger, Is.False,
                "The gift must keep solid world collision for floors and walls.");
        }
        finally
        {
            if (giftObject != null)
                Object.DestroyImmediate(giftObject);
        }
    }
}
