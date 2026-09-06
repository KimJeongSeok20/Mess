using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using PurrNet;
using GoreSimulatorComponent = PampelGames.GoreSimulator.GoreSimulator;

public sealed class CombatDeathContractTests
{
    private const string SmilyPrefab =
        "Assets/Monster/smily/smily-horror-monster_cc_attribution/smily_fixed.prefab";
    private const string ClownPrefab = "Assets/Clown/Prefab/Clown skin2 combined.prefab";
    private const string OctopusPrefab = "Assets/Monster/Octopus/OctopusSwarm.prefab";
    private const string PlayerPrefab = "Assets/FPS/Cyber_Generic.prefab";
    private const string BloodVfxRoot = "Assets/Vefects/Blood VFX URP/";
    private const string MonsterBloodDecalPrefab = "Assets/Combat/Death/MonsterBloodSplatter.prefab";

    [Test]
    public void DamageContract_UsesExplicitExplosiveType()
    {
        var request = DamageRequest.Explosive(250, new Vector3(1f, 2f, 3f), Vector3.forward);

        Assert.That(request.Amount, Is.EqualTo(250));
        Assert.That(request.DamageType, Is.EqualTo(DamageType.Explosive));
        Assert.That(request.HasHitPoint, Is.True);
        Assert.That(Enum.GetNames(typeof(DamageType)), Is.EquivalentTo(new[]
        {
            nameof(DamageType.Bullet),
            nameof(DamageType.Melee),
            nameof(DamageType.Explosive)
        }));
    }

    [TestCase(SmilyPrefab, "Forearm.L")]
    [TestCase(ClownPrefab, "lowerarm_l")]
    public void LargeMonsterPrefab_HasPartialGoreRagdollAndCorpseCategory(
        string prefabPath,
        string expectedCutBone)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(prefab, Is.Not.Null);

        MonsterHealth health = prefab.GetComponentInChildren<MonsterHealth>(true);
        GoreSimulatorComponent gore = prefab.GetComponentInChildren<GoreSimulatorComponent>(true);
        Item item = prefab.GetComponentInChildren<Item>(true);

        Assert.That(health, Is.Not.Null);
        Assert.That(gore, Is.Not.Null);
        Assert.That(gore.meshCutInitialized, Is.True);
        Assert.That(gore.ragdollInitialized, Is.True);
        Assert.That(item, Is.Not.Null);
        Assert.That(item.Definition, Is.Not.Null);
        Assert.That(item.Definition.category, Is.EqualTo(ItemCategory.Corpse));

