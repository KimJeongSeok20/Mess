using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class RpgProjectileProductionTests
{
    private const string ExplosionPrefabPath =
        "Assets/Items/Weapon/WeaponData/RPG_Explosion.prefab";

    private static readonly MethodInfo ApplyExplosionDamage = typeof(NetworkProjectile).GetMethod(
        "ApplyExplosionDamage",
        BindingFlags.Static | BindingFlags.NonPublic);

    [Test]
    public void ExplosionPrefabIsOneShotAndHasValidUrpMaterials()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ExplosionPrefabPath);
        Assert.That(prefab, Is.Not.Null, $"Missing RPG explosion prefab: {ExplosionPrefabPath}");

        ParticleSystem[] particleSystems = prefab.GetComponentsInChildren<ParticleSystem>(true);
        Assert.That(particleSystems, Has.Length.EqualTo(8));

        int embersCount = 0;
        foreach (ParticleSystem particleSystem in particleSystems)
        {
            ParticleSystem.MainModule main = particleSystem.main;
            Assert.That(main.loop, Is.False, $"{particleSystem.name} must be a one-shot effect.");
            Assert.That(main.prewarm, Is.False, $"{particleSystem.name} must not prewarm on spawn.");

            ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer == null || renderer.renderMode == ParticleSystemRenderMode.None)
                continue;

            Assert.That(renderer.sharedMaterial, Is.Not.Null, $"{particleSystem.name} has no material.");
            Assert.That(renderer.sharedMaterial.shader, Is.Not.Null, $"{particleSystem.name} has no shader.");
            Assert.That(renderer.sharedMaterial.shader.isSupported, Is.True,
                $"{particleSystem.name} uses an unsupported shader.");

            if (particleSystem.name == "Embers" || particleSystem.name == "Embers (1)")
                embersCount++;
        }

        Assert.That(embersCount, Is.EqualTo(2));
    }

    [Test]
    public void ExplosionFalloffDoesNotCallClosestPointOnNonConvexMeshCollider()
    {
        Assert.That(ApplyExplosionDamage, Is.Not.Null);

        GameObject target = null;
        Mesh mesh = null;
        try
        {
            target = new GameObject("RpgNonConvexMeshColliderTest");
            mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-1f, 0f, -1f),
                    new Vector3(1f, 0f, -1f),
                    new Vector3(0f, 0f, 1f)
                },
                triangles = new[] { 0, 1, 2 }
            };
            mesh.RecalculateBounds();

            MeshCollider collider = target.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false;
            Physics.SyncTransforms();

            int damagedTargets = (int)ApplyExplosionDamage.Invoke(
                null,
                new object[] { Vector3.zero, 5f, 150, (LayerMask)(1 << target.layer) });

            Assert.That(damagedTargets, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
        finally
        {
            if (target != null)
                Object.DestroyImmediate(target);
            if (mesh != null)
                Object.DestroyImmediate(mesh);
        }
    }
}
