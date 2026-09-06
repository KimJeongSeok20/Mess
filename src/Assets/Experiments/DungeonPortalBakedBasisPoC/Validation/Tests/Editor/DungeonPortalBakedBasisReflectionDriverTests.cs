using System;
using DungeonPortalBakedBasisPoC.Validation;
using DungeonPortalBakedBasisPoC.Validation.Editor;
using DungeonPortalTransportPoC;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DungeonPortalBakedBasisPoC.Validation.Tests.Editor
{
    [TestFixture]
    public sealed class DungeonPortalBakedBasisReflectionDriverTests
    {
        [Test]
        public void ExistingProfiles_AreExactlyOneStartAndFourAdministrativeP0ResidualPairs()
        {
            DungeonPortalBakedBasisValidationContract.ReflectionSpec[] specs =
                DungeonPortalBakedBasisValidationContract.ReflectionSpecs;
            Assert.That(specs, Has.Length.EqualTo(5));

            int startCount = 0;
            int administrativeCount = 0;
            for (int i = 0; i < specs.Length; i++)
            {
                DungeonPortalRoomReflectionProfile profile =
                    AssetDatabase.LoadAssetAtPath<DungeonPortalRoomReflectionProfile>(
                        specs[i].ProfilePath);
                Assert.That(profile, Is.Not.Null, specs[i].ProfilePath);
                Assert.That(profile.TryValidate(out string failure), Is.True, failure);
                Assert.That(profile.Power0ResidualCubemap,
                    Is.Not.SameAs(profile.Power100Cubemap));
                Assert.That(profile.FixedProbeIntensity, Is.GreaterThan(0f));
                if (specs[i].UsesStartPower)
                    startCount++;
                else
                    administrativeCount++;
            }

            Assert.That(startCount, Is.EqualTo(1));
            Assert.That(administrativeCount, Is.EqualTo(4));
        }

        [Test]
        public void ApplyP0AndP100_IsApertureIndependentAndRestoresEveryProbeExactly()
        {
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
                Assert.Ignore("ARGBHalf reflection blend targets are unsupported on this editor.");

            Harness harness = Harness.Create();
            try
            {
                Assert.That(harness.ReflectionRoot.GetComponentsInChildren<Renderer>(true),
                    Is.Empty);
                ProbeSnapshot[] before = ProbeSnapshot.CaptureAll(harness.AllManagedProbes);

                Assert.That(harness.Driver.TryApplyImmediateForEvidence(
                    0f, 1f, out string initialFailure), Is.True, initialFailure);
                Assert.That(harness.Driver.IsInitialized, Is.True);
                Assert.That(harness.Driver.LastAppliedStartPower01, Is.EqualTo(0f));
                Assert.That(harness.Driver.LastAppliedAdministrativePower01, Is.EqualTo(1f));
                AssertOwnedBlendState(harness);

                Texture[] blendTargets = new Texture[harness.OwnedProbes.Length];
                for (int i = 0; i < blendTargets.Length; i++)
                    blendTargets[i] = harness.OwnedProbes[i].customBakedTexture;

                SetAdjacentFlagWithoutDrivingConnection(harness.ConnectionDriver, false);
                Assert.That(harness.Driver.TryApplyImmediateForEvidence(
                    0f, 1f, out string adjacentOffFailure), Is.True, adjacentOffFailure);
                SetAdjacentFlagWithoutDrivingConnection(harness.ConnectionDriver, true);
                Assert.That(harness.Driver.TryApplyImmediateForEvidence(
                    0f, 1f, out string adjacentOnFailure), Is.True, adjacentOnFailure);
                for (int i = 0; i < blendTargets.Length; i++)
                    Assert.That(harness.OwnedProbes[i].customBakedTexture,
                        Is.SameAs(blendTargets[i]));

                Assert.That(harness.ReflectionRoot.GetComponentsInChildren<Renderer>(true),
                    Is.Empty);
                harness.Driver.RestoreProbeStatesForEvidence();
                Assert.That(harness.Driver.IsInitialized, Is.False);
                ProbeSnapshot.AssertAllExact(before, harness.AllManagedProbes);
            }
            finally
            {
                harness.Dispose();
            }
        }

        [Test]
        public void DuplicateProductionSuppression_FailsValidationWithoutTouchingAnyProbe()
        {
            Harness harness = Harness.Create(true);
            try
            {
                ProbeSnapshot[] before = ProbeSnapshot.CaptureAll(harness.AllManagedProbes);

                Assert.That(harness.Driver.TryValidateConfiguration(out string failure), Is.False);
                StringAssert.Contains("duplicate", failure.ToLowerInvariant());
                Assert.That(harness.Driver.IsInitialized, Is.False);
                Assert.That(harness.ReflectionRoot.GetComponentsInChildren<Renderer>(true),
                    Is.Empty);
                ProbeSnapshot.AssertAllExact(before, harness.AllManagedProbes);
            }
            finally
            {
                harness.Dispose();
            }
        }

        private static void AssertOwnedBlendState(Harness harness)
        {
            for (int i = 0; i < harness.OwnedProbes.Length; i++)
            {
                ReflectionProbe probe = harness.OwnedProbes[i];
                Assert.That(probe.enabled, Is.True);
                Assert.That(probe.mode, Is.EqualTo(ReflectionProbeMode.Custom));
                Assert.That(probe.customBakedTexture, Is.TypeOf<RenderTexture>());
                Assert.That(probe.intensity,
                    Is.EqualTo(harness.Profiles[i].FixedProbeIntensity).Within(0.000001f));
            }
            for (int i = 0; i < harness.SuppressedProbes.Length; i++)
                Assert.That(harness.SuppressedProbes[i].enabled, Is.False);
        }

        private static void SetAdjacentFlagWithoutDrivingConnection(
            DungeonPortalBakedBasisConnectionDriver connectionDriver,
            bool value)
        {
            SerializedObject serialized = new SerializedObject(connectionDriver);
            SerializedProperty adjacent = serialized.FindProperty("adjacentTransportEnabled");
            Assert.That(adjacent, Is.Not.Null);
            adjacent.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(connectionDriver.AdjacentTransportEnabled, Is.EqualTo(value));
        }

        private sealed class Harness : IDisposable
        {
            internal Scene PreviewScene;
            internal GameObject Root;
            internal GameObject ReflectionRoot;
            internal DungeonPortalBakedBasisConnectionDriver ConnectionDriver;
            internal DungeonPortalBakedBasisReflectionDriver Driver;
            internal DungeonPortalRoomReflectionProfile[] Profiles;
            internal ReflectionProbe[] OwnedProbes;
            internal ReflectionProbe[] SuppressedProbes;
            internal ReflectionProbe[] AllManagedProbes;

            internal static Harness Create(bool duplicateSuppression = false)
            {
                var result = new Harness();
                try
                {
                result.PreviewScene = EditorSceneManager.NewPreviewScene();
                result.Root = new GameObject("DPBB_ReflectionTestRoot");
                SceneManager.MoveGameObjectToScene(result.Root, result.PreviewScene);

                GameObject connectionObject = new GameObject("Connection");
                connectionObject.transform.SetParent(result.Root.transform, false);
                result.ConnectionDriver =
                    connectionObject.AddComponent<DungeonPortalBakedBasisConnectionDriver>();

                result.ReflectionRoot = new GameObject("ReflectionDriver");
                result.ReflectionRoot.transform.SetParent(result.Root.transform, false);
                result.Driver =
                    result.ReflectionRoot.AddComponent<DungeonPortalBakedBasisReflectionDriver>();

                DungeonPortalBakedBasisValidationContract.ReflectionSpec[] specs =
                    DungeonPortalBakedBasisValidationContract.ReflectionSpecs;
                result.Profiles = new DungeonPortalRoomReflectionProfile[specs.Length];
                result.OwnedProbes = new ReflectionProbe[specs.Length];
                var administrativeIds = new string[4];
                var administrativeProbes = new ReflectionProbe[4];
                var administrativeProfiles = new DungeonPortalRoomReflectionProfile[4];
                int administrativeIndex = 0;
                for (int i = 0; i < specs.Length; i++)
                {
                    result.Profiles[i] =
                        AssetDatabase.LoadAssetAtPath<DungeonPortalRoomReflectionProfile>(
                            specs[i].ProfilePath);
                    Assert.That(result.Profiles[i], Is.Not.Null, specs[i].ProfilePath);

                    GameObject probeObject = new GameObject(specs[i].StableId);
                    probeObject.transform.SetParent(result.ReflectionRoot.transform, false);
                    ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();
                    probe.enabled = i % 2 != 0;
                    probe.mode = i % 2 == 0
                        ? ReflectionProbeMode.Baked
                        : ReflectionProbeMode.Custom;
                    probe.customBakedTexture = result.Profiles[i].Power100Cubemap;
                    probe.intensity = 0.25f + i * 0.1f;
                    probe.size = new Vector3(3f + i, 4f + i, 5f + i);
                    probe.center = new Vector3(i, i * 0.5f, -i);
                    result.OwnedProbes[i] = probe;

                    if (!specs[i].UsesStartPower)
                    {
                        administrativeIds[administrativeIndex] = specs[i].StableId;
                        administrativeProbes[administrativeIndex] = probe;
                        administrativeProfiles[administrativeIndex] = result.Profiles[i];
                        administrativeIndex++;
                    }
                }

                result.SuppressedProbes = new ReflectionProbe[10];
                for (int i = 0; i < result.SuppressedProbes.Length; i++)
                {
                    GameObject probeObject = new GameObject("ProductionProbe_" + i);
                    probeObject.transform.SetParent(result.Root.transform, false);
                    ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();
                    probe.enabled = i % 3 != 0;
                    probe.mode = ReflectionProbeMode.Custom;
                    probe.customBakedTexture = result.Profiles[i % result.Profiles.Length]
                        .Power0ResidualCubemap;
                    probe.intensity = 0.5f + i * 0.05f;
                    result.SuppressedProbes[i] = probe;
                }

                ReflectionProbe[] configuredSuppression =
                    (ReflectionProbe[])result.SuppressedProbes.Clone();
                if (duplicateSuppression)
                    configuredSuppression[9] = configuredSuppression[0];
                result.Driver.ConfigureAuthoring(
                    result.ConnectionDriver,
                    specs[0].StableId,
                    result.OwnedProbes[0],
                    result.Profiles[0],
                    administrativeIds,
                    administrativeProbes,
                    administrativeProfiles,
                    configuredSuppression);

                result.AllManagedProbes = new ReflectionProbe[
                    result.OwnedProbes.Length + result.SuppressedProbes.Length];
                Array.Copy(result.OwnedProbes, 0, result.AllManagedProbes, 0,
                    result.OwnedProbes.Length);
                Array.Copy(result.SuppressedProbes, 0, result.AllManagedProbes,
                    result.OwnedProbes.Length, result.SuppressedProbes.Length);
                return result;
                }
                catch
                {
                    result.Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                if (Driver != null)
                    Driver.RestoreProbeStatesForEvidence();
                if (PreviewScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(PreviewScene);
            }
        }

        private readonly struct ProbeSnapshot
        {
            private readonly string serializedJson;
            private readonly bool activeSelf;
            private readonly Vector3 localPosition;
            private readonly Quaternion localRotation;
            private readonly Vector3 localScale;

            private ProbeSnapshot(ReflectionProbe probe)
            {
                serializedJson = EditorJsonUtility.ToJson(probe, false);
                activeSelf = probe.gameObject.activeSelf;
                localPosition = probe.transform.localPosition;
                localRotation = probe.transform.localRotation;
                localScale = probe.transform.localScale;
            }

            internal static ProbeSnapshot[] CaptureAll(ReflectionProbe[] probes)
            {
                var result = new ProbeSnapshot[probes.Length];
                for (int i = 0; i < probes.Length; i++)
                    result[i] = new ProbeSnapshot(probes[i]);
                return result;
            }

            internal static void AssertAllExact(
                ProbeSnapshot[] expected,
                ReflectionProbe[] actual)
            {
                Assert.That(actual, Has.Length.EqualTo(expected.Length));
                for (int i = 0; i < expected.Length; i++)
                {
                    Assert.That(EditorJsonUtility.ToJson(actual[i], false),
                        Is.EqualTo(expected[i].serializedJson), "serialized probe " + i);
                    Assert.That(actual[i].gameObject.activeSelf,
                        Is.EqualTo(expected[i].activeSelf), "activeSelf " + i);
                    Assert.That(actual[i].transform.localPosition,
                        Is.EqualTo(expected[i].localPosition), "position " + i);
                    Assert.That(actual[i].transform.localRotation,
                        Is.EqualTo(expected[i].localRotation), "rotation " + i);
                    Assert.That(actual[i].transform.localScale,
                        Is.EqualTo(expected[i].localScale), "scale " + i);
                }
            }
        }
    }
}