        var serializedHealth = new SerializedObject(health);
        Assert.That(serializedHealth.FindProperty("_goreSimulator").objectReferenceValue, Is.SameAs(gore));
        Assert.That(serializedHealth.FindProperty("enableNormalDeathCut").boolValue, Is.False,
            "Bullet and melee deaths must keep the itemized corpse attached.");
        Assert.That(serializedHealth.FindProperty("normalGoreBoneName").stringValue, Is.EqualTo(expectedCutBone));
        GameObject hitVfx = serializedHealth.FindProperty("hitVfxPrefab").objectReferenceValue as GameObject;
        Assert.That(hitVfx, Is.Not.Null);
        Assert.That(AssetDatabase.GetAssetPath(hitVfx), Does.StartWith(BloodVfxRoot));
        Assert.That(hitVfx.name, Is.EqualTo("VFX_Blood_Bullet_Hit_Medium"));
        Assert.That(serializedHealth.FindProperty("hitVfxScale").floatValue, Is.EqualTo(1f).Within(0.001f));
        Assert.That(serializedHealth.FindProperty("hitVfxLifetime").floatValue, Is.GreaterThan(0f));
        Assert.That(serializedHealth.FindProperty("deathVfxPrefab").objectReferenceValue,
            Is.SameAs(LoadBloodBurst("Large")));
        Assert.That(serializedHealth.FindProperty("deathVfxScale").floatValue, Is.EqualTo(1f).Within(0.001f));
        Assert.That(serializedHealth.FindProperty("deathPoolPrefab").objectReferenceValue,
            Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(MonsterBloodDecalPrefab)));
        Assert.That(serializedHealth.FindProperty("gorePartMaxSpeed").floatValue, Is.InRange(1f, 10f));
    }

    [Test]
    public void OctopusPrefab_HasBloodAndTemporaryExplosiveFragments()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OctopusPrefab);
        Assert.That(prefab, Is.Not.Null);

        OctopusSwarmPresentation presentation =
            prefab.GetComponentInChildren<OctopusSwarmPresentation>(true);
        Assert.That(presentation, Is.Not.Null);

        var serializedPresentation = new SerializedObject(presentation);
        Assert.That(serializedPresentation.FindProperty("damagedPrefab").objectReferenceValue,
            Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(BloodVfxRoot
                + "VFX/Performance Versions/Bullet Hit/Once/VFX_Blood_Bullet_Hit_Tiny.prefab")));
        Assert.That(serializedPresentation.FindProperty("damagedVfxScale").floatValue,
            Is.EqualTo(1f).Within(0.001f));
        Assert.That(serializedPresentation.FindProperty("deathPrefab").objectReferenceValue,
            Is.SameAs(LoadBloodBurst("Small")));
        Assert.That(serializedPresentation.FindProperty("deathVfxScale").floatValue,
            Is.EqualTo(1f).Within(0.001f));
        Assert.That(serializedPresentation.FindProperty("deathPoolPrefab").objectReferenceValue,
            Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(MonsterBloodDecalPrefab)));
        Assert.That(serializedPresentation.FindProperty("explosiveDeathPrefab").objectReferenceValue,
            Is.SameAs(LoadBloodBurst("Medium")));
        Assert.That(serializedPresentation.FindProperty("explosiveFragmentPrefab").objectReferenceValue, Is.Not.Null);
        Assert.That(serializedPresentation.FindProperty("explosiveFragmentCount").intValue, Is.GreaterThan(0));
        Assert.That(serializedPresentation.FindProperty("explosiveFragmentLifetime").floatValue, Is.GreaterThan(0f));

        Assert.That(prefab.GetComponentInChildren<Item>(true), Is.Null);
    }

    [Test]
    public void PlayerPrefab_HasInitializedFullGoreDeath()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        Assert.That(prefab, Is.Not.Null);

        PlayerDeath playerDeath = prefab.GetComponent<PlayerDeath>();
        GoreSimulatorComponent gore = prefab.GetComponent<GoreSimulatorComponent>();
        Assert.That(playerDeath, Is.Not.Null);
        Assert.That(gore, Is.Not.Null);
        Assert.That(gore.meshCutInitialized, Is.True);
        Assert.That(AssetDatabase.GetAssetPath(gore.storage),
            Is.EqualTo("Assets/PampelGames/GoreSimulator/Storages/Cyber_Generic_Player.asset"));

        var serializedDeath = new SerializedObject(playerDeath);
        Assert.That(serializedDeath.FindProperty("playerGoreSimulator").objectReferenceValue, Is.SameAs(gore));
        Assert.That(serializedDeath.FindProperty("deathVfxPrefab").objectReferenceValue, Is.Not.Null);
        Assert.That(serializedDeath.FindProperty("deathVfxScale").floatValue, Is.GreaterThanOrEqualTo(2f));
        Assert.That(serializedDeath.FindProperty("deathPoolMaterial").objectReferenceValue, Is.Not.Null);
        Assert.That(serializedDeath.FindProperty("gorePartMaxSpeed").floatValue, Is.InRange(1f, 10f));
    }

    [Test]
    public void CorpseCategory_IsSeparateFromName()
    {
        ItemDefinition definition = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            definition.displayName = "NotNamedCorpse";
            definition.category = ItemCategory.Corpse;
            Assert.That(definition.category, Is.EqualTo(ItemCategory.Corpse));

            definition.displayName = "corpse_fake_name";
            definition.category = ItemCategory.Material;
            Assert.That(definition.category, Is.Not.EqualTo(ItemCategory.Corpse));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(definition);
        }
    }

    [Test]
    public void CorpseServerAuthority_ConsumesOnlyGrantedCorpse()
    {
        const string corpseName = "Smily_Corpse_TestLedger";

        var inventory = new ServerInventoryLedger();
        Assert.That(inventory.Consume("corpse-token", out _), Is.False);
        inventory.Grant(new InventoryReceipt { token = "corpse-token", itemName = corpseName, category = (int)ItemCategory.Corpse });
        Assert.That(inventory.Consume("corpse-token", out _), Is.True);
        Assert.That(inventory.Consume("corpse-token", out _), Is.False);
    }

    [Test]
    public void CorpseRagdoll_NormalizesColliderUnderImportedPointZeroOneScale()
    {
        GameObject bone = new("ImportedScaleRagdollBone");
        try
        {
            bone.transform.localScale = Vector3.one * 0.01f;
            bone.AddComponent<Rigidbody>();
            CapsuleCollider collider = bone.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.radius = 0.18f;
            collider.height = 0.4f;
            Physics.SyncTransforms();
            Assert.That(collider.bounds.extents.magnitude, Is.LessThan(0.01f));

            var normalize = typeof(MonsterHealth).GetMethod(
                "NormalizeUndersizedRagdollCollider",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(normalize, Is.Not.Null);
            normalize.Invoke(null, new object[] { collider });
            Physics.SyncTransforms();

            Assert.That(collider.bounds.extents.magnitude, Is.GreaterThan(0.1f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(bone);
        }
    }

    [UnityTest]
    public IEnumerator SmilyNonfatalHit_PreservesAuthoredBloodAndCleansUpAfterOwnerIsDisabled()
    {
        yield return new EnterPlayMode();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SmilyPrefab);
        Assert.That(prefab, Is.Not.Null);
        GameObject instance = UnityEngine.Object.Instantiate(prefab, Vector3.up * 0.1f, Quaternion.identity);
        instance.name = "Smily_BloodHitRuntimeTest";
        instance.SetActive(true);
        yield return null;

        MonsterHealth health = instance.GetComponentInChildren<MonsterHealth>(true);
        Assert.That(health, Is.Not.Null);
        int originalHealth = health.Health;
        Assert.That(originalHealth, Is.GreaterThan(1));
        var hitPrefabField = typeof(MonsterHealth).GetField("hitVfxPrefab",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var hitScaleField = typeof(MonsterHealth).GetField("hitVfxScale",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.That(hitPrefabField, Is.Not.Null, "MonsterHealth must expose the authored hit VFX reference.");
        Assert.That(hitScaleField, Is.Not.Null, "MonsterHealth must expose the hit VFX scale.");
        GameObject bloodPrefab = hitPrefabField.GetValue(health) as GameObject;
        Assert.That(bloodPrefab, Is.Not.Null);
        Assert.That(bloodPrefab.name, Is.EqualTo("VFX_Blood_Bullet_Hit_Medium"));
        float hitScale = (float)hitScaleField.GetValue(health);
        Assert.That(hitScale, Is.EqualTo(1f).Within(0.001f));
        var previousRoots = new HashSet<GameObject>(UnityEngine.Object
            .FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(particle => particle.transform.root.gameObject));

        Vector3 hitPoint = instance.transform.position + Vector3.up * 1.2f;
        CreateEvidenceGround("SmilyBloodHitRuntimeTest_Ground", Vector3.zero);
        Camera evidenceCamera = CreateEvidenceCamera("SmilyBloodHitEvidenceCamera",
            new Vector3(2.4f, 1.6f, -2.8f), hitPoint);
        health.TakeDamage(DamageRequest.Bullet(1, hitPoint, Vector3.forward));
        Assert.That(health.IsDead, Is.False);
        Assert.That(health.Health, Is.EqualTo(originalHealth - 1));

        // A captured local allocates a closure before EnterPlayMode; the domain reload cannot restore it.
        var newBloodRoots = new List<GameObject>();
        foreach (ParticleSystem particle in UnityEngine.Object.FindObjectsByType<ParticleSystem>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            GameObject root = particle.transform.root.gameObject;
            if (!previousRoots.Contains(root) && root.name.StartsWith(bloodPrefab.name)
                && !newBloodRoots.Contains(root))
                newBloodRoots.Add(root);
        }
        Assert.That(newBloodRoots.Count, Is.EqualTo(1), "One accepted non-fatal hit must emit one blood cue.");
        GameObject blood = newBloodRoots[0];
        AssertAuthoredBlood(blood, bloodPrefab, hitScale);
        Assert.That(Vector3.Distance(blood.transform.position, hitPoint), Is.LessThan(0.05f));
        Assert.That(Vector3.Dot(blood.transform.up, -Vector3.forward), Is.GreaterThan(0.999f),
            "The authored positive-Y blood emission must point back along the incoming hit direction.");
        yield return new WaitForSeconds(0.08f);
        CaptureEvidence(evidenceCamera, "SmilyBloodHit_Runtime.png");

        instance.SetActive(false);
        yield return new WaitForSeconds(0.1f);
        Assert.That(blood != null && blood.activeInHierarchy, Is.True,
            "Disabling a monster must not cut off its detached blood cue.");
        Assert.That(blood.GetComponentsInChildren<ParticleSystem>(true).Any(particle => particle.IsAlive(true)),
            Is.True);

        float cleanupDeadline = Time.time + 15f;
        while (blood != null && Time.time < cleanupDeadline)
            yield return null;
        Assert.That(blood == null, Is.True, "The detached hit cue must clean itself up without its owner.");

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator ClownExplosiveDeath_ShowsBloodAndKeepsFragmentsStable()
    {
        yield return new EnterPlayMode();

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "ClownGoreRuntimeTest_Ground";
        ground.transform.position = new Vector3(-7.3f, -0.1f, 6.5f);
        ground.transform.localScale = new Vector3(12f, 0.2f, 12f);
        Physics.SyncTransforms();
        yield return null;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClownPrefab);
        Assert.That(prefab, Is.Not.Null);

        GameObject instance = UnityEngine.Object.Instantiate(
            prefab,
            new Vector3(-7.3f, 0.15f, 6.5f),
            Quaternion.identity);
        instance.name = "Clown_GoreRuntimeTest";
        instance.SetActive(true);
        yield return null;

        GameObject cameraObject = new("ClownGoreEvidenceCamera");
        Camera evidenceCamera = cameraObject.AddComponent<Camera>();
        evidenceCamera.transform.position = new Vector3(-7.3f, 3.8f, 2.4f);
        evidenceCamera.transform.LookAt(new Vector3(-7.3f, 0.45f, 6.5f));
        evidenceCamera.fieldOfView = 70f;
        evidenceCamera.nearClipPlane = 0.03f;
        evidenceCamera.cullingMask = ~0;

        MonsterHealth health = instance.GetComponentInChildren<MonsterHealth>(true);
        GoreSimulatorComponent gore = instance.GetComponentInChildren<GoreSimulatorComponent>(true);
        Assert.That(health, Is.Not.Null);
        Assert.That(gore, Is.Not.Null);

        Vector3 hitPoint = instance.transform.position + Vector3.up * 1.2f;
        Assert.That(health.Health, Is.GreaterThan(0));
        health.TakeDamage(DamageRequest.Explosive(
            health.MaxHealth,
            hitPoint,
            Vector3.forward));
        Assert.That(health.IsDead, Is.True);
        Assert.That(gore.smr.enabled, Is.False);

        yield return new WaitForSeconds(0.35f);

        CaptureEvidence(evidenceCamera, "ClownGoreRuntimeTest.png");

        ParticleSystem[] bloodParticles = UnityEngine.Object
            .FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(particle => particle != null
                && particle.transform.root.name.Contains("VFX_Blood_Burst_Large"))
            .ToArray();
        Assert.That(bloodParticles.Length, Is.GreaterThanOrEqualTo(3));
        Assert.That(bloodParticles.Any(particle => particle.isPlaying && particle.IsAlive(true)), Is.True);
        foreach (GameObject blood in bloodParticles.Select(particle => particle.transform.root.gameObject).Distinct())
            AssertAuthoredBlood(blood, LoadBloodBurst("Large"), 1.5f);

        BloodPoolVisual bloodPool = UnityEngine.Object.FindObjectsByType<BloodPoolVisual>(FindObjectsSortMode.None)
            .FirstOrDefault(pool => pool.name.StartsWith("MonsterBloodSplatter"));
        Assert.That(bloodPool, Is.Not.Null);
        AssertAuthoredBloodPool(bloodPool.gameObject);

        Rigidbody[] goreBodies = UnityEngine.Object
            .FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)
            .Where(body => body != null
                && body.gameObject.name.StartsWith("Clown_GoreRuntimeTest - "))
            .ToArray();
        Assert.That(goreBodies.Length, Is.GreaterThan(0));
        Assert.That(goreBodies.All(body => body.useGravity && !body.isKinematic), Is.True);
        Assert.That(goreBodies.All(body => body.maxLinearVelocity <= 3.01f), Is.True);
        Assert.That(goreBodies.Max(body => body.linearVelocity.magnitude), Is.LessThanOrEqualTo(3.1f));
        Assert.That(goreBodies.Max(body => body.position.magnitude), Is.LessThan(100f));

        yield return new WaitForSeconds(2.15f);
        CaptureEvidence(evidenceCamera, "ClownGoreRuntimeTest_Settled.png");
        Assert.That(goreBodies.All(body => body.position.y > -2f), Is.True,
            "A gore fragment fell through the test floor.");
        Assert.That(goreBodies.All(body =>
            body.GetComponentsInChildren<BoxCollider>(true).Any(collider => collider.enabled)), Is.True,
            "Every gore fragment must retain an enabled stable BoxCollider.");

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SmilyBulletDeath_KeepsAttachedItemizedCorpseStable()
    {
        yield return new EnterPlayMode();

        GameObject ground = CreateEvidenceGround("SmilyBulletRuntimeTest_Ground", Vector3.zero);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SmilyPrefab);
        Assert.That(prefab, Is.Not.Null);

        GameObject instance = UnityEngine.Object.Instantiate(prefab, Vector3.up * 0.1f, Quaternion.identity);
        instance.name = "Smily_BulletRuntimeTest";
        instance.SetActive(true);
        yield return null;

        Camera evidenceCamera = CreateEvidenceCamera(
            "SmilyBulletEvidenceCamera",
            new Vector3(3f, 2.2f, -3.6f),
            new Vector3(0f, 0.8f, 0f));
        MonsterHealth health = instance.GetComponentInChildren<MonsterHealth>(true);
        GoreSimulatorComponent gore = instance.GetComponentInChildren<GoreSimulatorComponent>(true);
        Item corpseItem = instance.GetComponentInChildren<Item>(true);
        Assert.That(health, Is.Not.Null);
        Assert.That(gore, Is.Not.Null);
        Assert.That(corpseItem, Is.Not.Null);
        health.TakeDamage(DamageRequest.Bullet(
            health.MaxHealth,
            instance.transform.position + Vector3.up * 1.2f,
            Vector3.forward));
        Assert.That(health.IsDead, Is.True);
        yield return new WaitForSeconds(1.8f);

        CaptureEvidence(evidenceCamera, "SmilyBulletDeath_Runtime.png");
        Assert.That(gore.smr.enabled, Is.True);
        Assert.That(gore.GetCreatedObjects().Count, Is.EqualTo(0));
        Assert.That(gore.GetDetachedChildren().Count, Is.EqualTo(0));
        Assert.That(instance.activeInHierarchy, Is.True);
        Assert.That(corpseItem.Definition.category, Is.EqualTo(ItemCategory.Corpse));

        Rigidbody[] corpseBodies = instance.GetComponentsInChildren<Rigidbody>(true);
        Assert.That(corpseBodies.Length, Is.GreaterThan(0));
        Assert.That(corpseBodies.Max(body => body.position.magnitude), Is.LessThan(50f));
        Renderer[] corpseRenderers = instance.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
            .ToArray();
        Bounds corpseBounds = corpseRenderers[0].bounds;
        foreach (Renderer renderer in corpseRenderers.Skip(1))
            corpseBounds.Encapsulate(renderer.bounds);
        Collider[] ragdollColliders = corpseBodies
            .Where(body => !body.isKinematic)
            .SelectMany(body => body.GetComponents<Collider>())
            .Where(collider => collider.enabled)
            .ToArray();
        Bounds contactBounds = ragdollColliders[0].bounds;
        foreach (Collider collider in ragdollColliders.Skip(1))
            contactBounds.Encapsulate(collider.bounds);
        Assert.That(contactBounds.min.y, Is.GreaterThanOrEqualTo(-0.04f),
            "The Smily corpse ragdoll must remain supported by the ground plane.");
        Assert.That(corpseBounds.size.y, Is.LessThan(2.5f),
            "The attached ragdoll must not stretch vertically.");
        Rigidbody[] dynamicCorpseBodies = corpseBodies
            .Where(body => body != null && !body.isKinematic)
            .ToArray();
        Assert.That(dynamicCorpseBodies.Length, Is.GreaterThanOrEqualTo(12));
        Assert.That(dynamicCorpseBodies.Max(body => body.angularVelocity.magnitude),
            Is.GreaterThan(0.2f),
            "The Smily corpse must remain an active articulated ragdoll.");
        Assert.That(UnityEngine.Object.FindObjectsByType<BloodPoolVisual>(FindObjectsSortMode.None).Length,
            Is.GreaterThan(0));

        UnityEngine.Object.Destroy(ground);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SmilyExplosiveDeath_LeavesBloodAndTemporaryFragments()
    {
        yield return new EnterPlayMode();

        CreateEvidenceGround("SmilyExplosiveRuntimeTest_Ground", Vector3.zero);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SmilyPrefab);
        GameObject instance = UnityEngine.Object.Instantiate(prefab, Vector3.up * 0.1f, Quaternion.identity);
        instance.name = "Smily_ExplosiveRuntimeTest";
        instance.SetActive(true);
        yield return null;

        Camera evidenceCamera = CreateEvidenceCamera(
            "SmilyExplosiveEvidenceCamera",
            new Vector3(0f, 2.8f, -5.5f),
            new Vector3(0f, 0.8f, 0f));
        MonsterHealth health = instance.GetComponentInChildren<MonsterHealth>(true);
        GoreSimulatorComponent gore = instance.GetComponentInChildren<GoreSimulatorComponent>(true);
        Assert.That(health, Is.Not.Null);
        Assert.That(gore, Is.Not.Null);

        health.TakeDamage(DamageRequest.Explosive(
            health.MaxHealth,
            instance.transform.position + Vector3.up,
            Vector3.forward));
        yield return new WaitForSeconds(0.4f);

        CaptureEvidence(evidenceCamera, "SmilyExplosiveDeath_Runtime.png");
        Assert.That(health.IsDead, Is.True);
        Assert.That(gore.smr.enabled, Is.False);
        Assert.That(gore.GetCreatedObjects().Count, Is.GreaterThan(0));
        Assert.That(UnityEngine.Object.FindObjectsByType<BloodPoolVisual>(FindObjectsSortMode.None).Length,
            Is.GreaterThan(0));
        Assert.That(gore.GetCreatedObjects()
            .SelectMany(part => part.GetComponentsInChildren<Rigidbody>(true))
            .All(body => body.position.magnitude < 50f), Is.True);

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator PlayerDamage_HasNoHitReaction_ThenFullGoreDeath()
    {
        yield return new EnterPlayMode();

        CreateEvidenceGround("PlayerDeathRuntimeTest_Ground", Vector3.zero);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        Assert.That(prefab, Is.Not.Null);
        GameObject instance = UnityEngine.Object.Instantiate(prefab, Vector3.up * 0.1f, Quaternion.identity);
        instance.name = "Player_GoreRuntimeTest";
        instance.SetActive(true);
        WeaponInventoryBridge inventoryBridge = instance.GetComponentInChildren<WeaponInventoryBridge>(true);
        if (inventoryBridge != null)
            inventoryBridge.enabled = false;
        Demo.Scripts.Runtime.Character.FPSMovement movement =
            instance.GetComponentInChildren<Demo.Scripts.Runtime.Character.FPSMovement>(true);
        if (movement != null)
            movement.enabled = false;
        foreach (Camera playerCamera in instance.GetComponentsInChildren<Camera>(true))
            playerCamera.enabled = false;
        foreach (AudioListener listener in instance.GetComponentsInChildren<AudioListener>(true))
            listener.enabled = false;
        yield return null;

        Camera evidenceCamera = CreateEvidenceCamera(
            "PlayerGoreEvidenceCamera",
            new Vector3(0f, 2.2f, -3.6f),
            new Vector3(0f, 1f, 0f));
        PlayerVitals vitals = instance.GetComponent<PlayerVitals>();
        PlayerDeath death = instance.GetComponent<PlayerDeath>();
        GoreSimulatorComponent gore = instance.GetComponent<GoreSimulatorComponent>();
        Animator animator = instance.GetComponentInChildren<Animator>(true);
        Assert.That(vitals, Is.Not.Null);
        Assert.That(death, Is.Not.Null);
        Assert.That(gore, Is.Not.Null);
        var deathStayTime = typeof(PlayerDeath).GetField(
            "defaultStayTime",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.That(deathStayTime, Is.Not.Null);
        deathStayTime.SetValue(death, 100f);

        vitals.TakeDamage(DamageRequest.Bullet(1, instance.transform.position + Vector3.up));
        yield return null;
        Assert.That(death.IsDead, Is.False);
        Assert.That(gore.GetCreatedObjects().Count, Is.EqualTo(0));
        Assert.That(animator == null || animator.enabled, Is.True,
            "A non-lethal player hit must not drive a visual hit reaction.");

        long allocatedBytesBeforeDeath = System.GC.GetAllocatedBytesForCurrentThread();
        var deathStopwatch = System.Diagnostics.Stopwatch.StartNew();
        vitals.TakeDamage(DamageRequest.Bullet(
            vitals.MaxHealth,
            instance.transform.position + Vector3.up,
            Vector3.forward));
        deathStopwatch.Stop();
        long deathAllocatedBytes = System.GC.GetAllocatedBytesForCurrentThread() - allocatedBytesBeforeDeath;
        int deathMeshVertices = UnityEngine.Object
            .FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(filter => filter != null
                && filter.sharedMesh != null
                && filter.name.StartsWith("Player_GoreGroup_"))
            .Sum(filter => filter.sharedMesh.vertexCount);
        Debug.Log($"[PlayerGorePerf] deathMs={deathStopwatch.Elapsed.TotalMilliseconds:F3} "
            + $"allocatedBytes={deathAllocatedBytes} groupVertices={deathMeshVertices}");
        for (int physicsStep = 0; physicsStep < 42; physicsStep++)
            yield return new WaitForFixedUpdate();

        CaptureEvidence(evidenceCamera, "PlayerFullGoreDeath_Runtime.png");
        Assert.That(death.IsDead, Is.True);
        Assert.That(gore.smr.enabled, Is.False);
        Assert.That(gore.GetCreatedObjects().Count, Is.GreaterThan(0));
        GameObject[] goreGroups = UnityEngine.Object
            .FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(candidate => candidate != null
                && candidate.name.StartsWith("Player_GoreGroup_"))
            .Select(candidate => candidate.gameObject)
            .Distinct()
            .ToArray();
        Assert.That(goreGroups.Length, Is.EqualTo(7),
            "Player death must consolidate the modular outfit into seven anatomical physics groups.");
        Assert.That(goreGroups.All(group => group.GetComponent<Rigidbody>() != null), Is.True,
            "Each anatomical group must move as one rigid body instead of exploding every accessory separately.");
        MeshFilter[] cutSeals = goreGroups
            .SelectMany(group => group.GetComponentsInChildren<MeshFilter>(true))
            .Where(filter => filter != null && filter.name.StartsWith("Player_GoreCutCap_"))
            .ToArray();
        Assert.That(cutSeals.Length, Is.EqualTo(10),
            "All five anatomical separations need a solid seal on both exposed ends.");
        Assert.That(cutSeals.All(filter => IsClosedMesh(filter.sharedMesh)), Is.True,
            "Player cut seals must be watertight meshes rather than single open discs.");
        Assert.That(UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Any(renderer => renderer != null
                && renderer.enabled
                && renderer.name.StartsWith("Player_GoreSupplement_")), Is.False,
            "Legacy whole-renderer accessory fragments must not remain visible.");

        Renderer[] fullGoreRenderers = goreGroups
            .SelectMany(group => group.GetComponentsInChildren<Renderer>(true))
            .Where(renderer => renderer != null && renderer.enabled)
            .ToArray();
        Assert.That(fullGoreRenderers.Length, Is.GreaterThanOrEqualTo(7));
        Bounds fullGoreBounds = fullGoreRenderers[0].bounds;
        foreach (Renderer renderer in fullGoreRenderers.Skip(1))
            fullGoreBounds.Encapsulate(renderer.bounds);
        Assert.That(fullGoreBounds.size.y, Is.GreaterThan(0.7f));
        Assert.That(fullGoreBounds.size.x + fullGoreBounds.size.z, Is.GreaterThan(1.4f),
            "Player fragments must visibly separate instead of remaining in one shrunken clump.");
        Assert.That(UnityEngine.Object.FindObjectsByType<BloodPoolVisual>(FindObjectsSortMode.None).Length,
            Is.GreaterThan(0));
        Assert.That(goreGroups
            .SelectMany(part => part.GetComponentsInChildren<Rigidbody>(true))
            .All(body => body.position.magnitude < 50f), Is.True);

        for (int physicsStep = 0; physicsStep < 100; physicsStep++)
            yield return new WaitForFixedUpdate();
        CaptureEvidence(evidenceCamera, "PlayerFullGoreDeath_Settled.png");
        int groundedGroups = goreGroups.Count(group => group
            .GetComponentsInChildren<Renderer>(true)
            .Any(renderer => renderer != null && renderer.enabled && renderer.bounds.min.y < 0.3f));
        string settledSummary = string.Join(", ", goreGroups.Select(group =>
        {
            Renderer groupRenderer = group.GetComponent<Renderer>();
            Rigidbody body = group.GetComponent<Rigidbody>();
            return $"{group.name}[minY={(groupRenderer != null ? groupRenderer.bounds.min.y : float.NaN):F2}, "
                + $"posY={group.transform.position.y:F2}, velY={(body != null ? body.linearVelocity.y : float.NaN):F2}, "
                + $"kinematic={(body != null && body.isKinematic)}]";
        }));
        Assert.That(groundedGroups, Is.GreaterThanOrEqualTo(5),
            $"Most anatomical player fragments must settle onto the floor instead of remaining suspended. {settledSummary}");

        yield return new ExitPlayMode();
    }

    private static bool IsClosedMesh(Mesh mesh)
    {
        if (mesh == null || mesh.vertexCount == 0)
            return false;

        var edgeUses = new Dictionary<(int, int), int>();
        int[] triangles = mesh.triangles;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            CountEdge(triangles[i], triangles[i + 1], edgeUses);
            CountEdge(triangles[i + 1], triangles[i + 2], edgeUses);
            CountEdge(triangles[i + 2], triangles[i], edgeUses);
        }

        return edgeUses.Count > 0 && edgeUses.Values.All(count => count == 2);
    }

    private static void CountEdge(int a, int b, Dictionary<(int, int), int> edgeUses)
    {
        var edge = a < b ? (a, b) : (b, a);
        edgeUses.TryGetValue(edge, out int count);
        edgeUses[edge] = count + 1;
    }

    [UnityTest]
    public IEnumerator OctopusDeaths_BulletDisappears_ExplosiveLeavesTemporaryFragments()
    {
        yield return new EnterPlayMode();

        CreateEvidenceGround("OctopusDeathRuntimeTest_Ground", Vector3.zero);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OctopusPrefab);
        Assert.That(prefab, Is.Not.Null);
        GameObject instance = UnityEngine.Object.Instantiate(prefab, Vector3.up * 0.1f, Quaternion.identity);
        instance.name = "OctopusSwarm_GoreRuntimeTest";
        instance.SetActive(true);
        yield return null;

        Camera evidenceCamera = CreateEvidenceCamera(
            "OctopusGoreEvidenceCamera",
            new Vector3(0f, 4f, -7f),
            new Vector3(0f, 0.7f, 0f));
        OctopusSwarmMember[] members = instance.GetComponentsInChildren<OctopusSwarmMember>(true);
        Assert.That(members.Length, Is.GreaterThanOrEqualTo(2));
        Assert.That(instance.GetComponentInChildren<Item>(true), Is.Null);

        OctopusSwarmMember bulletMember = members[0];
        bulletMember.TakeDamage(DamageRequest.Bullet(1, bulletMember.transform.position, Vector3.forward));
        yield return new WaitForSeconds(0.25f);
        CaptureEvidence(evidenceCamera, "OctopusBulletDeath_Animation.png");
        Assert.That(bulletMember.IsDead, Is.True);
        GameObject bulletBlood = UnityEngine.Object
            .FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(particle => particle.transform.root.gameObject)
            .FirstOrDefault(root => root.name.StartsWith("VFX_Blood_Burst_Small"));
        Assert.That(bulletBlood, Is.Not.Null);
        AssertAuthoredBlood(bulletBlood, LoadBloodBurst("Small"), 1f);
        Assert.That(bulletMember.gameObject.activeInHierarchy, Is.True,
            "Bullet death should briefly show the death animation.");
        float bulletHideDeadline = Time.time + 2f;
        while (bulletMember.gameObject.activeInHierarchy && Time.time < bulletHideDeadline)
            yield return null;
        Assert.That(bulletMember.gameObject.activeInHierarchy, Is.False,
            "The small one-shot octopus must disappear after its death animation.");
        Assert.That(bulletBlood != null && bulletBlood.activeInHierarchy, Is.True,
            "The authored death cue must survive the octopus disappearing.");

        OctopusSwarmMember explosiveMember = members[1];
        explosiveMember.TakeDamage(DamageRequest.Explosive(
            1,
            explosiveMember.transform.position,
            Vector3.forward));
        yield return new WaitForSeconds(0.3f);
        CaptureEvidence(evidenceCamera, "OctopusExplosiveDeath_Runtime.png");
        Assert.That(explosiveMember.IsDead, Is.True);
        GameObject explosiveBlood = UnityEngine.Object
            .FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(particle => particle.transform.root.gameObject)
            .FirstOrDefault(root => root.name.StartsWith("VFX_Blood_Burst_Medium"));
        Assert.That(explosiveBlood, Is.Not.Null);
        AssertAuthoredBlood(explosiveBlood, LoadBloodBurst("Medium"), 1f);
        float explosiveHideDeadline = Time.time + 2f;
        while (explosiveMember.gameObject.activeInHierarchy && Time.time < explosiveHideDeadline)
            yield return null;
        Assert.That(explosiveMember.gameObject.activeInHierarchy, Is.False);

        GameObject[] fragments = UnityEngine.Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .Where(go => go != null && go.name.StartsWith("OctopusGoreFragment"))
            .ToArray();
        Assert.That(fragments.Length, Is.GreaterThanOrEqualTo(3));
        Assert.That(fragments.All(fragment => fragment.GetComponentInChildren<Rigidbody>(true) != null), Is.True);
        BloodPoolVisual[] pools = UnityEngine.Object.FindObjectsByType<BloodPoolVisual>(FindObjectsSortMode.None)
            .Where(pool => pool.name.StartsWith("MonsterBloodSplatter"))
            .ToArray();
        Assert.That(pools.Length, Is.GreaterThanOrEqualTo(2));
        foreach (BloodPoolVisual pool in pools)
            AssertAuthoredBloodPool(pool.gameObject);

        yield return new ExitPlayMode();
    }

    private static GameObject LoadBloodBurst(string size)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BloodVfxRoot
            + $"VFX/Performance Versions/Bursts/Once/VFX_Blood_Burst_{size}.prefab");
        Assert.That(prefab, Is.Not.Null);
        return prefab;
    }

    private static void AssertAuthoredBlood(GameObject instance, GameObject prefab, float scale)
    {
        Assert.That(instance.transform.parent, Is.Null, "Blood must finish independently of the monster.");
        Assert.That(Vector3.Distance(instance.transform.localScale, prefab.transform.localScale * scale),
            Is.LessThan(0.001f));
        ParticleSystem[] actual = instance.GetComponentsInChildren<ParticleSystem>(true);
        ParticleSystem[] authored = prefab.GetComponentsInChildren<ParticleSystem>(true);
        Assert.That(actual.Length, Is.EqualTo(authored.Length).And.GreaterThan(0));

        for (int i = 0; i < actual.Length; i++)
        {
            ParticleSystem.MainModule actualMain = actual[i].main;
            ParticleSystem.MainModule authoredMain = authored[i].main;
            Assert.That(actualMain.loop, Is.False, actual[i].name);
            Assert.That(actualMain.scalingMode, Is.EqualTo(ParticleSystemScalingMode.Hierarchy), actual[i].name);
            Assert.That(actualMain.startColor.mode, Is.EqualTo(authoredMain.startColor.mode), actual[i].name);
            Assert.That(actualMain.startLifetime.mode, Is.EqualTo(authoredMain.startLifetime.mode), actual[i].name);
            foreach (float sample in new[] { 0f, 0.5f, 1f })
            {
                foreach (float interpolation in new[] { 0f, 1f })
                {
                    Assert.That(Vector4.Distance(
                            actualMain.startColor.Evaluate(sample, interpolation),
                            authoredMain.startColor.Evaluate(sample, interpolation)),
                        Is.LessThan(0.0001f), $"{actual[i].name} must keep the authored blood color.");
                    Assert.That(actualMain.startLifetime.Evaluate(sample, interpolation),
                        Is.EqualTo(authoredMain.startLifetime.Evaluate(sample, interpolation)).Within(0.0001f),
                        $"{actual[i].name} must keep its authored particle lifetime.");
                }
            }
        }

        Renderer[] actualRenderers = instance.GetComponentsInChildren<Renderer>(true);
        Renderer[] authoredRenderers = prefab.GetComponentsInChildren<Renderer>(true);
        Assert.That(actualRenderers.Length, Is.EqualTo(authoredRenderers.Length).And.GreaterThan(0));
        for (int i = 0; i < actualRenderers.Length; i++)
            Assert.That(actualRenderers[i].sharedMaterials, Is.EqualTo(authoredRenderers[i].sharedMaterials));
        Assert.That(actualRenderers.SelectMany(renderer => renderer.sharedMaterials)
            .Any(material => material != null && AssetDatabase.GetAssetPath(material).StartsWith(BloodVfxRoot)),
            Is.True, "The cue must render with the Vefects blood materials.");
    }

    private static void AssertAuthoredBloodPool(GameObject instance)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterBloodDecalPrefab);
        Assert.That(prefab, Is.Not.Null);
        Assert.That(instance.transform.parent, Is.Null);
        MeshRenderer renderer = instance.GetComponentInChildren<MeshRenderer>(true);
        MeshFilter filter = instance.GetComponentInChildren<MeshFilter>(true);
        Assert.That(renderer, Is.Not.Null);
        Assert.That(filter, Is.Not.Null);
        Assert.That(filter.sharedMesh, Is.SameAs(prefab.GetComponentInChildren<MeshFilter>(true).sharedMesh),
            "Monster pools must use the authored decal mesh.");
        Assert.That(renderer.sharedMaterial,
            Is.SameAs(prefab.GetComponentInChildren<MeshRenderer>(true).sharedMaterial));
        Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterial),
            Is.EqualTo("Assets/Combat/Death/MonsterBloodSplatter.mat"));
        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BloodVfxRoot
            + "VFX/Decals/Prefabs/VFX_Blood_Decal_Splatter_01.prefab");
        Assert.That(sourcePrefab, Is.Not.Null);
        Material sourceMaterial = sourcePrefab.GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
        Assert.That(sourceMaterial, Is.Not.Null);
        Assert.That(renderer.sharedMaterial.shader.name,
            Is.EqualTo("Vefects/SH_Vefects_VFX_URP_Blood_Decal_01"));
        Assert.That(renderer.sharedMaterial.GetTexture("_DecalTexture"),
            Is.SameAs(sourceMaterial.GetTexture("_DecalTexture")));
        Assert.That(renderer.sharedMaterial.GetTexture("_LUT"),
            Is.SameAs(sourceMaterial.GetTexture("_LUT")));
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        Assert.That(properties.isEmpty, Is.True, "Monster pools must preserve the authored blood shader color.");
    }

    private static GameObject CreateEvidenceGround(string name, Vector3 center)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = name;
        ground.transform.position = center + Vector3.down * 0.1f;
        ground.transform.localScale = new Vector3(14f, 0.2f, 14f);
        Physics.SyncTransforms();
        return ground;
    }

    private static Camera CreateEvidenceCamera(string name, Vector3 position, Vector3 lookAt)
    {
        GameObject cameraObject = new(name);
        Camera evidenceCamera = cameraObject.AddComponent<Camera>();
        evidenceCamera.transform.position = position;
        evidenceCamera.transform.LookAt(lookAt);
        evidenceCamera.fieldOfView = 65f;
        evidenceCamera.nearClipPlane = 0.03f;
        evidenceCamera.cullingMask = ~0;
        return evidenceCamera;
    }

    private static void CaptureEvidence(Camera evidenceCamera, string fileName)
    {
        RenderTexture renderTexture = RenderTexture.GetTemporary(960, 540, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        evidenceCamera.targetTexture = renderTexture;
        RenderTexture.active = renderTexture;
        evidenceCamera.Render();
        var evidence = new Texture2D(960, 540, TextureFormat.RGBA32, false);
        evidence.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
        evidence.Apply();
        string evidencePath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "Temp",
            fileName);
        File.WriteAllBytes(evidencePath, evidence.EncodeToPNG());
        evidenceCamera.targetTexture = null;
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(renderTexture);
        UnityEngine.Object.Destroy(evidence);
    }
}
