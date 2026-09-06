using System;
using DungeonPortalTransportPoC;
using DungeonPortalTransportPoC.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DungeonPortalBakedBasisPoC.Validation.Tests.Editor
{
    public sealed class DungeonPortalBakedBasisAngleCaptureTests
    {
        private const string SourceScenePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes/Start_Admin_PortalTransportValidation.unity";

        [Test]
        public void Contracts_AreD000PlusFourMatchedBaselineFullPairs()
        {
            DungeonPortalReceiverBounceBaker.DoorAngleCaptureContract[] contracts =
                DungeonPortalReceiverBounceBaker.GetDoorAngleCaptureContractsForTest();
            Assert.That(contracts, Has.Length.EqualTo(9));
            Assert.That(
                Array.ConvertAll(contracts, value => value.StateId),
                Is.EqualTo(new[]
                {
                    "D000_Baseline",
                    "D025_Baseline", "D025_Full",
                    "D050_Baseline", "D050_Full",
                    "D075_Baseline", "D075_Full",
                    "D100_Baseline", "D100_Full"
                }));
            Assert.That(
                Array.ConvertAll(contracts, value => value.OpenFraction),
                Is.EqualTo(new[] { 0f, 0.25f, 0.25f, 0.5f, 0.5f, 0.75f, 0.75f, 1f, 1f }));
            for (int i = 0; i < contracts.Length; i++)
            {
                Assert.That(contracts[i].AngleDegrees, Is.EqualTo(90f * contracts[i].OpenFraction).Within(0.0001f));
                Assert.That(contracts[i].UseTransientDoorProxy, Is.True);
                bool expectedFull = contracts[i].StateId.EndsWith("_Full", StringComparison.Ordinal);
                Assert.That(contracts[i].InjectorEnabled, Is.EqualTo(expectedFull));
                Assert.That(contracts[i].InjectorBounceIntensity, Is.EqualTo(expectedFull ? 1f : 0f));
            }
        }

        [Test]
        public void OwnedPathGuard_AcceptsOnlyPhysicalAngleCaptureRoot()
        {
            const string owned =
                "Assets/Experiments/DungeonPortalBakedBasisPoC/AngleCapture/Generated/StartRoom_R000/States/D050_Full";
            Assert.That(
                DungeonPortalReceiverBounceBaker.TryNormalizeOwnedAnglePathForTest(
                    owned,
                    out string canonical,
                    out string failure),
                Is.True,
                failure);
            Assert.That(canonical, Is.EqualTo(owned));
            Assert.That(
                DungeonPortalReceiverBounceBaker.TryNormalizeOwnedAnglePathForTest(
                    owned.Replace('/', '\\'),
                    out canonical,
                    out failure),
                Is.True,
                failure);
            Assert.That(canonical, Is.EqualTo(owned));
            Assert.That(
                DungeonPortalReceiverBounceBaker.TryNormalizeOwnedAnglePathForTest(
                    "Assets/Experiments/DungeonPortalBakedBasisPoC/AngleCapture/Generated/../Generated/x",
                    out _,
                    out _),
                Is.False);
            Assert.That(
                DungeonPortalReceiverBounceBaker.TryNormalizeOwnedAnglePathForTest(
                    "Assets/Experiments/DungeonPortalBakedBasisPoC/Generated/StartRoom_R000/x",
                    out _,
                    out _),
                Is.False);
            Assert.That(
                DungeonPortalReceiverBounceBaker.TryNormalizeOwnedAnglePathForTest(
                    "Assets/Experiments/DungeonPortalTransportPoC/Generated/Bounce/x",
                    out _,
                    out _),
                Is.False);
        }

        [Test]
        public void DoorPose_UsesClosedTimesNormalizedHingeFraction()
        {
            Quaternion closed = Quaternion.Euler(3f, 7f, 11f);
            Quaternion d025 = DungeonPortalReceiverBounceBaker.ComputeDoorAnglePoseForTest(
                closed,
                new Vector3(0f, 5f, 0f),
                90f,
                0.25f);
            Quaternion expected = closed * Quaternion.AngleAxis(22.5f, Vector3.up);
            Assert.That(Quaternion.Angle(d025, expected), Is.LessThan(0.0001f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DungeonPortalReceiverBounceBaker.ComputeDoorAnglePoseForTest(
                    closed,
                    Vector3.zero,
                    90f,
                    0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DungeonPortalReceiverBounceBaker.ComputeDoorAnglePoseForTest(
                    closed,
                    Vector3.up,
                    90f,
                    1.01f));
        }

        [Test]
        public void ReceiverLayoutAndMatchedPair_AcceptPoseSpecificStencilsButRejectPairDrift()
        {
            DungeonPortalBakedBasisDoorAngleCapture.AngleState baseline = MakeState(
                "D000_Baseline",
                0f,
                false,
                0f);
            DungeonPortalBakedBasisDoorAngleCapture.AngleState comparison = MakeState(
                "D050_Full",
                0.5f,
                true,
                1f);
            Assert.That(
                DungeonPortalBakedBasisDoorAngleCapture.TryValidateReceiverLayout(
                    baseline,
                    comparison,
                    out string failure),
                Is.True,
                failure);

            DungeonPortalReceiverResponseCapture.CaptureRenderer renderer = comparison.renderers[0];
            renderer.lightmapScaleOffset = new Vector4(0.5f, 0.5f, 0.26f, 0.25f);
            comparison.renderers[0] = renderer;
            Assert.That(
                DungeonPortalBakedBasisDoorAngleCapture.TryValidateReceiverLayout(
                    baseline,
                    comparison,
                    out _),
                Is.False);

            comparison = MakeState("D050_Full", 0.5f, true, 1f);
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry entry =
                comparison.fullRendererInventory[0];
            entry.parityMetadataHash = "changed-material-or-renderer-parity";
            comparison.fullRendererInventory[0] = entry;
            Assert.That(
                DungeonPortalBakedBasisDoorAngleCapture.TryValidateReceiverLayout(
                    baseline,
                    comparison,
                    out _),
                Is.False);

            DungeonPortalBakedBasisDoorAngleCapture.AngleState matchedBaseline = MakeState(
                "D050_Baseline",
                0.5f,
                false,
                0f);
            comparison = MakeState("D050_Full", 0.5f, true, 1f);
            Assert.That(
                DungeonPortalBakedBasisDoorAngleCapture.TryValidateMatchedPosePair(
                    matchedBaseline,
                    comparison,
                    out failure),
                Is.True,
                failure);
            DungeonPortalReceiverResponseCapture.ProbeSample changedProbe = comparison.probes[0];
            changedProbe.localPosition += Vector3.right * 0.01f;
            comparison.probes[0] = changedProbe;
            Assert.That(
                DungeonPortalBakedBasisDoorAngleCapture.TryValidateMatchedPosePair(
                    matchedBaseline,
                    comparison,
                    out _),
                Is.False);
        }

        [Test]
        public void NoProxyCorrespondence_AcceptsAtlasRepackWhileExactLayoutStillRejectsIt()
        {
            DungeonPortalBakedBasisDoorAngleCapture.AngleState proxy = MakeState(
                "D050_Full",
                0.5f,
                true,
                1f);
            DungeonPortalBakedBasisDoorAngleCapture.AngleState noProxy = MakeState(
                DungeonPortalBakedBasisDoorAngleCapture.NoProxyProofStateId,
                0.5f,
                true,
                1f);

            DungeonPortalReceiverResponseCapture.CaptureRenderer renderer = noProxy.renderers[0];
            renderer.lightmapIndex = 7;
            renderer.lightmapScaleOffset = new Vector4(0.560757f, 0.560757f, 0.26784268f, -0.004380914f);
            noProxy.renderers[0] = renderer;
            DungeonPortalReceiverResponseCapture.CaptureLightmap map = noProxy.lightmaps[0];
            map.sourceLightmapIndex = 7;
            map.colorWidth = 128;
            map.colorHeight = 32;
            map.directionWidth = 128;
            map.directionHeight = 32;
            noProxy.lightmaps[0] = map;
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry inventory =
                noProxy.fullRendererInventory[0];
            inventory.parityMetadataHash = "layout-dependent-hash-after-repack";
            noProxy.fullRendererInventory[0] = inventory;

            Assert.That(
                DungeonPortalBakedBasisDoorAngleCapture.TryValidateReceiverLayout(
                    proxy,
                    noProxy,
                    out _),
                Is.False,
                "The persisted nine-state strict layout contract must continue to reject repacks.");
            Assert.That(
                DungeonPortalBakedBasisDoorAngleCapture.TryValidateNoProxyReceiverCorrespondence(
                    proxy,
                    noProxy,
                    "same-layout-free-parity",
                    "same-layout-free-parity",
                    out string failure),
                Is.True,
                failure);
        }

        [Test]
        public void NoProxyCorrespondence_RejectsReceiverIdentityMeshAndUv2Drift()
        {
            DungeonPortalBakedBasisDoorAngleCapture.AngleState proxy = MakeState(
                "D050_Full",
                0.5f,
                true,
                1f);

            DungeonPortalBakedBasisDoorAngleCapture.AngleState changed = MakeState(
                DungeonPortalBakedBasisDoorAngleCapture.NoProxyProofStateId,
                0.5f,
                true,
                1f);
            DungeonPortalReceiverResponseCapture.CaptureRenderer renderer = changed.renderers[0];
            renderer.relativePath = "Walls[1]/Wall[0]";
            changed.renderers[0] = renderer;
            AssertNoProxyCorrespondenceRejected(proxy, changed);

            changed = MakeState(
                DungeonPortalBakedBasisDoorAngleCapture.NoProxyProofStateId,
                0.5f,
                true,
                1f);
            renderer = changed.renderers[0];
            renderer.meshAssetGuid = "changed-mesh-guid";
            changed.renderers[0] = renderer;
            AssertNoProxyCorrespondenceRejected(proxy, changed);

            changed = MakeState(
                DungeonPortalBakedBasisDoorAngleCapture.NoProxyProofStateId,
                0.5f,
                true,
                1f);
            renderer = changed.renderers[0];
            renderer.meshLocalId = 456;
            changed.renderers[0] = renderer;
            AssertNoProxyCorrespondenceRejected(proxy, changed);

            changed = MakeState(
                DungeonPortalBakedBasisDoorAngleCapture.NoProxyProofStateId,
                0.5f,
                true,
                1f);
            renderer = changed.renderers[0];
            renderer.meshUv2Hash = "changed-uv2";
            changed.renderers[0] = renderer;
            AssertNoProxyCorrespondenceRejected(proxy, changed);
        }

        [Test]
        public void NoProxyCorrespondence_RejectsReceiverInventoryOrLayoutFreeParityDrift()
        {
            DungeonPortalBakedBasisDoorAngleCapture.AngleState proxy = MakeState(
                "D050_Full",
                0.5f,
                true,
                1f);
            DungeonPortalBakedBasisDoorAngleCapture.AngleState changed = MakeState(
                DungeonPortalBakedBasisDoorAngleCapture.NoProxyProofStateId,
                0.5f,
                true,
                1f);
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry inventory =
                changed.fullRendererInventory[0];
            inventory.canonicalBucket =
                "ReceiverRoom|Walls[0]/Wall[0]|UnityEngine.SkinnedMeshRenderer|0";
            changed.fullRendererInventory[0] = inventory;
            AssertNoProxyCorrespondenceRejected(proxy, changed);

            changed = MakeState(
                DungeonPortalBakedBasisDoorAngleCapture.NoProxyProofStateId,
                0.5f,
                true,
                1f);
            Assert.That(
                DungeonPortalBakedBasisDoorAngleCapture.TryValidateNoProxyReceiverCorrespondence(
                    proxy,
                    changed,
                    "layout-free-before",
                    "layout-free-after",
                    out _),
                Is.False);
        }

        [Test]
        public void NoProxyCorrespondence_RejectsInvalidPerBakeIndexOrSt()
        {
            DungeonPortalBakedBasisDoorAngleCapture.AngleState proxy = MakeState(
                "D050_Full",
                0.5f,
                true,
                1f);
            DungeonPortalBakedBasisDoorAngleCapture.AngleState changed = MakeState(
                DungeonPortalBakedBasisDoorAngleCapture.NoProxyProofStateId,
                0.5f,
                true,
                1f);
            DungeonPortalReceiverResponseCapture.CaptureRenderer renderer = changed.renderers[0];
            renderer.lightmapScaleOffset = new Vector4(0f, 0.5f, 0.25f, 0.25f);
            changed.renderers[0] = renderer;
            AssertNoProxyCorrespondenceRejected(proxy, changed);

            changed = MakeState(
                DungeonPortalBakedBasisDoorAngleCapture.NoProxyProofStateId,
                0.5f,
                true,
                1f);
            renderer = changed.renderers[0];
            renderer.lightmapIndex = 9;
            changed.renderers[0] = renderer;
            AssertNoProxyCorrespondenceRejected(proxy, changed);
        }

        [Test]
        public void EffectProof_IsFailClosedAtEveryThreshold()
        {
            DungeonPortalBakedBasisDoorAngleCapture.NoProxyEffectProof proof = MakeEffectProof();
            Assert.That(
                DungeonPortalBakedBasisDoorAngleCapture.TryValidateEffectProof(
                    proof,
                    90f,
                    out string failure),
                Is.True,
                failure);
            proof.meanAbsoluteRgbDifference = proof.requiredMeanAbsoluteRgbDifference * 0.5f;
            Assert.That(
                DungeonPortalBakedBasisDoorAngleCapture.TryValidateEffectProof(
                    proof,
                    90f,
                    out _),
                Is.False);
        }

        [Test]
        public void AssetContract_RequiresExactShadowsOnlyContributeGiProxyFlags()
        {
            var capture = ScriptableObject.CreateInstance<DungeonPortalBakedBasisDoorAngleCapture>();
            try
            {
                DungeonPortalBakedBasisDoorAngleCapture.CaptureProvenance provenance = MakeProvenance();
                DungeonPortalBakedBasisDoorAngleCapture.DoorProxyProvenance proxy = MakeProxyProvenance();
                DungeonPortalBakedBasisDoorAngleCapture.AngleState baseline = MakeState(
                    "D000_Baseline",
                    0f,
                    false,
                    0f);
                DungeonPortalBakedBasisDoorAngleCapture.AngleState[] matchedBaselines =
                {
                    MakeState("D025_Baseline", 0.25f, false, 0f),
                    MakeState("D050_Baseline", 0.5f, false, 0f),
                    MakeState("D075_Baseline", 0.75f, false, 0f),
                    MakeState("D100_Baseline", 1f, false, 0f)
                };
                DungeonPortalBakedBasisDoorAngleCapture.AngleState[] full =
                {
                    MakeState("D025_Full", 0.25f, true, 1f),
                    MakeState("D050_Full", 0.5f, true, 1f),
                    MakeState("D075_Full", 0.75f, true, 1f),
                    MakeState("D100_Full", 1f, true, 1f)
                };
                capture.ConfigureAuthoring(
                    "StartRoom_R000",
                    "Doorways[5]/Door_SM_A[0]/DoorWayPoint[0]",
                    90f,
                    provenance,
                    proxy,
                    baseline,
                    matchedBaselines,
                    full,
                    MakeEffectProof());
                Assert.That(capture.TryValidate(out string failure), Is.True, failure);

                proxy.staticEditorFlags = 0;
                capture.ConfigureAuthoring(
                    "StartRoom_R000",
                    "Doorways[5]/Door_SM_A[0]/DoorWayPoint[0]",
                    90f,
                    provenance,
                    proxy,
                    baseline,
                    matchedBaselines,
                    full,
                    MakeEffectProof());
                Assert.That(capture.TryValidate(out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(capture);
            }
        }

        [Test]
        public void StorageEstimate_LeavesPeakHeadroomAbovePersistedPayload()
        {
            DungeonPortalReceiverBounceBaker.DoorAngleCaptureStorageEstimate estimate =
                DungeonPortalReceiverBounceBaker.GetDoorAngleCaptureStorageEstimateForTest();
            Assert.That(estimate.ApproximatePersistedBytes, Is.EqualTo(900L * 1024L * 1024L));
            Assert.That(estimate.RecommendedFreeBytes, Is.GreaterThanOrEqualTo(2L * estimate.ApproximatePersistedBytes));
        }

        [Test]
        public void NoProxySurfaceSampling_UsesDeterministicBoundedTriangleStride()
        {
            int maximum = DungeonPortalReceiverBounceBaker
                .GetNoProxyEffectMaximumTrianglesPerRendererForTest();
            Assert.That(maximum, Is.EqualTo(4096));
            Assert.That(
                DungeonPortalReceiverBounceBaker.ComputeNoProxyEffectTriangleStrideForTest(1),
                Is.EqualTo(1));
            Assert.That(
                DungeonPortalReceiverBounceBaker.ComputeNoProxyEffectTriangleStrideForTest(maximum),
                Is.EqualTo(1));
            Assert.That(
                DungeonPortalReceiverBounceBaker.ComputeNoProxyEffectTriangleStrideForTest(maximum + 1),
                Is.EqualTo(2));
            Assert.That(
                DungeonPortalReceiverBounceBaker.ComputeNoProxyEffectTriangleStrideForTest(maximum * 4),
                Is.EqualTo(4));

            int triangleCount = maximum * 9 + 17;
            int stride = DungeonPortalReceiverBounceBaker
                .ComputeNoProxyEffectTriangleStrideForTest(triangleCount);
            long selected = ((long)triangleCount + stride - 1L) / stride;
            Assert.That(selected, Is.InRange(1L, (long)maximum));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DungeonPortalReceiverBounceBaker.ComputeNoProxyEffectTriangleStrideForTest(0));
        }

        [Test]
        public void NoProxySurfaceSampling_AcceptsUnityPaddedNegativeOffsetButRejectsInvalidScaleOrNaN()
        {
            Assert.That(
                DungeonPortalReceiverBounceBaker.IsNoProxyEffectLightmapScaleOffsetValidForTest(
                    new Vector4(0.560757f, 0.560757f, 0.26784268f, -0.004380914f)),
                Is.True,
                "Unity may emit a slightly negative padded chart offset; actual mapped UV2 samples own the range check.");
            Assert.That(
                DungeonPortalReceiverBounceBaker.IsNoProxyEffectLightmapScaleOffsetValidForTest(
                    new Vector4(0f, 0.5f, 0.25f, 0.25f)),
                Is.False);
            Assert.That(
                DungeonPortalReceiverBounceBaker.IsNoProxyEffectLightmapScaleOffsetValidForTest(
                    new Vector4(0.5f, 0.5f, float.NaN, 0.25f)),
                Is.False);

            Vector2 beyondOneRectangle = DungeonPortalReceiverBounceBaker
                .MapNoProxyEffectMeshUvToAtlasForTest(
                    new Vector2(0.5f, 0.5f),
                    new Vector4(0.560757f, 0.560757f, 0.5422568f, -0.004380914f));
            Assert.That(beyondOneRectangle.x, Is.InRange(0f, 1f));
            Assert.That(beyondOneRectangle.y, Is.InRange(0f, 1f));

            Vector2 negativeOffsets = DungeonPortalReceiverBounceBaker
                .MapNoProxyEffectMeshUvToAtlasForTest(
                    new Vector2(0.5f, 0.5f),
                    new Vector4(0.560757f, 0.560757f, -0.006571371f, -0.004380914f));
            Assert.That(negativeOffsets.x, Is.InRange(0f, 1f));
            Assert.That(negativeOffsets.y, Is.InRange(0f, 1f));
            Assert.Throws<InvalidOperationException>(() =>
                DungeonPortalReceiverBounceBaker.MapNoProxyEffectMeshUvToAtlasForTest(
                    Vector2.zero,
                    new Vector4(0.560757f, 0.560757f, -0.006571371f, -0.004380914f)));
        }

        [Test]
        public void D025_PoseAwareStencil_ReproducesRejectedDoorOverlapAndKeepsAllSamplesClear()
        {
            OpenCleanSourceSceneForSelectionTest();
            DungeonPortalReceiverBounceBaker.DoorAngleProbeStencilDiagnostic d025 =
                DungeonPortalReceiverBounceBaker.InspectStartAngleProbeStencilForTest(0.25f);
            DungeonPortalReceiverBounceBaker.DoorAngleProbeStencilDiagnostic d050 =
                DungeonPortalReceiverBounceBaker.InspectStartAngleProbeStencilForTest(0.5f);
            DungeonPortalReceiverBounceBaker.DoorAngleProbeStencilDiagnostic d075 =
                DungeonPortalReceiverBounceBaker.InspectStartAngleProbeStencilForTest(0.75f);
            DungeonPortalReceiverBounceBaker.DoorAngleProbeStencilDiagnostic d100 =
                DungeonPortalReceiverBounceBaker.InspectStartAngleProbeStencilForTest(1f);

            Assert.That(d025.LegacyRejectedPointOverlapsDoor, Is.True);
            Assert.That(d025.LegacyRejectedColliderName, Is.EqualTo("Door_01"));
            Assert.That(d025.SampleCount, Is.EqualTo(27));
            Assert.That(d025.LocalPositionSignature, Is.Not.Empty);
            Assert.That(d025.NearDepth, Is.LessThan(-0.25f));
            Assert.That(d025.ValidatedCollisionRadius, Is.EqualTo(0.05f));
            Assert.That(d050.SampleCount, Is.EqualTo(27));
            Assert.That(d050.LocalPositionSignature, Is.Not.EqualTo(d025.LocalPositionSignature));
            Assert.That(d075.SampleCount, Is.EqualTo(27));
            Assert.That(d100.SampleCount, Is.EqualTo(27));
            Assert.That(
                new[]
                {
                    d025.LocalPositionSignature,
                    d050.LocalPositionSignature,
                    d075.LocalPositionSignature,
                    d100.LocalPositionSignature
                },
                Is.Unique);
        }

        [Test]
        public void EditorSessionSnapshot_RestoresExactSelectionByGlobalObjectIdAcrossSceneReopen()
        {
            Scene source = OpenCleanSourceSceneForSelectionTest();
            Selection.activeObject = null;
            DungeonPortalReceiverBounceBaker.EditorSessionSnapshotTestHandle selectedSnapshot = null;
            try
            {
                GameObject[] roots = source.GetRootGameObjects();
                Assert.That(roots, Is.Not.Empty);
                GameObject selected = roots[0];
                Selection.activeObject = selected;
                string expectedGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(selected).ToString();
                int originalInstanceId = selected.GetInstanceID();

                selectedSnapshot = DungeonPortalReceiverBounceBaker.CaptureEditorSessionSnapshotForTest();
                Scene reopened = EditorSceneManager.OpenScene(source.path, OpenSceneMode.Single);
                Assert.That(reopened.IsValid() && reopened.isLoaded && !reopened.isDirty, Is.True);
                Assert.That(GlobalObjectId.TryParse(expectedGlobalId, out GlobalObjectId parsed), Is.True);
                UnityEngine.Object reloaded = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed);
                Assert.That(reloaded, Is.Not.Null);
                Assert.That(reloaded.GetInstanceID(), Is.Not.EqualTo(originalInstanceId));
                Selection.activeObject = null;

                DungeonPortalReceiverBounceBaker.EditorSessionSnapshotTestHandle restoreHandle = selectedSnapshot;
                selectedSnapshot = null;
                restoreHandle.RestoreAndAssert();
                Assert.That(Selection.activeObject, Is.Not.Null);
                Assert.That(
                    GlobalObjectId.GetGlobalObjectIdSlow(Selection.activeObject).ToString(),
                    Is.EqualTo(expectedGlobalId));
            }
            finally
            {
                if (selectedSnapshot != null)
                    selectedSnapshot.RestoreAndAssert();
                Selection.activeObject = null;
                Scene restored = SceneManager.GetActiveScene();
                Assert.That(restored.path, Is.EqualTo(SourceScenePath));
                Assert.That(restored.isDirty, Is.False);
            }
        }

        [Test]
        public void EditorSessionSnapshot_RejectsTransientSelectionWithoutStableGlobalObjectId()
        {
            Scene source = OpenCleanSourceSceneForSelectionTest();
            ScriptableObject transient = ScriptableObject.CreateInstance<DungeonPortalBakedBasisDoorAngleCapture>();
            try
            {
                Selection.activeObject = transient;
                Assert.Throws<InvalidOperationException>(() =>
                    DungeonPortalReceiverBounceBaker.CaptureEditorSessionSnapshotForTest());
            }
            finally
            {
                Selection.activeObject = null;
                UnityEngine.Object.DestroyImmediate(transient);
                Assert.That(source.isDirty, Is.False);
            }
        }

        private static Scene OpenCleanSourceSceneForSelectionTest()
        {
            Scene source = SceneManager.GetSceneByPath(SourceScenePath);
            if (!source.IsValid() || !source.isLoaded)
                source = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
            Assert.That(source.IsValid() && source.isLoaded && !source.isDirty, Is.True);
            if (SceneManager.GetActiveScene() != source)
                Assert.That(EditorSceneManager.SetActiveScene(source), Is.True);
            return source;
        }

        private static void AssertNoProxyCorrespondenceRejected(
            DungeonPortalBakedBasisDoorAngleCapture.AngleState proxy,
            DungeonPortalBakedBasisDoorAngleCapture.AngleState changed)
        {
            Assert.That(
                DungeonPortalBakedBasisDoorAngleCapture.TryValidateNoProxyReceiverCorrespondence(
                    proxy,
                    changed,
                    "same-layout-free-parity",
                    "same-layout-free-parity",
                    out _),
                Is.False);
        }

        private static DungeonPortalBakedBasisDoorAngleCapture.AngleState MakeState(
            string stateId,
            float fraction,
            bool injectorEnabled,
            float bounce)
        {
            var probes = new DungeonPortalReceiverResponseCapture.ProbeSample[27];
            for (int i = 0; i < probes.Length; i++)
            {
                probes[i] = new DungeonPortalReceiverResponseCapture.ProbeSample
                {
                    probeIndex = i,
                    localPosition = new Vector3(i, i * 0.1f, i * -0.1f)
                };
            }
            return new DungeonPortalBakedBasisDoorAngleCapture.AngleState
            {
                captured = true,
                stateId = stateId,
                openFraction = fraction,
                angleDegrees = 90f * fraction,
                injectorEnabled = injectorEnabled,
                injectorBounceIntensity = bounce,
                elapsedSeconds = 1d,
                capturedUtcIso8601 = "2026-08-24T00:00:00.0000000Z",
                stateFolderPath = DungeonPortalBakedBasisDoorAngleCapture.OwnedAssetRoot +
                                  "/StartRoom_R000/States/" + stateId,
                stateHash = "state-hash",
                receiverLayoutSignature = "receiver-layout",
                fullRendererParitySignature = "full-parity",
                doorPresentationParityVerified = true,
                probeSampleCount = probes.Length,
                probeLocalPositionSignature = "pose-stencil-" + fraction.ToString("F2"),
                probeStencilPolicy = "pose-aware-test-policy",
                injector = new DungeonPortalReceiverResponseCapture.InjectorProvenance
                {
                    enabled = injectorEnabled,
                    bounceIntensity = bounce,
                    lightmapBakeType = LightmapBakeType.Baked
                },
                lightmaps = new[]
                {
                    new DungeonPortalReceiverResponseCapture.CaptureLightmap
                    {
                        sourceLightmapIndex = 2,
                        colorWidth = 64,
                        colorHeight = 64,
                        colorFormat = "RGBAHalf",
                        directionWidth = 64,
                        directionHeight = 64,
                        directionFormat = "RGBAHalf",
                        hasShadowMask = false,
                        shadowMaskWidth = 0,
                        shadowMaskHeight = 0,
                        shadowMaskFormat = string.Empty
                    }
                },
                renderers = new[]
                {
                    new DungeonPortalReceiverResponseCapture.CaptureRenderer
                    {
                        relativePath = "Walls[0]/Wall[0]",
                        rendererBucketIndex = 0,
                        componentOrdinal = 0,
                        meshAssetGuid = "mesh-guid",
                        meshLocalId = 123,
                        meshUv2Hash = "uv2",
                        lightmapIndex = 2,
                        lightmapScaleOffset = new Vector4(0.5f, 0.5f, 0.25f, 0.25f)
                    }
                },
                probes = probes,
                fullRendererInventory = new[]
                {
                    new DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry
                    {
                        scope = "ReceiverRoom",
                        relativePath = "Walls[0]/Wall[0]",
                        rendererType = "UnityEngine.MeshRenderer",
                        componentOrdinal = 0,
                        canonicalBucket = "ReceiverRoom|Walls[0]/Wall[0]|UnityEngine.MeshRenderer|0",
                        parityMetadataHash = "production-surface-parity"
                    },
                    new DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry
                    {
                        scope = "RealDoor",
                        relativePath = "Door_01[0]",
                        rendererType = "UnityEngine.MeshRenderer",
                        componentOrdinal = 0,
                        canonicalBucket = "RealDoor|Door_01[0]|UnityEngine.MeshRenderer|0",
                        parityMetadataHash = "door-pose-specific-parity"
                    }
                }
            };
        }

        private static DungeonPortalBakedBasisDoorAngleCapture.CaptureProvenance MakeProvenance()
        {
            string root = DungeonPortalBakedBasisDoorAngleCapture.OwnedAssetRoot + "/StartRoom_R000";
            return new DungeonPortalBakedBasisDoorAngleCapture.CaptureProvenance
            {
                toolVersion = "test",
                unityVersion = Application.unityVersion,
                receiverRoomId = "StartRoom_R000",
                stableDoorwayId = "Doorways[5]/Door_SM_A[0]/DoorWayPoint[0]",
                workspaceScenePath = root + "/Workspace/Test.unity",
                canonicalWorkspaceDependencyHash = "workspace-hash",
                lightingSettingsClonePath = root + "/Lighting/Test.lighting",
                lightingSettingsCloneDependencyHash = "lighting-hash",
                capturedUtcIso8601 = "2026-08-24T00:00:00.0000000Z",
                productionInputs = new[]
                {
                    new DungeonPortalReceiverResponseCapture.AssetFingerprint
                    {
                        assetPath = "Assets/fake.prefab",
                        dependencyHash = "fake-hash"
                    }
                }
            };
        }

        private static DungeonPortalBakedBasisDoorAngleCapture.DoorProxyProvenance MakeProxyProvenance()
        {
            return new DungeonPortalBakedBasisDoorAngleCapture.DoorProxyProvenance
            {
                proxyPolicy = "test fixed-angle transient proxy",
                sourceMeshAssetPath = "Assets/fake.fbx",
                sourceMeshGuid = "mesh-guid",
                sourceMeshLocalId = 123,
                sourceMeshDependencyHash = "mesh-hash",
                sourceMaterialAssetPaths = new[] { "Assets/fake.mat" },
                sourceMaterialDependencyHashes = new[] { "material-hash" },
                shadowCastingMode = ShadowCastingMode.ShadowsOnly,
                staticEditorFlags = (int)(StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic),
                scaleInLightmap = 0f,
                materialPropertyBlockVerifiedEmpty = true,
                transientAndAbsentFromCapturedInventories = true
            };
        }

        private static DungeonPortalBakedBasisDoorAngleCapture.NoProxyEffectProof MakeEffectProof()
        {
            return new DungeonPortalBakedBasisDoorAngleCapture.NoProxyEffectProof
            {
                evaluated = true,
                passed = true,
                stateId = DungeonPortalBakedBasisDoorAngleCapture.NoProxyProofStateId,
                openFraction = 0.5f,
                angleDegrees = 45f,
                sampledTexelCount = 1000,
                changedTexelCount = 10,
                changedTexelFraction = 0.01f,
                meanAbsoluteRgbDifference = 0.001f,
                maximumAbsoluteRgbDifference = 0.1f,
                requiredChangedTexelFraction = 0.001f,
                requiredMeanAbsoluteRgbDifference = 0.000001f,
                requiredMaximumAbsoluteRgbDifference = 0.001f,
                comparisonPolicy = "test comparison"
            };
        }
    }
}
