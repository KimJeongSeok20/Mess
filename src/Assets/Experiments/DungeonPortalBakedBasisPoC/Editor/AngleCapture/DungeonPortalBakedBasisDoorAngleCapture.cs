using System;
using System.Collections.Generic;
using DungeonPortalTransportPoC;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonPortalTransportPoC.Editor
{
    /// <summary>
    /// Editor-only, immutable-by-convention source evidence for one room-local doorway.
    /// It deliberately lives outside the legacy schema-7 receiver-capture asset so the
    /// accepted Baseline/DirectOnly/Full evidence is never rewritten or reinterpreted.
    /// </summary>
    public sealed class DungeonPortalBakedBasisDoorAngleCapture : ScriptableObject
    {
        public const int CurrentSchemaVersion = 2;
        public const string OwnedAssetRoot =
            "Assets/Experiments/DungeonPortalBakedBasisPoC/AngleCapture/Generated";
        public const string EndpointNativeV2OwnedAssetRoot =
            "Assets/Experiments/DungeonPortalBakedBasisPoC/AngleCapture/GeneratedV2";
        public const string BaselineStateId = "D000_Baseline";
        public const string NoProxyProofStateId = "D050_NoProxyControl";

        [Serializable]
        public struct CaptureProvenance
        {
            public string toolVersion;
            public string unityVersion;
            public string receiverRoomId;
            public string stableDoorwayId;
            public string workspaceScenePath;
            public string canonicalWorkspaceDependencyHash;
            public string lightingSettingsClonePath;
            public string lightingSettingsCloneDependencyHash;
            public string capturedUtcIso8601;
            public DungeonPortalReceiverResponseCapture.AssetFingerprint[] productionInputs;
        }

        [Serializable]
        public struct DoorProxyProvenance
        {
            public string proxyPolicy;
            public string sourceMeshAssetPath;
            public string sourceMeshGuid;
            public long sourceMeshLocalId;
            public string sourceMeshDependencyHash;
            public string[] sourceMaterialAssetPaths;
            public string[] sourceMaterialDependencyHashes;
            public ShadowCastingMode shadowCastingMode;
            public int staticEditorFlags;
            public float scaleInLightmap;
            public bool materialPropertyBlockVerifiedEmpty;
            public bool transientAndAbsentFromCapturedInventories;
        }

        [Serializable]
        public struct AngleState
        {
            public bool captured;
            public string stateId;
            public float openFraction;
            public float angleDegrees;
            public bool injectorEnabled;
            public float injectorBounceIntensity;
            public double elapsedSeconds;
            public string capturedUtcIso8601;
            public string stateFolderPath;
            public string stateHash;
            public string receiverLayoutSignature;
            public string fullRendererParitySignature;
            public bool doorPresentationParityVerified;
            public int probeSampleCount;
            public string probeLocalPositionSignature;
            public string probeStencilPolicy;
            public DungeonPortalReceiverResponseCapture.InjectorProvenance injector;
            public DungeonPortalReceiverResponseCapture.CaptureLightmap[] lightmaps;
            public DungeonPortalReceiverResponseCapture.CaptureRenderer[] renderers;
            public DungeonPortalReceiverResponseCapture.ProbeSample[] probes;
            public DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] fullRendererInventory;
        }

        [Serializable]
        public struct NoProxyEffectProof
        {
            public bool evaluated;
            public bool passed;
            public string stateId;
            public float openFraction;
            public float angleDegrees;
            public int sampledTexelCount;
            public int changedTexelCount;
            public float changedTexelFraction;
            public float meanAbsoluteRgbDifference;
            public float maximumAbsoluteRgbDifference;
            public float requiredChangedTexelFraction;
            public float requiredMeanAbsoluteRgbDifference;
            public float requiredMaximumAbsoluteRgbDifference;
            public string comparisonPolicy;
        }

        [SerializeField] private int schemaVersion = CurrentSchemaVersion;
        [SerializeField] private string receiverRoomId;
        [SerializeField] private string stableDoorwayId;
        [SerializeField] private float canonicalOpenAngleDegrees = 90f;
        [SerializeField] private CaptureProvenance provenance;
        [SerializeField] private DoorProxyProvenance proxyProvenance;
        [SerializeField] private AngleState baseline;
        [SerializeField] private AngleState[] matchedBaselineAngleStates = Array.Empty<AngleState>();
        [SerializeField] private AngleState[] fullAngleStates = Array.Empty<AngleState>();
        [SerializeField] private NoProxyEffectProof noProxyEffectProof;

        public int SchemaVersion => schemaVersion;
        public string ReceiverRoomId => receiverRoomId;
        public string StableDoorwayId => stableDoorwayId;
        public float CanonicalOpenAngleDegrees => canonicalOpenAngleDegrees;
        public CaptureProvenance Provenance => provenance;
        public DoorProxyProvenance ProxyProvenance => proxyProvenance;
        public AngleState Baseline => CloneState(baseline);
        public AngleState[] MatchedBaselineAngleStates => CloneStates(matchedBaselineAngleStates);
        public AngleState[] FullAngleStates => CloneStates(fullAngleStates);
        public NoProxyEffectProof EffectProof => noProxyEffectProof;

        public void ConfigureAuthoring(
            string roomId,
            string doorwayId,
            float openAngleDegrees,
            CaptureProvenance captureProvenance,
            DoorProxyProvenance doorProxyProvenance,
            AngleState baselineState,
            AngleState[] matchedBaselineStates,
            AngleState[] fullStates,
            NoProxyEffectProof effectProof)
        {
            schemaVersion = CurrentSchemaVersion;
            receiverRoomId = roomId != null ? roomId.Trim() : string.Empty;
            stableDoorwayId = doorwayId != null ? doorwayId.Trim() : string.Empty;
            canonicalOpenAngleDegrees = openAngleDegrees;
            provenance = CloneProvenance(captureProvenance);
            proxyProvenance = CloneProxyProvenance(doorProxyProvenance);
            baseline = CloneState(baselineState);
            matchedBaselineAngleStates = CloneStates(matchedBaselineStates);
            fullAngleStates = CloneStates(fullStates);
            noProxyEffectProof = effectProof;
        }

        public bool TryValidate(out string failure)
        {
            if (schemaVersion != CurrentSchemaVersion ||
                string.IsNullOrWhiteSpace(receiverRoomId) ||
                string.IsNullOrWhiteSpace(stableDoorwayId) ||
                !IsFinite(canonicalOpenAngleDegrees) || canonicalOpenAngleDegrees <= 0f)
            {
                failure = "Angle-capture identity/schema is incomplete.";
                return false;
            }

            if (!TryValidateProvenance(provenance, receiverRoomId, stableDoorwayId, out failure) ||
                !TryValidateProxy(proxyProvenance, out failure) ||
                !TryValidateState(baseline, BaselineStateId, 0f, 0f, false, 0f, out failure))
            {
                return false;
            }

            AngleState[] matchedBaselines = matchedBaselineAngleStates ?? Array.Empty<AngleState>();
            AngleState[] states = fullAngleStates ?? Array.Empty<AngleState>();
            float[] expectedFractions = { 0.25f, 0.5f, 0.75f, 1f };
            if (matchedBaselines.Length != expectedFractions.Length ||
                states.Length != expectedFractions.Length)
            {
                failure = "Exactly four same-pose Baseline/Full pairs D025/D050/D075/D100 are required.";
                return false;
            }

            for (int i = 0; i < states.Length; i++)
            {
                string poseId = "D" + Mathf.RoundToInt(expectedFractions[i] * 100f)
                    .ToString("000");
                string expectedBaselineId = poseId + "_Baseline";
                string expectedFullId = poseId + "_Full";
                if (!TryValidateState(
                        matchedBaselines[i],
                        expectedBaselineId,
                        expectedFractions[i],
                        canonicalOpenAngleDegrees * expectedFractions[i],
                        false,
                        0f,
                        out failure) ||
                    !TryValidateState(
                        states[i],
                        expectedFullId,
                        expectedFractions[i],
                        canonicalOpenAngleDegrees * expectedFractions[i],
                        true,
                        1f,
                        out failure) ||
                    !TryValidateReceiverLayout(baseline, matchedBaselines[i], out failure) ||
                    !TryValidateReceiverLayout(baseline, states[i], out failure) ||
                    !TryValidateMatchedPosePair(matchedBaselines[i], states[i], out failure))
                {
                    return false;
                }
            }

            if (!TryValidateEffectProof(noProxyEffectProof, canonicalOpenAngleDegrees, out failure))
                return false;

            failure = string.Empty;
            return true;
        }

        public static bool TryValidateReceiverLayout(
            AngleState authoritative,
            AngleState comparison,
            out string failure)
        {
            DungeonPortalReceiverResponseCapture.CaptureLightmap[] leftMaps =
                authoritative.lightmaps ?? Array.Empty<DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            DungeonPortalReceiverResponseCapture.CaptureLightmap[] rightMaps =
                comparison.lightmaps ?? Array.Empty<DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            if (leftMaps.Length == 0 || leftMaps.Length != rightMaps.Length)
            {
                failure = "Receiver lightmap count differs from D000 Baseline.";
                return false;
            }
            for (int i = 0; i < leftMaps.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.CaptureLightmap left = leftMaps[i];
                DungeonPortalReceiverResponseCapture.CaptureLightmap right = rightMaps[i];
                if (left.sourceLightmapIndex != right.sourceLightmapIndex ||
                    left.colorWidth != right.colorWidth || left.colorHeight != right.colorHeight ||
                    left.directionWidth != right.directionWidth || left.directionHeight != right.directionHeight ||
                    left.hasShadowMask != right.hasShadowMask ||
                    left.shadowMaskWidth != right.shadowMaskWidth || left.shadowMaskHeight != right.shadowMaskHeight ||
                    !string.Equals(left.colorFormat, right.colorFormat, StringComparison.Ordinal) ||
                    !string.Equals(left.directionFormat, right.directionFormat, StringComparison.Ordinal) ||
                    !string.Equals(left.shadowMaskFormat, right.shadowMaskFormat, StringComparison.Ordinal))
                {
                    failure = "Receiver lightmap layout differs at atlas " + i + ".";
                    return false;
                }
            }

            DungeonPortalReceiverResponseCapture.CaptureRenderer[] leftRenderers =
                authoritative.renderers ?? Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] rightRenderers =
                comparison.renderers ?? Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            if (leftRenderers.Length == 0 || leftRenderers.Length != rightRenderers.Length)
            {
                failure = "Receiver renderer count differs from D000 Baseline.";
                return false;
            }
            for (int i = 0; i < leftRenderers.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.CaptureRenderer left = leftRenderers[i];
                DungeonPortalReceiverResponseCapture.CaptureRenderer right = rightRenderers[i];
                if (!string.Equals(left.relativePath, right.relativePath, StringComparison.Ordinal) ||
                    left.rendererBucketIndex != right.rendererBucketIndex ||
                    left.componentOrdinal != right.componentOrdinal ||
                    !string.Equals(left.meshAssetGuid, right.meshAssetGuid, StringComparison.Ordinal) ||
                    left.meshLocalId != right.meshLocalId ||
                    !string.Equals(left.meshUv2Hash, right.meshUv2Hash, StringComparison.Ordinal) ||
                    left.lightmapIndex != right.lightmapIndex || left.lightmapScaleOffset != right.lightmapScaleOffset)
                {
                    failure = "Receiver renderer identity/index/ST differs at entry " + i + ".";
                    return false;
                }
            }

            if (!TryValidateReceiverInventoryParity(authoritative, comparison, out failure))
                return false;

            failure = string.Empty;
            return true;
        }

        /// <summary>
        /// D050 no-proxy proof only. A transient ContributeGI renderer may legitimately
        /// repack the receiver atlases when it is removed, so this validator preserves
        /// receiver identity while deliberately treating atlas index, ST, and dimensions
        /// as per-bake data. The independent layout-free parity signatures cover every
        /// renderer/material field except those two Unity lightmap layout fields.
        /// </summary>
        public static bool TryValidateNoProxyReceiverCorrespondence(
            AngleState proxyState,
            AngleState noProxyState,
            string layoutFreeParityBeforeBake,
            string layoutFreeParityAfterBake,
            out string failure)
        {
            if (string.IsNullOrWhiteSpace(layoutFreeParityBeforeBake) ||
                string.IsNullOrWhiteSpace(layoutFreeParityAfterBake) ||
                !string.Equals(
                    layoutFreeParityBeforeBake,
                    layoutFreeParityAfterBake,
                    StringComparison.Ordinal))
            {
                failure = "D050 no-proxy receiver layout-free renderer/material parity changed during bake.";
                return false;
            }

            DungeonPortalReceiverResponseCapture.CaptureRenderer[] proxyRenderers =
                CloneAndSortNoProxyRenderers(proxyState.renderers);
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] noProxyRenderers =
                CloneAndSortNoProxyRenderers(noProxyState.renderers);
            if (proxyRenderers.Length == 0 || proxyRenderers.Length != noProxyRenderers.Length)
            {
                failure = "D050 no-proxy receiver renderer cardinality differs from its proxy bake.";
                return false;
            }

            if (!TryValidateNoProxyLayoutReferences(proxyState, proxyRenderers, "proxy", out failure) ||
                !TryValidateNoProxyLayoutReferences(noProxyState, noProxyRenderers, "no-proxy", out failure))
            {
                return false;
            }

            for (int i = 0; i < proxyRenderers.Length; i++)
            {
                if (i > 0 &&
                    CompareNoProxyRendererIdentity(proxyRenderers[i - 1], proxyRenderers[i]) == 0)
                {
                    failure = "D050 proxy receiver identity is duplicated at sorted entry " + i + ".";
                    return false;
                }
                if (i > 0 &&
                    CompareNoProxyRendererIdentity(noProxyRenderers[i - 1], noProxyRenderers[i]) == 0)
                {
                    failure = "D050 no-proxy receiver identity is duplicated at sorted entry " + i + ".";
                    return false;
                }
                if (CompareNoProxyRendererIdentity(proxyRenderers[i], noProxyRenderers[i]) != 0)
                {
                    failure = "D050 no-proxy receiver identity/mesh/UV2 differs at sorted entry " + i + ".";
                    return false;
                }
            }

            if (!TryValidateNoProxyReceiverInventoryIdentity(proxyState, noProxyState, out failure))
                return false;

            failure = string.Empty;
            return true;
        }

        public static bool TryValidateMatchedPosePair(
            AngleState baselineState,
            AngleState fullState,
            out string failure)
        {
            if (!Approximately(baselineState.openFraction, fullState.openFraction) ||
                !Approximately(baselineState.angleDegrees, fullState.angleDegrees) ||
                baselineState.injectorEnabled || fullState.injectorEnabled == false ||
                !Approximately(baselineState.injectorBounceIntensity, 0f) ||
                !Approximately(fullState.injectorBounceIntensity, 1f) ||
                baselineState.probeSampleCount != 27 || fullState.probeSampleCount != 27 ||
                baselineState.probeSampleCount != fullState.probeSampleCount ||
                string.IsNullOrWhiteSpace(baselineState.probeLocalPositionSignature) ||
                !string.Equals(
                    baselineState.probeLocalPositionSignature,
                    fullState.probeLocalPositionSignature,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    baselineState.probeStencilPolicy,
                    fullState.probeStencilPolicy,
                    StringComparison.Ordinal))
            {
                failure = "Matched Baseline/Full states do not share one exact door pose and probe stencil.";
                return false;
            }

            DungeonPortalReceiverResponseCapture.ProbeSample[] left = baselineState.probes ??
                Array.Empty<DungeonPortalReceiverResponseCapture.ProbeSample>();
            DungeonPortalReceiverResponseCapture.ProbeSample[] right = fullState.probes ??
                Array.Empty<DungeonPortalReceiverResponseCapture.ProbeSample>();
            if (left.Length != baselineState.probeSampleCount || right.Length != fullState.probeSampleCount)
            {
                failure = "Matched Baseline/Full probe payload count differs from its recorded count.";
                return false;
            }
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i].probeIndex != i || right[i].probeIndex != i ||
                    left[i].localPosition != right[i].localPosition)
                {
                    failure = "Matched Baseline/Full doorway probe layout differs at entry " + i + ".";
                    return false;
                }
            }
            if (!TryValidateReceiverLayout(baselineState, fullState, out failure))
                return false;

            failure = string.Empty;
            return true;
        }

        public static bool TryValidateEffectProof(
            NoProxyEffectProof proof,
            float canonicalOpenAngleDegrees,
            out string failure)
        {
            if (!proof.evaluated || !proof.passed ||
                !string.Equals(proof.stateId, NoProxyProofStateId, StringComparison.Ordinal) ||
                !Approximately(proof.openFraction, 0.5f) ||
                !Approximately(proof.angleDegrees, canonicalOpenAngleDegrees * 0.5f) ||
                proof.sampledTexelCount <= 0 || proof.changedTexelCount <= 0 ||
                proof.changedTexelCount > proof.sampledTexelCount ||
                !IsFinite(proof.changedTexelFraction) ||
                !IsFinite(proof.meanAbsoluteRgbDifference) ||
                !IsFinite(proof.maximumAbsoluteRgbDifference) ||
                proof.changedTexelFraction < proof.requiredChangedTexelFraction ||
                proof.meanAbsoluteRgbDifference < proof.requiredMeanAbsoluteRgbDifference ||
                proof.maximumAbsoluteRgbDifference < proof.requiredMaximumAbsoluteRgbDifference ||
                string.IsNullOrWhiteSpace(proof.comparisonPolicy))
            {
                failure = "The D050 ShadowsOnly-proxy versus no-proxy effect proof is absent or below threshold.";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private static bool TryValidateProvenance(
            CaptureProvenance value,
            string roomId,
            string doorwayId,
            out string failure)
        {
            if (string.IsNullOrWhiteSpace(value.toolVersion) ||
                string.IsNullOrWhiteSpace(value.unityVersion) ||
                !string.Equals(value.receiverRoomId, roomId, StringComparison.Ordinal) ||
                !string.Equals(value.stableDoorwayId, doorwayId, StringComparison.Ordinal) ||
                !IsOwnedPath(value.workspaceScenePath) ||
                string.IsNullOrWhiteSpace(value.canonicalWorkspaceDependencyHash) ||
                !IsOwnedPath(value.lightingSettingsClonePath) ||
                string.IsNullOrWhiteSpace(value.lightingSettingsCloneDependencyHash) ||
                string.IsNullOrWhiteSpace(value.capturedUtcIso8601) ||
                (value.productionInputs ?? Array.Empty<DungeonPortalReceiverResponseCapture.AssetFingerprint>()).Length == 0)
            {
                failure = "Angle-capture provenance is incomplete.";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private static bool TryValidateProxy(DoorProxyProvenance value, out string failure)
        {
            string[] materials = value.sourceMaterialAssetPaths ?? Array.Empty<string>();
            string[] materialHashes = value.sourceMaterialDependencyHashes ?? Array.Empty<string>();
            StaticEditorFlags expectedStaticFlags =
                StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic;
            if (string.IsNullOrWhiteSpace(value.proxyPolicy) ||
                string.IsNullOrWhiteSpace(value.sourceMeshAssetPath) ||
                string.IsNullOrWhiteSpace(value.sourceMeshGuid) || value.sourceMeshLocalId == 0L ||
                string.IsNullOrWhiteSpace(value.sourceMeshDependencyHash) ||
                materials.Length == 0 || materials.Length != materialHashes.Length ||
                value.shadowCastingMode != ShadowCastingMode.ShadowsOnly ||
                value.staticEditorFlags != (int)expectedStaticFlags ||
                value.scaleInLightmap != 0f ||
                !value.materialPropertyBlockVerifiedEmpty ||
                !value.transientAndAbsentFromCapturedInventories)
            {
                failure = "Transient ShadowsOnly GI door-proxy provenance is incomplete.";
                return false;
            }
            for (int i = 0; i < materials.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(materials[i]) || string.IsNullOrWhiteSpace(materialHashes[i]))
                {
                    failure = "Door-proxy material provenance is incomplete at slot " + i + ".";
                    return false;
                }
            }
            failure = string.Empty;
            return true;
        }

        private static bool TryValidateState(
            AngleState state,
            string expectedId,
            float expectedFraction,
            float expectedAngle,
            bool expectedInjectorEnabled,
            float expectedBounce,
            out string failure)
        {
            if (!state.captured || !string.Equals(state.stateId, expectedId, StringComparison.Ordinal) ||
                !Approximately(state.openFraction, expectedFraction) ||
                !Approximately(state.angleDegrees, expectedAngle) ||
                state.injectorEnabled != expectedInjectorEnabled ||
                !Approximately(state.injectorBounceIntensity, expectedBounce) ||
                state.injector.enabled != expectedInjectorEnabled ||
                !Approximately(state.injector.bounceIntensity, expectedBounce) ||
                state.injector.lightmapBakeType != LightmapBakeType.Baked ||
                !IsFinite((float)state.elapsedSeconds) || state.elapsedSeconds < 0d ||
                string.IsNullOrWhiteSpace(state.capturedUtcIso8601) ||
                !IsOwnedPath(state.stateFolderPath) ||
                string.IsNullOrWhiteSpace(state.stateHash) ||
                string.IsNullOrWhiteSpace(state.receiverLayoutSignature) ||
                string.IsNullOrWhiteSpace(state.fullRendererParitySignature) ||
                !state.doorPresentationParityVerified ||
                state.probeSampleCount != 27 ||
                string.IsNullOrWhiteSpace(state.probeLocalPositionSignature) ||
                string.IsNullOrWhiteSpace(state.probeStencilPolicy) ||
                (state.lightmaps ?? Array.Empty<DungeonPortalReceiverResponseCapture.CaptureLightmap>()).Length == 0 ||
                (state.renderers ?? Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>()).Length == 0 ||
                (state.probes ?? Array.Empty<DungeonPortalReceiverResponseCapture.ProbeSample>()).Length !=
                    state.probeSampleCount ||
                (state.fullRendererInventory ?? Array.Empty<DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry>()).Length == 0)
            {
                failure = "Angle state '" + expectedId + "' is incomplete or violates its pose/injector contract.";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private static bool TryValidateReceiverInventoryParity(
            AngleState authoritative,
            AngleState comparison,
            out string failure)
        {
            var baselineByBucket = new Dictionary<string, DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry>(
                StringComparer.Ordinal);
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] baselineInventory =
                authoritative.fullRendererInventory ?? Array.Empty<DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry>();
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] comparisonInventory =
                comparison.fullRendererInventory ?? Array.Empty<DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry>();
            for (int i = 0; i < baselineInventory.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry entry = baselineInventory[i];
                if (string.Equals(entry.scope, "ReceiverRoom", StringComparison.Ordinal))
                    baselineByBucket[entry.canonicalBucket] = entry;
            }

            int compared = 0;
            for (int i = 0; i < comparisonInventory.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry right = comparisonInventory[i];
                if (!string.Equals(right.scope, "ReceiverRoom", StringComparison.Ordinal))
                    continue;
                if (!baselineByBucket.TryGetValue(right.canonicalBucket, out DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry left) ||
                    !string.Equals(left.relativePath, right.relativePath, StringComparison.Ordinal) ||
                    !string.Equals(left.rendererType, right.rendererType, StringComparison.Ordinal) ||
                    left.componentOrdinal != right.componentOrdinal ||
                    !string.Equals(left.parityMetadataHash, right.parityMetadataHash, StringComparison.Ordinal))
                {
                    failure = "Production receiver renderer/material parity changed at '" + right.canonicalBucket + "'.";
                    return false;
                }
                compared++;
            }
            if (compared == 0 || compared != baselineByBucket.Count)
            {
                failure = "Production receiver inventory cardinality differs from D000 Baseline.";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private static DungeonPortalReceiverResponseCapture.CaptureRenderer[] CloneAndSortNoProxyRenderers(
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] source)
        {
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] result = source != null
                ? (DungeonPortalReceiverResponseCapture.CaptureRenderer[])source.Clone()
                : Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            Array.Sort(result, CompareNoProxyRendererIdentity);
            return result;
        }

        private static int CompareNoProxyRendererIdentity(
            DungeonPortalReceiverResponseCapture.CaptureRenderer left,
            DungeonPortalReceiverResponseCapture.CaptureRenderer right)
        {
            int result = string.CompareOrdinal(left.relativePath, right.relativePath);
            if (result != 0) return result;
            result = left.rendererBucketIndex.CompareTo(right.rendererBucketIndex);
            if (result != 0) return result;
            result = left.componentOrdinal.CompareTo(right.componentOrdinal);
            if (result != 0) return result;
            result = string.CompareOrdinal(left.meshAssetGuid, right.meshAssetGuid);
            if (result != 0) return result;
            result = left.meshLocalId.CompareTo(right.meshLocalId);
            if (result != 0) return result;
            return string.CompareOrdinal(left.meshUv2Hash, right.meshUv2Hash);
        }

        private static bool TryValidateNoProxyLayoutReferences(
            AngleState state,
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] sortedRenderers,
            string label,
            out string failure)
        {
            DungeonPortalReceiverResponseCapture.CaptureLightmap[] maps = state.lightmaps ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            if (maps.Length == 0)
            {
                failure = "D050 " + label + " receiver lightmap inventory is empty.";
                return false;
            }

            var mapsBySourceIndex = new Dictionary<int, DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            for (int i = 0; i < maps.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.CaptureLightmap map = maps[i];
                bool validShadowMaskDimensions = map.hasShadowMask
                    ? map.shadowMaskWidth > 0 && map.shadowMaskHeight > 0
                    : map.shadowMaskWidth == 0 && map.shadowMaskHeight == 0;
                if (map.sourceLightmapIndex < 0 || map.colorWidth <= 0 || map.colorHeight <= 0 ||
                    map.directionWidth <= 0 || map.directionHeight <= 0 ||
                    !validShadowMaskDimensions || mapsBySourceIndex.ContainsKey(map.sourceLightmapIndex))
                {
                    failure = "D050 " + label + " receiver lightmap record is invalid or duplicated at entry " + i + ".";
                    return false;
                }
                mapsBySourceIndex.Add(map.sourceLightmapIndex, map);
            }

            var referencedIndices = new HashSet<int>();
            for (int i = 0; i < sortedRenderers.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.CaptureRenderer renderer = sortedRenderers[i];
                Vector4 st = renderer.lightmapScaleOffset;
                bool identityValid =
                    !string.IsNullOrWhiteSpace(renderer.relativePath) &&
                    renderer.rendererBucketIndex >= 0 && renderer.componentOrdinal >= 0 &&
                    !string.IsNullOrWhiteSpace(renderer.meshAssetGuid) && renderer.meshLocalId != 0L &&
                    !string.IsNullOrWhiteSpace(renderer.meshUv2Hash);
                bool stValid =
                    IsFinite(st.x) && IsFinite(st.y) && IsFinite(st.z) && IsFinite(st.w) &&
                    st.x > 0f && st.y > 0f;
                bool atlasValid =
                    renderer.lightmapIndex >= 0 &&
                    mapsBySourceIndex.ContainsKey(renderer.lightmapIndex);
                if (!identityValid || !stValid || !atlasValid)
                {
                    failure = "D050 " + label +
                              " receiver identity or per-bake lightmap index/ST is invalid at sorted entry " +
                              i + ". path='" + (renderer.relativePath ?? "<null>") +
                              "' lightmapIndex=" + renderer.lightmapIndex +
                              " ST=(" + st.x + "," + st.y + "," + st.z + "," + st.w +
                              ") identityValid=" + identityValid +
                              " stValid=" + stValid + " atlasValid=" + atlasValid + ".";
                    return false;
                }
                referencedIndices.Add(renderer.lightmapIndex);
            }
            if (referencedIndices.Count != mapsBySourceIndex.Count)
            {
                failure = "D050 " + label + " receiver lightmap inventory contains an unreferenced atlas.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool TryValidateNoProxyReceiverInventoryIdentity(
            AngleState proxyState,
            AngleState noProxyState,
            out string failure)
        {
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] proxy =
                GetSortedReceiverRoomInventory(proxyState.fullRendererInventory);
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] noProxy =
                GetSortedReceiverRoomInventory(noProxyState.fullRendererInventory);
            if (proxy.Length == 0 || proxy.Length != noProxy.Length)
            {
                failure = "D050 no-proxy ReceiverRoom inventory cardinality differs from its proxy bake.";
                return false;
            }

            for (int i = 0; i < proxy.Length; i++)
            {
                if (!IsValidReceiverRoomInventoryIdentity(proxy[i]) ||
                    !IsValidReceiverRoomInventoryIdentity(noProxy[i]) ||
                    CompareReceiverRoomInventoryIdentity(proxy[i], noProxy[i]) != 0)
                {
                    failure = "D050 no-proxy ReceiverRoom inventory identity differs at sorted entry " + i + ".";
                    return false;
                }
                if (i > 0 &&
                    (CompareReceiverRoomInventoryIdentity(proxy[i - 1], proxy[i]) == 0 ||
                     CompareReceiverRoomInventoryIdentity(noProxy[i - 1], noProxy[i]) == 0))
                {
                    failure = "D050 no-proxy ReceiverRoom inventory contains a duplicate identity at sorted entry " + i + ".";
                    return false;
                }
            }

            failure = string.Empty;
            return true;
        }

        private static DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[]
            GetSortedReceiverRoomInventory(
                DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] source)
        {
            source = source ?? Array.Empty<DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry>();
            var result = new List<DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry>();
            for (int i = 0; i < source.Length; i++)
            {
                if (string.Equals(source[i].scope, "ReceiverRoom", StringComparison.Ordinal))
                    result.Add(source[i]);
            }
            result.Sort(CompareReceiverRoomInventoryIdentity);
            return result.ToArray();
        }

        private static int CompareReceiverRoomInventoryIdentity(
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry left,
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry right)
        {
            int result = string.CompareOrdinal(left.canonicalBucket, right.canonicalBucket);
            if (result != 0) return result;
            result = string.CompareOrdinal(left.relativePath, right.relativePath);
            if (result != 0) return result;
            result = string.CompareOrdinal(left.rendererType, right.rendererType);
            if (result != 0) return result;
            return left.componentOrdinal.CompareTo(right.componentOrdinal);
        }

        private static bool IsValidReceiverRoomInventoryIdentity(
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry entry)
        {
            return string.Equals(entry.scope, "ReceiverRoom", StringComparison.Ordinal) &&
                   !string.IsNullOrWhiteSpace(entry.relativePath) &&
                   !string.IsNullOrWhiteSpace(entry.rendererType) &&
                   entry.componentOrdinal >= 0 &&
                   !string.IsNullOrWhiteSpace(entry.canonicalBucket) &&
                   !string.IsNullOrWhiteSpace(entry.parityMetadataHash);
        }

        private static bool IsOwnedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            string normalized = path.Replace('\\', '/');
            return normalized.StartsWith(OwnedAssetRoot + "/", StringComparison.Ordinal) ||
                   normalized.StartsWith(EndpointNativeV2OwnedAssetRoot + "/", StringComparison.Ordinal);
        }

        private static bool Approximately(float left, float right)
        {
            return Mathf.Abs(left - right) <= 0.0001f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static CaptureProvenance CloneProvenance(CaptureProvenance source)
        {
            source.productionInputs = source.productionInputs != null
                ? (DungeonPortalReceiverResponseCapture.AssetFingerprint[])source.productionInputs.Clone()
                : Array.Empty<DungeonPortalReceiverResponseCapture.AssetFingerprint>();
            return source;
        }

        private static DoorProxyProvenance CloneProxyProvenance(DoorProxyProvenance source)
        {
            source.sourceMaterialAssetPaths = source.sourceMaterialAssetPaths != null
                ? (string[])source.sourceMaterialAssetPaths.Clone()
                : Array.Empty<string>();
            source.sourceMaterialDependencyHashes = source.sourceMaterialDependencyHashes != null
                ? (string[])source.sourceMaterialDependencyHashes.Clone()
                : Array.Empty<string>();
            return source;
        }

        private static AngleState[] CloneStates(AngleState[] source)
        {
            if (source == null)
                return Array.Empty<AngleState>();
            var result = new AngleState[source.Length];
            for (int i = 0; i < source.Length; i++)
                result[i] = CloneState(source[i]);
            return result;
        }

        private static AngleState CloneState(AngleState source)
        {
            source.lightmaps = source.lightmaps != null
                ? (DungeonPortalReceiverResponseCapture.CaptureLightmap[])source.lightmaps.Clone()
                : Array.Empty<DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            source.renderers = source.renderers != null
                ? (DungeonPortalReceiverResponseCapture.CaptureRenderer[])source.renderers.Clone()
                : Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            source.probes = source.probes != null
                ? (DungeonPortalReceiverResponseCapture.ProbeSample[])source.probes.Clone()
                : Array.Empty<DungeonPortalReceiverResponseCapture.ProbeSample>();
            source.fullRendererInventory = source.fullRendererInventory != null
                ? (DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[])source.fullRendererInventory.Clone()
                : Array.Empty<DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry>();
            return source;
        }
    }

}
