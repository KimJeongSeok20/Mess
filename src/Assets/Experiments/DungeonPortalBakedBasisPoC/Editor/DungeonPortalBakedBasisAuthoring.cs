using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DungeonPortalTransportPoC;
using DungeonPortalTransportPoC.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace DungeonPortalBakedBasisPoC.Editor
{
    /// <summary>
    /// Builds a quarantined room-local K=1 baked-basis payload.  It never opens, saves,
    /// or mutates the validation scene, production prefabs, Flow, TileSet, MapList, or
    /// source bake assets.  The only writes are a staged payload under this PoC root,
    /// which is promoted transactionally after its generated texture and data contracts
    /// pass verification.
    /// </summary>
    public static class DungeonPortalBakedBasisAuthoring
    {
        private const string Root = "Assets/Experiments/DungeonPortalBakedBasisPoC";
        private const string GeneratedRoot = Root + "/Generated";
        // The first endpoint-native payload is retained as immutable legacy evidence.
        // Actual V2-canonical authoring owns only this new root, so promotion/rollback can
        // never move the prior EndpointNativeGenerated leaves or their scene GUIDs.
        private const string EndpointNativePayloadRootName = "EndpointNativeGeneratedV2";
        private const string EndpointNativeGeneratedRoot = Root + "/" + EndpointNativePayloadRootName;
        // Validation consumes these directly under Generated/<RoomId>/..., so staging is
        // promoted child-by-child rather than introducing an extra runtime-facing folder.
        private const string FinalPayloadFolder = GeneratedRoot;
        private const string ManifestFileName = "DungeonPortalBakedBasisManifest.json";
        private const string EndpointNativeManifestFileName =
            "DungeonPortalBakedBasisEndpointNativeManifest.json";
        private const string EndpointNativeManifestSchema =
            "DungeonPortalBakedBasisEndpointNativeManifest/v2";
        private const string ChartCopyShaderPath = Root + "/Editor/DungeonPortalBakedBasisAtlasChartCopy.shader";
        private const string ChartCopyShaderName = "Hidden/DungeonPortalBakedBasisPoC/AtlasChartCopy";
        private const string ToolVersion = "DungeonPortalBakedBasisAuthoring/14";
        private const string EndpointNativeAngleCaptureToolVersion =
            "DungeonPortalBakedBasisAngleCaptureBaker/3";
        private const string EndpointNativeReceiverPosePolicy =
            "ANGLE_CAPTURE_D025_D050_D075_D100_SAME_POSE_FULL_MINUS_BASELINE_K1";
        private const string EndpointNativeCompactResponseEncoding =
            "NormalizedPositiveNegativeDelta/BC6HOrSelectiveRGBAHalfColor_BC7OrSelectiveRGBAHalfDirectionalMoment/Mip0BoxGeneratedFullMips_CanonicalPositiveZero";
        private const string EndpointNativeEndpointBaseTransitionPolicy =
            "CapturedEndpointLayoutWithOppositeEndpointMip0TriangleRepackedBoxGeneratedFullMips_BC6HOrSelectiveRGBAHalfColor_RGBA32Direction";
        private const string EndpointNativeEndpointResponseMipPolicy =
            "DestinationNativeMip0TriangleRasterizedBoxGeneratedFullMipsNormalizedPositiveNegativeCompact_CanonicalPositiveZero_SelectiveRGBAHalfCompactPairFallbacks";
        private const string EndpointNativeManifestBasePolicy =
            "EXACT_ENDPOINT_REFERENCES_ONLY_AT_CAPTURED_ENDPOINT_AND_D0: P0/P100 source textures, full mip chains, renderer lightmap indices, and ST remain exact. While a door is open, two isolated layouts retain the captured endpoint and repack only the opposite base (P100-to-P0 and P0-to-P100) for a continuous power lerp. Repacked opposite-base atlases rasterize collision-free destination mip0 with a four-texel unowned-only BC block gutter, then generate and independently verify a linear 2x2 box-filtered full mip chain. Opposite-base color uses BC6H when every persisted mip passes and selectively falls back per atlas to linear RGBAHalf when BC6H fails; nonzero low-energy compressed mips may exceed the relative-only RMSE bound only when absolute RMSE is at most 0.001 and worst-channel error is at most 0.05. Opposite-base direction is linear RGBA32 to preserve directional alpha and avoid BC7 block outliers. No lower-mip chart ownership or cross-layout source-mip parity is claimed.";
        private const string EndpointNativeManifestResponseMipPolicy =
            "DESTINATION_NATIVE_COLLISION_FREE_MIP0_BOX_GENERATED_FULL_MIPS_SAME_POSE_FULL_MINUS_BASELINE_NORMALIZED_SIGNED_COMPACT: Baseline/Full are temporary same-pose authoring states. Each response serializes positive/negative normalized magnitudes with per-mip Vector4 scales and canonical positive zero. Color pairs use BC6H and directional-moment pairs use BC7 when persisted reconstruction passes every mip gate; either pair selectively falls back together to linear RGBAHalf when its compressed candidate fails, and the fallback is rechecked by the same gate. Lower response mips are generated once from destination-native mip0 and independently verified against linear 2x2 box downsampling. No lower-mip chart ownership or cross-layout source-mip parity is claimed.";
        private const int RequiredAnglePoseCount = 4;
        // BC6H/BC7 can cross zero by a few 1e-4 for sub-perceptual response values.
        // Reconstruction RMSE/max gates still bound those values; this gate is reserved
        // for a meaningful polarity inversion rather than representable zero noise.
        private const float CompactResponseWrongSignEpsilon = 0.0005f;
        public enum CompactSignClassification
        {
            Preserved = 0,
            QuantizedToZero = 1,
            Opposite = 2
        }
        private const string StableDoorwayId = "Doorways[5]/Door_SM_A[0]/DoorWayPoint[0]";
        private const string K1BasisId = "K1_UnitWhite_Full";
        // BC6H and BC7 both encode 4x4 blocks. A full block-width unowned-only gutter
        // prevents a chart-edge block from mixing valid direction/color with the neutral
        // atlas background. Owner texels are immutable, so this cannot overwrite a chart.
        private const int ChartGutterPixels = 4;
        // Destination mip0 is compressed only after Apply has generated the full linear
        // mip chain and an independent CPU 2x2 box check has verified every level. These
        // bounds still reject compression drift while leaving headroom for BC6H/BC7.
        private const float ColorMipCompressionMaxRelativeRmse = 0.08f;
        private const float ColorMipCompressionMaxAbsoluteError = 1f;
        private const double LowEnergyBaseColorMipMaxAbsoluteRmse = 0.001d;
        private const float LowEnergyBaseColorMipMaxAbsoluteError = 0.05f;
        // At 32x32 and below, BC6H blocks can report a large relative error for a
        // low-energy generated mip even when both its absolute RMS and worst texel
        // remain tightly bounded. This exception is deliberately unavailable to mip0
        // and to every level 64px or larger so production-surface parity stays strict.
        private const int CoarseBaseColorMipMaximumDimension = 32;
        private const double CoarseBaseColorMipMaxAbsoluteRmse = 0.025d;
        private const float CoarseBaseColorMipMaxAbsoluteError = 0.25f;
        private const float ResponseColorMipCompressionMaxRelativeRmse = 0.15f;
        private const float ResponseColorMipCompressionRelativeErrorFloor = 0.001f;
        private const float ResponseColorMipCompressionMaxAbsoluteRmse = 0.005f;
        private const float ResponseColorMipCompressionMaxAbsoluteError = 0.25f;
        private const float DirectionMipCompressionMaxAbsoluteRmse = 0.025f;
        private const float DirectionMipCompressionMaxAbsoluteError = 0.15f;
        private const float ResponseDirectionMipCompressionMaxAbsoluteError = 0.25f;
        // Apply's byte/half storage rounds the mathematically exact box average. These
        // limits allow only that representation error; parity is checked before compression.
        private const float GeneratedByteMipMaxAbsoluteError = 1.5f / 255f;
        private const float GeneratedHalfMipMinimumAbsoluteError = 0.0015f;
        private const float GeneratedHalfMipMaxRelativeError = 0.0015f;
        // The proof samples the same imported P0 source through the same UV2/ST
        // transform on two GPU paths.  This leaves room for half-float readback while
        // still rejecting a texel-scale Y flip or an incorrect source transform.
        private const float CanonicalOrientationProofMaxAbsoluteError = 0.02f;
        private const int AtlasCopyTrianglePass = 0;
        private const int AtlasOwnerMinPass = 1;
        private const int AtlasOwnerMaxPass = 2;
        private const int AtlasDilateColorPass = 3;
        private const int AtlasDilateOccupancyPass = 4;

        private static readonly int AtlasSourceTextureId = Shader.PropertyToID("_MainTex");
        private static readonly int AtlasDestinationStId = Shader.PropertyToID("_DestinationST");
        private static readonly int AtlasSourceStId = Shader.PropertyToID("_SourceST");
        private static readonly int AtlasOwnerId = Shader.PropertyToID("_OwnerId");
        private static readonly int AtlasOwnerMapId = Shader.PropertyToID("_OwnerMap");
        private static readonly int AtlasOccupancyId = Shader.PropertyToID("_Occupancy");
        private static readonly int AtlasUseConstantSourceUvId = Shader.PropertyToID("_UseConstantSourceUv");

        private enum LightmapUvMode
        {
            StoredUv2Triangles,
            ImplicitZero
        }

        private enum EndpointLayout
        {
            Power0,
            Power100
        }

        // Kept byte-for-byte equivalent in meaning for the legacy Generated payload.
        private static readonly RoomSpec[] LegacyRoomSpecs =
        {
            new RoomSpec(
                "StartRoom_R000",
                "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/StartRoom_R000.prefab",
                "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/BakedData/StartRoom_R000/P0/StartRoom_R000_BakeData.asset",
                "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/BakedData/StartRoom_R000/P100/StartRoom_R000_BakeData.asset",
                "Assets/Experiments/DungeonPortalTransportPoC/Generated/Bounce/StartRoom_R000/StartRoom_R000_ReceiverResponseCapture.asset",
                "Assets/Experiments/DungeonPortalTransportPoC/Generated/EndpointProfiles/StartRoom_R000_EndpointProfile.asset",
                77,
                76),
            new RoomSpec(
                "AdminstrativeSegregation_R000",
                "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/AdminstrativeSegregation_R000.prefab",
                "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/BakedData/AdminstrativeSegregation_R000/P0/AdminstrativeSegregation_R000_BakeData.asset",
                "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/BakedData/AdminstrativeSegregation_R000/P100/AdminstrativeSegregation_R000_BakeData.asset",
                "Assets/Experiments/DungeonPortalTransportPoC/Generated/Bounce/AdminstrativeSegregation_R000/AdminstrativeSegregation_R000_ReceiverResponseCapture.asset",
                "Assets/Experiments/DungeonPortalTransportPoC/Generated/EndpointProfiles/AdminstrativeSegregation_R000_EndpointProfile.asset",
                306,
                302)
        };

        // Endpoint-native V2 intentionally uses the exact canonical prefabs and the
        // standalone V2 P0/P100 BakeData embedded by the final V2 prefabs. The angle
        // capture is regenerated separately against these same prefab identities.
        private static readonly RoomSpec[] EndpointNativeRoomSpecs =
        {
            new RoomSpec(
                "StartRoom_R000",
                "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/Canonical/StartRoom_R000.prefab",
                "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/Canonical/BakedData/StartRoom_R000/P0/StartRoom_R000_BakeData.asset",
                "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/Canonical/BakedData/StartRoom_R000/P100/StartRoom_R000_BakeData.asset",
                "Assets/Experiments/DungeonPortalTransportPoC/Generated/Bounce/StartRoom_R000/StartRoom_R000_ReceiverResponseCapture.asset",
                "Assets/Experiments/DungeonPortalTransportPoC/Generated/EndpointProfiles/StartRoom_R000_EndpointProfile.asset",
                79,
                78),
            new RoomSpec(
                "AdminstrativeSegregation_R000",
                "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/Canonical/AdminstrativeSegregation_R000.prefab",
                "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/Canonical/BakedData/AdminstrativeSegregation_R000/P0/AdminstrativeSegregation_R000_BakeData.asset",
                "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/Canonical/BakedData/AdminstrativeSegregation_R000/P100/AdminstrativeSegregation_R000_BakeData.asset",
                "Assets/Experiments/DungeonPortalTransportPoC/Generated/Bounce/AdminstrativeSegregation_R000/AdminstrativeSegregation_R000_ReceiverResponseCapture.asset",
                "Assets/Experiments/DungeonPortalTransportPoC/Generated/EndpointProfiles/AdminstrativeSegregation_R000_EndpointProfile.asset",
                306,
                302)
        };

        [MenuItem("Tools/Dungeon/Lighting/Portal Baked Basis PoC/Build All (Staged)")]
        public static void BuildAllFromMenu()
        {
            string result = BuildAll();
            if (result.StartsWith("PASS", StringComparison.Ordinal))
                Debug.Log("[DungeonPortalBakedBasis] " + result);
            else
                Debug.LogError("[DungeonPortalBakedBasis] " + result);
        }

        /// <summary>Unity -executeMethod entry point.  It never starts/stops Play Mode.</summary>
        public static void BuildAllCli()
        {
            string result = BuildAll();
            if (!result.StartsWith("PASS", StringComparison.Ordinal))
                throw new InvalidOperationException(result);
        }

        /// <summary>
        /// Builds both room payloads into a fresh staging folder.  Existing canonical
        /// output is moved to a timestamped generated backup only after the staging
        /// payload has passed its own verifier; a failed promotion restores it.
        /// </summary>
        public static string BuildAll()
        {
            Material copyMaterial = null;
            PromotionTransaction promotion = null;
            string stagingRoot = string.Empty;
            try
            {
                ValidateEditorPreflight();
                EnsureFolder(Root);
                EnsureFolder(GeneratedRoot);
                AssertNoStaleStagingFolders();

                Shader copyShader = AssetDatabase.LoadAssetAtPath<Shader>(ChartCopyShaderPath);
                if (copyShader == null)
                    copyShader = Shader.Find(ChartCopyShaderName);
                if (copyShader == null || !copyShader.isSupported || copyShader.passCount < 5)
                    throw new InvalidOperationException(
                        "The editor-only UV2 triangle chart-copy shader is unavailable, unsupported, or has fewer than five required passes.");
                copyMaterial = new Material(copyShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "__DungeonPortalBakedBasisAtlasCopy__"
                };

                var rooms = new List<ResolvedRoom>(LegacyRoomSpecs.Length);
                for (int i = 0; i < LegacyRoomSpecs.Length; i++)
                    rooms.Add(ResolveRoom(LegacyRoomSpecs[i]));

                stagingRoot = CreateStagingFolder();
                var manifest = new DungeonPortalBakedBasisGeneratedManifest
                {
                    toolVersion = ToolVersion,
                    generatedUtcIso8601 = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    payloadRootName = "Generated",
                    chartCopyShaderPath = ChartCopyShaderPath,
                    chartCopyShaderDependencyHash = GetDependencyHash(ChartCopyShaderPath),
                    productionInputsReadOnly = true,
                    strictPersistedCaptureIntegrityClaimed = false,
                    k1SpatialApproximationCaveat =
                        "Endpoint cookies describe a nearly shared K=1 outgoing spatial shape, but the raw receiver " +
                        "injector was intentionally baked without a cookie. ResponseScale remains an oracle/visual " +
                        "calibration parameter; this manifest does not treat cookie similarity as a visual PASS.",
                    inputs = BuildInputFingerprints(rooms),
                    captureIntegrity = rooms.Select(BuildCaptureIntegrityRecord).ToArray()
                };

                manifest.sourceCookieComparison = CompareEndpointCookies(rooms[0], rooms[1]);

                var artifactFingerprints = new List<ArtifactFingerprint>();
                var roomRecords = new List<RoomRecord>(rooms.Count);
                for (int i = 0; i < rooms.Count; i++)
                {
                    roomRecords.Add(BuildRoomPayload(
                        rooms[i],
                        stagingRoot,
                        copyMaterial,
                        artifactFingerprints));
                }

                manifest.rooms = roomRecords.ToArray();
                manifest.artifacts = artifactFingerprints.ToArray();
                WriteManifest(stagingRoot, manifest);
                // Do not call AssetDatabase.SaveAssets here: it would persist unrelated
                // user-dirty assets/scenes. Each generated ScriptableObject is saved at
                // creation time below, and generated textures/manifest are synchronously
                // imported at their exact owned paths.
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                if (!TryVerifyPayload(stagingRoot, out string stagingFailure))
                    throw new InvalidOperationException("Staging payload verifier failed: " + stagingFailure);

                promotion = PromotionTransaction.Promote(stagingRoot);
                stagingRoot = string.Empty;
                if (!TryVerifyPayload(FinalPayloadFolder, out string finalFailure))
                {
                    promotion.RollBack();
                    throw new InvalidOperationException(
                        "Promoted payload verifier failed and the prior generated payload was restored: " + finalFailure);
                }

                promotion.Commit();
                return "PASS built and verified canonical P0/P100 + Full-Baseline receiver basis payload for " +
                       "StartRoom_R000 and AdminstrativeSegregation_R000. Existing generated output was preserved " +
                       "as a transaction backup when present; strict persisted-capture validation is intentionally " +
                       "not claimed.";
            }
            catch (Exception exception)
            {
                if (promotion != null)
                {
                    try
                    {
                        promotion.RollBack();
                    }
                    catch (Exception rollbackFailure)
                    {
                        return "FAIL baked-basis build failed: " + exception.Message +
                               " | generated-payload rollback also failed: " + rollbackFailure.Message;
                    }
                }

                return "FAIL baked-basis build failed: " + exception.Message +
                       (string.IsNullOrWhiteSpace(stagingRoot)
                           ? string.Empty
                           : " Staging was intentionally retained for inspection: '" + stagingRoot + "'.");
            }
            finally
            {
                if (copyMaterial != null)
                    UnityEngine.Object.DestroyImmediate(copyMaterial);
            }
        }

        [MenuItem("Tools/Dungeon/Lighting/Portal Baked Basis PoC/Build Endpoint-Native Responses (Isolated)")]
        public static void BuildEndpointNativeResponsesFromMenu()
        {
            string result = BuildEndpointNativeResponses();
            if (result.StartsWith("PASS", StringComparison.Ordinal))
                Debug.Log("[DungeonPortalBakedBasis] " + result);
            else
                Debug.LogError("[DungeonPortalBakedBasis] " + result);
        }

        /// <summary>
        /// Builds the response state pair twice: once in the exact production P0 atlas/ST
        /// layout and once in the exact production P100 atlas/ST layout. Original endpoint
        /// textures remain exact references. For base-power interpolation only, the opposite
        /// endpoint is triangle-repacked into each native layout, producing P100-in-P0 and
        /// P0-in-P100 transition pairs without changing renderer mappings while active.
        /// </summary>
        public static string BuildEndpointNativeResponses()
        {
            Material copyMaterial = null;
            PromotionTransaction promotion = null;
            string stagingRoot = string.Empty;
            try
            {
                ValidateEditorPreflight();
                EnsureFolder(Root);
                EnsureFolder(EndpointNativeGeneratedRoot);
                AssertNoStaleStagingFolders(EndpointNativeGeneratedRoot);

                Shader copyShader = AssetDatabase.LoadAssetAtPath<Shader>(ChartCopyShaderPath);
                if (copyShader == null)
                    copyShader = Shader.Find(ChartCopyShaderName);
                if (copyShader == null || !copyShader.isSupported || copyShader.passCount < 5)
                {
                    throw new InvalidOperationException(
                        "The editor-only UV2 triangle chart-copy shader is unavailable, unsupported, or has fewer than five required passes.");
                }
                copyMaterial = new Material(copyShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "__DungeonPortalBakedBasisEndpointNativeAtlasCopy__"
                };

                var rooms = new List<ResolvedRoom>(EndpointNativeRoomSpecs.Length);
                for (int i = 0; i < EndpointNativeRoomSpecs.Length; i++)
                    rooms.Add(ResolveEndpointNativeRoom(EndpointNativeRoomSpecs[i]));

                stagingRoot = CreateStagingFolder(EndpointNativeGeneratedRoot);
                var artifacts = new List<ArtifactFingerprint>();
                var roomRecords = new List<EndpointNativeRoomRecord>(rooms.Count);
                for (int i = 0; i < rooms.Count; i++)
                {
                    roomRecords.Add(BuildEndpointNativeRoomPayload(
                        rooms[i],
                        stagingRoot,
                        copyMaterial,
                        artifacts));
                }

                var manifest = new EndpointNativeManifest
                {
                    schema = EndpointNativeManifestSchema,
                    toolVersion = ToolVersion,
                    generatedUtcIso8601 = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    payloadRootName = EndpointNativePayloadRootName,
                    chartCopyShaderPath = ChartCopyShaderPath,
                    chartCopyShaderDependencyHash = GetDependencyHash(ChartCopyShaderPath),
                    productionInputsReadOnly = true,
                    strictPersistedCaptureIntegrityClaimed = false,
                    basePolicy = EndpointNativeManifestBasePolicy,
                    responseMipPolicy = EndpointNativeManifestResponseMipPolicy,
                    runtimeSchema = DungeonPortalBakedRoomBasisData.CurrentSchema,
                    receiverPosePolicy = EndpointNativeReceiverPosePolicy,
                    compactResponseEncoding = EndpointNativeCompactResponseEncoding,
                    runtimeConsumerReady = true,
                    inputs = BuildEndpointNativeInputFingerprints(rooms),
                    angleCaptureIntegrity = rooms.Select(BuildAngleCaptureIntegrityRecord).ToArray(),
                    artifacts = artifacts.ToArray(),
                    rooms = roomRecords.ToArray()
                };
                WriteEndpointNativeManifest(stagingRoot, manifest);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                if (!TryVerifyEndpointNativePayload(stagingRoot, true, out string stagingFailure))
                    throw new InvalidOperationException("Endpoint-native staging verifier failed: " + stagingFailure);

                promotion = PromotionTransaction.Promote(
                    stagingRoot,
                    EndpointNativeGeneratedRoot,
                    EndpointNativeManifestFileName);
                stagingRoot = string.Empty;
                if (!TryVerifyEndpointNativePayload(EndpointNativeGeneratedRoot, true, out string finalFailure))
                {
                    promotion.RollBack();
                    throw new InvalidOperationException(
                        "Endpoint-native promotion verifier failed and the prior isolated payload was restored: " +
                        finalFailure);
                }

                promotion.Commit();
                return "PASS built and verified isolated DPBB-2 endpoint-native P0/P100 room-basis payloads for " +
                       "StartRoom_R000 and AdminstrativeSegregation_R000 from the exact V2 canonical prefabs. " +
                       "Legacy Generated, legacy EndpointNativeGenerated, and all production " +
                       "assets were untouched; both P100-to-P0 and P0-to-P100 base transitions are " +
                       "captured-layout local; D025/D050/D075/D100 compact same-pose responses are " +
                       "runtimeConsumerReady=true; receiverPosePolicy=" +
                       EndpointNativeReceiverPosePolicy + ".";
            }
            catch (Exception exception)
            {
                if (promotion != null)
                {
                    try
                    {
                        promotion.RollBack();
                    }
                    catch (Exception rollbackFailure)
                    {
                        return "FAIL endpoint-native authoring failed: " + exception.Message +
                               " | isolated-payload rollback also failed: " + rollbackFailure.Message;
                    }
                }

                string retainedFailureRoot = stagingRoot;
                string archiveFailure = string.Empty;
                if (!string.IsNullOrWhiteSpace(stagingRoot))
                {
                    try
                    {
                        string failureReportPath = stagingRoot + "/EndpointNativeAuthoringFailure.txt";
                        File.WriteAllText(
                            AssetPathToPhysicalPath(failureReportPath),
                            exception.ToString(),
                            new UTF8Encoding(false));
                        AssetDatabase.ImportAsset(
                            failureReportPath,
                            ImportAssetOptions.ForceSynchronousImport);
                    }
                    catch (Exception reportException)
                    {
                        archiveFailure = " Failed-report persistence also failed: " +
                                         reportException.Message;
                    }
                    try
                    {
                        retainedFailureRoot = ArchiveEndpointNativeFailedStaging(stagingRoot);
                    }
                    catch (Exception archiveException)
                    {
                        archiveFailure += " Failed-staging archive also failed: " + archiveException.Message;
                    }
                }
                return "FAIL endpoint-native authoring failed: " + exception.Message +
                       (string.IsNullOrWhiteSpace(retainedFailureRoot)
                           ? string.Empty
                           : " Failed staging was retained for inspection: '" + retainedFailureRoot + "'.") +
                       archiveFailure;
            }
            finally
            {
                if (copyMaterial != null)
                    UnityEngine.Object.DestroyImmediate(copyMaterial);
            }
        }

        public static bool TryVerifyEndpointNativeGeneratedPayload(out string failure)
        {
            return TryVerifyEndpointNativePayload(EndpointNativeGeneratedRoot, true, out failure);
        }

        public static string DiagnoseStartP100OppositeBaseTransition()
        {
            return DiagnoseOppositeBaseTransition(
                0,
                EndpointLayout.Power100,
                "__Diagnostic_StartP100OppositeBaseTransition",
                "Start/P100");
        }

        public static string DiagnoseAdminP0OppositeBaseTransition()
        {
            return DiagnoseOppositeBaseTransition(
                1,
                EndpointLayout.Power0,
                "__Diagnostic_AdminP0OppositeBaseTransition",
                "Admin/P0");
        }

        public static string DiagnoseAdminP100OppositeBaseTransition()
        {
            return DiagnoseOppositeBaseTransition(
                1,
                EndpointLayout.Power100,
                "__Diagnostic_AdminP100OppositeBaseTransition",
                "Admin/P100");
        }

        private static string DiagnoseOppositeBaseTransition(
            int roomIndex,
            EndpointLayout destinationLayout,
            string diagnosticFolderName,
            string diagnosticLabel)
        {
            string diagnosticRoot = EndpointNativeGeneratedRoot + "/" + diagnosticFolderName;
            Material copyMaterial = null;
            try
            {
                ValidateEditorPreflight();
                EnsureFolder(Root);
                EnsureFolder(EndpointNativeGeneratedRoot);
                if (AssetDatabase.IsValidFolder(diagnosticRoot) ||
                    Directory.Exists(AssetPathToPhysicalPath(diagnosticRoot)))
                {
                    throw new InvalidOperationException(
                        diagnosticLabel + " transition diagnostic target already exists: '" +
                        diagnosticRoot + "'.");
                }
                EnsureFolder(diagnosticRoot);
                Shader copyShader = AssetDatabase.LoadAssetAtPath<Shader>(ChartCopyShaderPath) ??
                                    Shader.Find(ChartCopyShaderName);
                if (copyShader == null || !copyShader.isSupported || copyShader.passCount < 5)
                    throw new InvalidOperationException("The chart-copy shader is unavailable for the diagnostic.");
                copyMaterial = new Material(copyShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "__DPBB_TransitionDiagnostic__"
                };
                ResolvedRoom room = ResolveEndpointNativeRoom(EndpointNativeRoomSpecs[roomIndex]);
                BucketWork[] buckets = BuildBucketWork(room);
                List<ChartCopyOperation> oppositeBaseCopies = destinationLayout == EndpointLayout.Power100
                    ? BuildP0CopyOperations(room, buckets)
                    : BuildP100CopyOperations(room, buckets);
                string endpointId = destinationLayout == EndpointLayout.Power100 ? "P100" : "P0";
                string oppositeEndpointId = destinationLayout == EndpointLayout.Power100 ? "P0" : "P100";
                var artifacts = new List<ArtifactFingerprint>();
                WriteRepackedAtlasSet(
                    diagnosticRoot + "/" + room.Spec.RoomId + "/Endpoints/" + endpointId +
                    "/BaseTransition/OppositeEndpoint",
                    oppositeEndpointId + "In" + endpointId + "LayoutDiagnostic",
                    buckets,
                    oppositeBaseCopies,
                    copyMaterial,
                    diagnosticRoot,
                    artifacts,
                    true,
                    destinationLayout,
                    true);
                return "PASS " + diagnosticLabel + " opposite-base transition diagnostic artifacts=" +
                       artifacts.Count + ".";
            }
            catch (Exception exception)
            {
                try
                {
                    if (AssetDatabase.IsValidFolder(diagnosticRoot))
                    {
                        string reportPath = diagnosticRoot + "/DiagnosticFailure.txt";
                        File.WriteAllText(
                            AssetPathToPhysicalPath(reportPath),
                            exception.ToString(),
                            new UTF8Encoding(false));
                        AssetDatabase.ImportAsset(reportPath, ImportAssetOptions.ForceSynchronousImport);
                    }
                }
                catch
                {
                    // The returned diagnostic remains authoritative even if persistence fails.
                }
                return "FAIL " + diagnosticLabel + " opposite-base transition diagnostic: " +
                       exception.Message;
            }
            finally
            {
                if (copyMaterial != null)
                    UnityEngine.Object.DestroyImmediate(copyMaterial);
            }
        }

        public static void VerifyEndpointNativeGeneratedPayloadCli()
        {
            if (!TryVerifyEndpointNativeGeneratedPayload(out string failure))
                throw new InvalidOperationException(failure);
            Debug.Log("[DungeonPortalBakedBasis] PASS endpoint-native DPBB-2 generated payload verifier.");
        }

        [MenuItem("Tools/Dungeon/Lighting/Portal Baked Basis PoC/Publish Endpoint-Native DPBB-2 Basis (No Re-Bake)")]
        public static void PublishEndpointNativeBasisAssetsFromMenu()
        {
            string result = PublishEndpointNativeBasisAssets();
            if (result.StartsWith("PASS", StringComparison.Ordinal))
                Debug.Log("[DungeonPortalBakedBasis] " + result);
            else
                Debug.LogError("[DungeonPortalBakedBasis] " + result);
        }

        public static void PublishEndpointNativeBasisAssetsCli()
        {
            string result = PublishEndpointNativeBasisAssets();
            if (!result.StartsWith("PASS", StringComparison.Ordinal))
                throw new InvalidOperationException(result);
            Debug.Log("[DungeonPortalBakedBasis] " + result);
        }

        /// <summary>
        /// Transactionally adds the two small DPBB-2 room-basis assets to an already
        /// verified endpoint-native response payload. No response texture is regenerated.
        /// </summary>
        public static string PublishEndpointNativeBasisAssets()
        {
            EndpointNativeBasisPublicationTransaction publication = null;
            string stagingRoot = string.Empty;
            try
            {
                ValidateEditorPreflight();
                EnsureFolder(Root);
                EnsureFolder(EndpointNativeGeneratedRoot);
                AssertNoStaleStagingFolders(EndpointNativeGeneratedRoot);

                EndpointNativeManifest manifest = LoadEndpointNativeManifest(
                    EndpointNativeGeneratedRoot);
                if (manifest.runtimeConsumerReady)
                {
                    if (!TryVerifyEndpointNativePayload(
                            EndpointNativeGeneratedRoot,
                            true,
                            out string existingReadyFailure))
                    {
                        throw new InvalidOperationException(
                            "Existing ready endpoint-native payload failed verification: " +
                            existingReadyFailure);
                    }
                    return "PASS endpoint-native DPBB-2 room-basis payload was already published and verified; " +
                           "no assets changed.";
                }

                if (!TryVerifyEndpointNativePayload(
                        EndpointNativeGeneratedRoot,
                        false,
                        out string sourceFailure))
                {
                    throw new InvalidOperationException(
                        "The pre-publication endpoint-native response payload is not valid: " + sourceFailure);
                }

                var rooms = new Dictionary<string, ResolvedRoom>(StringComparer.Ordinal);
                for (int i = 0; i < EndpointNativeRoomSpecs.Length; i++)
                {
                    ResolvedRoom resolved = ResolveEndpointNativeRoom(EndpointNativeRoomSpecs[i]);
                    rooms.Add(resolved.Spec.RoomId, resolved);
                }

                stagingRoot = CreateStagingFolder(EndpointNativeGeneratedRoot);
                var basisArtifacts = new List<ArtifactFingerprint>(EndpointNativeRoomSpecs.Length);
                EndpointNativeRoomRecord[] roomRecords = manifest.rooms ??
                    Array.Empty<EndpointNativeRoomRecord>();
                if (roomRecords.Length != EndpointNativeRoomSpecs.Length)
                    throw new InvalidOperationException("Endpoint-native publication requires exactly two room records.");

                for (int i = 0; i < roomRecords.Length; i++)
                {
                    EndpointNativeRoomRecord record = roomRecords[i];
                    if (record == null || !rooms.TryGetValue(record.roomId, out ResolvedRoom room))
                        throw new InvalidOperationException("Endpoint-native publication found an unexpected room record.");
                    string stagedRoomFolder = stagingRoot + "/" + record.roomId;
                    EnsureFolder(stagedRoomFolder);
                    string stagedBasisPath = stagedRoomFolder + "/" + record.roomId + "_RoomBasis.asset";
                    CreateEndpointNativeBasisAsset(
                        room,
                        record,
                        EndpointNativeGeneratedRoot,
                        stagedBasisPath,
                        stagingRoot,
                        basisArtifacts);
                    record.basisAssetRelativePath =
                        record.roomId + "/" + record.roomId + "_RoomBasis.asset";
                    record.basisDependencyHash = GetDependencyHash(stagedBasisPath);
                }

                ArtifactFingerprint[] priorArtifacts = manifest.artifacts ??
                    Array.Empty<ArtifactFingerprint>();
                VerifyEndpointNativeDeltaArtifactContract(manifest, priorArtifacts);
                manifest.toolVersion = ToolVersion;
                manifest.publishedUtcIso8601 = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                manifest.runtimeSchema = DungeonPortalBakedRoomBasisData.CurrentSchema;
                manifest.receiverPosePolicy = EndpointNativeReceiverPosePolicy;
                manifest.runtimeConsumerReady = true;
                manifest.artifacts = priorArtifacts.Concat(basisArtifacts).ToArray();

                WriteEndpointNativeManifest(stagingRoot, manifest);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                VerifyEndpointNativePublicationCandidate(stagingRoot, manifest);

                publication = EndpointNativeBasisPublicationTransaction.Promote(
                    stagingRoot,
                    EndpointNativeManifestFileName);
                stagingRoot = string.Empty;
                if (!TryVerifyEndpointNativePayload(
                        EndpointNativeGeneratedRoot,
                        true,
                        out string finalFailure))
                {
                    publication.RollBack();
                    throw new InvalidOperationException(
                        "Endpoint-native DPBB-2 publication verifier failed and the prior manifest was restored: " +
                        finalFailure);
                }

                publication.Commit();
                return "PASS transactionally published and verified DPBB-2 room-basis assets without rebaking " +
                       "the dynamically declared endpoint-native compact responses and dual-layout base textures; legacy Generated and " +
                       "production assets were untouched; receiverPosePolicy=" +
                       EndpointNativeReceiverPosePolicy + ".";
            }
            catch (Exception exception)
            {
                if (publication != null)
                {
                    try
                    {
                        publication.RollBack();
                    }
                    catch (Exception rollbackFailure)
                    {
                        return "FAIL endpoint-native DPBB-2 publication failed: " + exception.Message +
                               " | publication rollback also failed: " + rollbackFailure.Message;
                    }
                }

                string retainedFailureRoot = stagingRoot;
                string archiveFailure = string.Empty;
                if (!string.IsNullOrWhiteSpace(stagingRoot))
                {
                    try
                    {
                        retainedFailureRoot = ArchiveEndpointNativeFailedStaging(stagingRoot);
                    }
                    catch (Exception archiveException)
                    {
                        archiveFailure = " Failed-staging archive also failed: " + archiveException.Message;
                    }
                }
                return "FAIL endpoint-native DPBB-2 publication failed: " + exception.Message +
                       (string.IsNullOrWhiteSpace(retainedFailureRoot)
                           ? string.Empty
                           : " Failed staging was retained for inspection: '" + retainedFailureRoot + "'.") +
                       archiveFailure;
            }
        }

        [MenuItem("Tools/Dungeon/Lighting/Portal Baked Basis PoC/Verify Generated Payload")]
        public static void VerifyGeneratedPayloadFromMenu()
        {
            if (TryVerifyGeneratedPayload(out string failure))
                Debug.Log("[DungeonPortalBakedBasis] PASS generated payload verifier.");
            else
                Debug.LogError("[DungeonPortalBakedBasis] FAIL generated payload verifier: " + failure);
        }

        public static void VerifyGeneratedPayloadCli()
        {
            if (!TryVerifyGeneratedPayload(out string failure))
                throw new InvalidOperationException(failure);
        }

        public static bool TryVerifyGeneratedPayload(out string failure)
        {
            return TryVerifyPayload(FinalPayloadFolder, out failure);
        }

        private static void ValidateEditorPreflight()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "The baked-basis authoring tool is edit-mode only and will not change the user's Play Mode state.");
            }

            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("Unity is compiling; wait for a clean compile before authoring.");

            if (Lightmapping.isRunning)
                throw new InvalidOperationException("A lightmap bake is running; this tool will not interfere with it.");
        }

        private static ResolvedRoom ResolveRoom(RoomSpec spec)
        {
            DungeonTileBakeData p0 = LoadRequired<DungeonTileBakeData>(spec.P0BakeDataPath);
            DungeonTileBakeData p100 = LoadRequired<DungeonTileBakeData>(spec.P100BakeDataPath);
            DungeonPortalReceiverResponseCapture capture =
                LoadRequired<DungeonPortalReceiverResponseCapture>(spec.ReceiverCapturePath);
            DungeonPortalEndpointProfile endpoint =
                LoadRequired<DungeonPortalEndpointProfile>(spec.EndpointProfilePath);
            GameObject prefab = LoadRequired<GameObject>(spec.ProductionPrefabPath);

            AssertCleanInput(spec.ProductionPrefabPath, prefab);
            AssertCleanInput(spec.P0BakeDataPath, p0);
            AssertCleanInput(spec.P100BakeDataPath, p100);
            AssertCleanInput(spec.ReceiverCapturePath, capture);
            AssertCleanInput(spec.EndpointProfilePath, endpoint);

            if (!string.Equals(capture.ReceiverRoomId, spec.RoomId, StringComparison.Ordinal) ||
                !string.Equals(capture.StableDoorwayId, StableDoorwayId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Receiver capture room/doorway id does not match the canonical contract for '" + spec.RoomId + "'.");
            }
            if (!capture.TryValidate(out string captureError))
                throw new InvalidOperationException("Receiver capture structural validation failed for '" + spec.RoomId + "': " + captureError);
            if (!endpoint.TryValidate(out string endpointError))
                throw new InvalidOperationException("Endpoint profile validation failed for '" + spec.RoomId + "': " + endpointError);
            if (!string.Equals(endpoint.RoomId, spec.RoomId, StringComparison.Ordinal) ||
                !string.Equals(endpoint.DoorwayId, StableDoorwayId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Endpoint profile room/doorway id does not match the canonical contract for '" + spec.RoomId + "'.");
            }

            ValidateProductionBakes(spec, p0, p100);
            DungeonPortalReceiverResponseCapture.CaptureState baseline = capture.Baseline;
            DungeonPortalReceiverResponseCapture.CaptureState full = capture.Full;
            if (!DungeonPortalReceiverResponseCapture.TryValidateCompatibleStateLayouts(
                    baseline,
                    full,
                    out string layoutFailure))
            {
                throw new InvalidOperationException(
                    "Receiver Baseline/Full layout differs for '" + spec.RoomId + "': " + layoutFailure);
            }
            if (!string.Equals(baseline.stateName, DungeonPortalReceiverResponseCapture.BaselineStateName, StringComparison.Ordinal) ||
                !string.Equals(full.stateName, DungeonPortalReceiverResponseCapture.FullStateName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Receiver capture exposes non-canonical Baseline/Full state names.");
            }
            if (!baseline.captured || !full.captured)
                throw new InvalidOperationException("Receiver Baseline/Full state is not marked captured for '" + spec.RoomId + "'.");

            CanonicalRendererWork[] canonical = BuildCanonicalRendererWork(spec, prefab, p0, p100, baseline, full);
            int mapped = canonical.Count(item => item.BaselineCapture.HasValue);
            if (mapped != spec.ExpectedCaptureRendererCount ||
                canonical.Length != spec.ExpectedProductionRendererCount ||
                canonical.Length - mapped != spec.ExpectedProductionRendererCount - spec.ExpectedCaptureRendererCount)
            {
                throw new InvalidOperationException(
                    "Receiver-to-production mapping count drifted for '" + spec.RoomId + "'. production=" +
                    canonical.Length + " capture=" + mapped + ".");
            }

            return new ResolvedRoom(spec, prefab, p0, p100, capture, endpoint, baseline, full, canonical);
        }

        /// <summary>
        /// Resolves the endpoint-native authoring inputs without treating the legacy D100
        /// receiver capture as a response source.  The angle capture is the sole response
        /// evidence for DPBB-2/v2, and every captured renderer is rebound through its
        /// sibling-indexed relativePath plus componentOrdinal identity.
        /// </summary>
        private static ResolvedRoom ResolveEndpointNativeRoom(RoomSpec spec)
        {
            DungeonTileBakeData p0 = LoadRequired<DungeonTileBakeData>(spec.P0BakeDataPath);
            DungeonTileBakeData p100 = LoadRequired<DungeonTileBakeData>(spec.P100BakeDataPath);
            DungeonPortalEndpointProfile endpoint =
                LoadRequired<DungeonPortalEndpointProfile>(spec.EndpointProfilePath);
            GameObject prefab = LoadRequired<GameObject>(spec.ProductionPrefabPath);
            string angleCapturePath = GetAngleCaptureAssetPath(spec.RoomId);
            DungeonPortalBakedBasisDoorAngleCapture angleCapture =
                LoadRequired<DungeonPortalBakedBasisDoorAngleCapture>(angleCapturePath);

            AssertCleanInput(spec.ProductionPrefabPath, prefab);
            AssertCleanInput(spec.P0BakeDataPath, p0);
            AssertCleanInput(spec.P100BakeDataPath, p100);
            AssertCleanInput(spec.EndpointProfilePath, endpoint);
            AssertCleanInput(angleCapturePath, angleCapture);
            ValidateProductionBakes(spec, p0, p100);
            if (!endpoint.TryValidate(out string endpointFailure) ||
                !string.Equals(endpoint.RoomId, spec.RoomId, StringComparison.Ordinal) ||
                !string.Equals(endpoint.DoorwayId, StableDoorwayId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Endpoint profile validation/identity failed for endpoint-native room '" +
                    spec.RoomId + "': " + endpointFailure);
            }
            if (!angleCapture.TryValidate(out string angleFailure) ||
                !string.Equals(angleCapture.ReceiverRoomId, spec.RoomId, StringComparison.Ordinal) ||
                !string.Equals(angleCapture.StableDoorwayId, StableDoorwayId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Door-angle capture validation/identity failed for endpoint-native room '" +
                    spec.RoomId + "': " + angleFailure);
            }
            ValidateEndpointNativeAngleCaptureProvenance(spec, angleCapture);

            CanonicalRendererWork[] canonical = BuildCanonicalRendererWork(
                spec,
                prefab,
                p0,
                p100,
                default,
                default,
                false);
            if (canonical.Length != spec.ExpectedProductionRendererCount)
            {
                throw new InvalidOperationException(
                    "Endpoint-native production renderer count drifted for '" + spec.RoomId + "'.");
            }

            var room = new ResolvedRoom(
                spec,
                prefab,
                p0,
                p100,
                null,
                endpoint,
                default,
                default,
                canonical,
                angleCapture,
                angleCapturePath);
            ValidateAngleCaptureBindings(room);
            return room;
        }

        private static void ValidateEndpointNativeAngleCaptureProvenance(
            RoomSpec spec,
            DungeonPortalBakedBasisDoorAngleCapture angleCapture)
        {
            DungeonPortalBakedBasisDoorAngleCapture.CaptureProvenance provenance =
                angleCapture.Provenance;
            if (!string.Equals(
                    provenance.toolVersion,
                    EndpointNativeAngleCaptureToolVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Endpoint-native V2 requires a V2-canonical angle capture written by '" +
                    EndpointNativeAngleCaptureToolVersion + "' for '" + spec.RoomId + "'.");
            }

            DungeonPortalReceiverResponseCapture.AssetFingerprint[] inputs =
                provenance.productionInputs ??
                Array.Empty<DungeonPortalReceiverResponseCapture.AssetFingerprint>();
            int matchingPrefabInputs = 0;
            for (int i = 0; i < inputs.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.AssetFingerprint input = inputs[i];
                if (!string.Equals(input.assetPath, spec.ProductionPrefabPath, StringComparison.Ordinal))
                    continue;
                matchingPrefabInputs++;
                if (input.wasDirtyBeforeCapture || input.wasDirtyAfterCapture ||
                    !string.Equals(
                        input.dependencyHash,
                        GetDependencyHash(spec.ProductionPrefabPath),
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Endpoint-native V2 angle capture has a dirty or stale canonical prefab fingerprint for '" +
                        spec.RoomId + "'.");
                }
            }
            if (matchingPrefabInputs != 1)
            {
                throw new InvalidOperationException(
                    "Endpoint-native V2 angle capture does not name exactly one matching V2 canonical prefab for '" +
                    spec.RoomId + "'.");
            }
        }

        private static string GetAngleCaptureAssetPath(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId))
                throw new ArgumentException("Room id is required for the angle-capture path.", nameof(roomId));
            return DungeonPortalBakedBasisDoorAngleCapture.EndpointNativeV2OwnedAssetRoot + "/" + roomId + "/" +
                   roomId + "_DoorAngleCapture.asset";
        }

        private static void ValidateProductionBakes(RoomSpec spec, DungeonTileBakeData p0, DungeonTileBakeData p100)
        {
            if (p0 == null || p100 == null ||
                p0.lightmapsMode != LightmapsMode.CombinedDirectional ||
                p100.lightmapsMode != LightmapsMode.CombinedDirectional)
            {
                throw new InvalidOperationException(
                    "Both production bake assets must use CombinedDirectional lightmaps for '" + spec.RoomId + "'.");
            }

            ValidateBakeTextureArrays(spec.RoomId, "P0", p0);
            ValidateBakeTextureArrays(spec.RoomId, "P100", p100);
            DungeonTileBakeData.RendererBakeEntry[] p0Entries = p0.rendererEntries ?? Array.Empty<DungeonTileBakeData.RendererBakeEntry>();
            DungeonTileBakeData.RendererBakeEntry[] p100Entries = p100.rendererEntries ?? Array.Empty<DungeonTileBakeData.RendererBakeEntry>();
            if (p0Entries.Length != spec.ExpectedProductionRendererCount ||
                p100Entries.Length != spec.ExpectedProductionRendererCount)
            {
                throw new InvalidOperationException(
                    "Production renderer-entry count drifted for '" + spec.RoomId + "'. expected=" +
                    spec.ExpectedProductionRendererCount + " p0=" + p0Entries.Length + " p100=" + p100Entries.Length + ".");
            }
        }

        private static void ValidateBakeTextureArrays(string roomId, string powerName, DungeonTileBakeData bake)
        {
            Texture2D[] colors = bake.lightmapColors ?? Array.Empty<Texture2D>();
            Texture2D[] directions = bake.lightmapDirections ?? Array.Empty<Texture2D>();
            if (colors.Length == 0 || colors.Length != directions.Length)
            {
                throw new InvalidOperationException(
                    roomId + " " + powerName + " has incomplete CombinedDirectional color/direction arrays.");
            }
            for (int i = 0; i < colors.Length; i++)
            {
                ValidateSampleTexture(colors[i], roomId + " " + powerName + " color[" + i + "]", false);
                ValidateSampleTexture(directions[i], roomId + " " + powerName + " direction[" + i + "]", true);
                if (colors[i].width != directions[i].width || colors[i].height != directions[i].height ||
                    colors[i].mipmapCount != directions[i].mipmapCount || colors[i].mipmapCount <= 1)
                {
                    throw new InvalidOperationException(
                        roomId + " " + powerName +
                        " color/direction dimensions or full mip-chain count differs at atlas " + i + ".");
                }
            }
        }

        private static void ValidateSampleTexture(Texture2D texture, string label, bool requiresAlpha)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0 ||
                GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat) ||
                !SystemInfo.SupportsTextureFormat(texture.format) ||
                (texture.format != TextureFormat.RGBAHalf && texture.format != TextureFormat.RGBA32 &&
                 texture.format != TextureFormat.BC6H && texture.format != TextureFormat.BC7) ||
                (requiresAlpha && texture.format == TextureFormat.BC6H))
            {
                throw new InvalidOperationException("Unsupported/non-linear source lightmap texture: " + label + ".");
            }
        }

        private static T LoadRequired<T>(string assetPath) where T : UnityEngine.Object
        {
            T result = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (result == null)
                throw new InvalidOperationException("Required input asset is missing: '" + assetPath + "'.");
            return result;
        }

        private static void AssertCleanInput(string assetPath, UnityEngine.Object asset)
        {
            if (asset == null || EditorUtility.IsDirty(asset))
                throw new InvalidOperationException("Required input asset is missing or dirty: '" + assetPath + "'.");
        }

        private static CanonicalRendererWork[] BuildCanonicalRendererWork(
            RoomSpec spec,
            GameObject prefab,
            DungeonTileBakeData p0,
            DungeonTileBakeData p100,
            DungeonPortalReceiverResponseCapture.CaptureState baseline,
            DungeonPortalReceiverResponseCapture.CaptureState full,
            bool bindLegacyCapture = true)
        {
            DungeonTileBakeData.RendererBakeEntry[] p0Entries = p0.rendererEntries ??
                Array.Empty<DungeonTileBakeData.RendererBakeEntry>();
            DungeonTileBakeData.RendererBakeEntry[] p100Entries = p100.rendererEntries ??
                Array.Empty<DungeonTileBakeData.RendererBakeEntry>();
            var p100ByPath = GroupProductionEntries(p100Entries, spec.RoomId + " P100");
            var occurrenceByPath = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = new CanonicalRendererWork[p0Entries.Length];
            var canonicalByPath = new Dictionary<string, List<CanonicalRendererWork>>(StringComparer.Ordinal);

            for (int i = 0; i < p0Entries.Length; i++)
            {
                DungeonTileBakeData.RendererBakeEntry p0Entry = p0Entries[i];
                ValidateProductionRendererEntry(spec.RoomId + " P0", p0Entry, p0.lightmapColors.Length);
                if (!p100ByPath.TryGetValue(p0Entry.relativePath, out List<DungeonTileBakeData.RendererBakeEntry> p100Bucket))
                {
                    throw new InvalidOperationException(
                        "P100 has no renderer bucket matching P0 path '" + p0Entry.relativePath +
                        "' for '" + spec.RoomId + "'.");
                }
                occurrenceByPath.TryGetValue(p0Entry.relativePath, out int occurrence);
                occurrenceByPath[p0Entry.relativePath] = occurrence + 1;
                if (occurrence >= p100Bucket.Count)
                {
                    throw new InvalidOperationException(
                        "P100 renderer bucket is shorter than P0 at '" + p0Entry.relativePath +
                        "#" + occurrence + "' for '" + spec.RoomId + "'.");
                }

                DungeonTileBakeData.RendererBakeEntry p100Entry = p100Bucket[occurrence];
                ValidateProductionRendererEntry(spec.RoomId + " P100", p100Entry, p100.lightmapColors.Length);
                var item = new CanonicalRendererWork
                {
                    CanonicalKey = p0Entry.relativePath + "#" + occurrence.ToString(CultureInfo.InvariantCulture),
                    ProductionRelativePath = p0Entry.relativePath,
                    Occurrence = occurrence,
                    P0 = p0Entry,
                    P100 = p100Entry
                };
                result[i] = item;
                if (!canonicalByPath.TryGetValue(item.ProductionRelativePath, out List<CanonicalRendererWork> bucket))
                {
                    bucket = new List<CanonicalRendererWork>();
                    canonicalByPath.Add(item.ProductionRelativePath, bucket);
                }
                bucket.Add(item);
            }

            foreach (KeyValuePair<string, List<DungeonTileBakeData.RendererBakeEntry>> pair in p100ByPath)
            {
                if (!canonicalByPath.TryGetValue(pair.Key, out List<CanonicalRendererWork> p0Bucket) ||
                    p0Bucket.Count != pair.Value.Count)
                {
                    throw new InvalidOperationException(
                        "P0/P100 renderer-bucket cardinality differs at '" + pair.Key + "' for '" + spec.RoomId + "'.");
                }
            }

            Dictionary<string, List<ProductionRendererIdentity>> productionIdentities =
                LoadProductionRendererIdentities(prefab, canonicalByPath.Keys);
            foreach (KeyValuePair<string, List<CanonicalRendererWork>> pair in canonicalByPath)
            {
                productionIdentities.TryGetValue(pair.Key, out List<ProductionRendererIdentity> identities);
                BindProductionRendererBucketInRuntimeTraversalOrder(
                    spec.RoomId,
                    pair.Key,
                    pair.Value,
                    identities);
            }

            if (bindLegacyCapture)
            {
                Dictionary<string, List<CapturePair>> captureByPath =
                    BuildCapturePairs(baseline, full, spec.RoomId);
                int mappedCount = 0;
                foreach (KeyValuePair<string, List<CapturePair>> pair in captureByPath)
                {
                    if (!canonicalByPath.TryGetValue(pair.Key, out List<CanonicalRendererWork> productionBucket))
                    {
                        throw new InvalidOperationException(
                            "Receiver capture contains a path not found in the production P0 bake: '" + pair.Key +
                            "' for '" + spec.RoomId + "'.");
                    }
                    mappedCount += BindCaptureBucketByExactProductionIdentity(
                        spec.RoomId,
                        pair.Key,
                        productionBucket,
                        pair.Value);
                }

                if (mappedCount !=
                    (baseline.renderers ?? Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>()).Length)
                {
                    throw new InvalidOperationException(
                        "Receiver capture mapping did not consume every captured renderer for '" + spec.RoomId + "'.");
                }
            }

            return result;
        }

        private static void BindProductionRendererBucketInRuntimeTraversalOrder(
            string roomId,
            string relativePath,
            IReadOnlyList<CanonicalRendererWork> workBucket,
            IReadOnlyList<ProductionRendererIdentity> identitiesInRuntimeTraversalOrder)
        {
            int expectedCount = workBucket != null ? workBucket.Count : 0;
            int actualCount = identitiesInRuntimeTraversalOrder != null
                ? identitiesInRuntimeTraversalOrder.Count
                : 0;
            if (identitiesInRuntimeTraversalOrder == null || actualCount != expectedCount)
            {
                throw new InvalidOperationException(
                    "Production prefab hierarchy does not match P0 renderer bucket '" + relativePath +
                    "' for '" + roomId + "'. expected=" + expectedCount + " actual=" + actualCount + ".");
            }

            // LoadProductionRendererIdentities preserves GetComponentsInChildren<Renderer>(true)
            // traversal. The production BakeData writer and runtime switcher both assign duplicate
            // relative paths by occurrence in that exact traversal. Sorting stable paths here would
            // silently pair a renderer with another occurrence's P0/P100 charts.
            for (int i = 0; i < expectedCount; i++)
            {
                CanonicalRendererWork work = workBucket[i];
                ProductionRendererIdentity identity = identitiesInRuntimeTraversalOrder[i];
                work.ProductionStablePath = identity.StablePath;
                work.ProductionComponentOrdinal = identity.ComponentOrdinal;
                work.ProductionMesh = identity.Mesh;
                work.ProductionMeshAssetGuid = identity.MeshAssetGuid;
                work.ProductionMeshLocalId = identity.MeshLocalId;
                work.ProductionMeshUv2Hash = identity.MeshUv2Hash;
                work.LightmapUvMode = identity.LightmapUvMode;
            }
        }

        internal readonly struct RendererStableIdentity : IEquatable<RendererStableIdentity>
        {
            public readonly string StablePath;
            public readonly int ComponentOrdinal;

            public RendererStableIdentity(string stablePath, int componentOrdinal)
            {
                StablePath = stablePath;
                ComponentOrdinal = componentOrdinal;
            }

            public bool Equals(RendererStableIdentity other)
            {
                return ComponentOrdinal == other.ComponentOrdinal &&
                       string.Equals(StablePath, other.StablePath, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is RendererStableIdentity other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((StablePath != null ? StringComparer.Ordinal.GetHashCode(StablePath) : 0) * 397) ^
                           ComponentOrdinal;
                }
            }

            public override string ToString()
            {
                return (StablePath ?? "<null>") + "@component" +
                       ComponentOrdinal.ToString(CultureInfo.InvariantCulture);
            }
        }

        internal static int[] MatchCaptureIdentitiesToProductionOccurrences(
            string label,
            IReadOnlyList<RendererStableIdentity> productionInOccurrenceOrder,
            IReadOnlyList<RendererStableIdentity> capturesInAnyOrder)
        {
            if (productionInOccurrenceOrder == null)
                throw new ArgumentNullException(nameof(productionInOccurrenceOrder));
            if (capturesInAnyOrder == null)
                throw new ArgumentNullException(nameof(capturesInAnyOrder));
            if (capturesInAnyOrder.Count > productionInOccurrenceOrder.Count)
            {
                throw new InvalidOperationException(
                    label + " has more capture identities than production occurrences.");
            }

            var productionOccurrenceByIdentity =
                new Dictionary<RendererStableIdentity, int>(productionInOccurrenceOrder.Count);
            for (int occurrence = 0; occurrence < productionInOccurrenceOrder.Count; occurrence++)
            {
                RendererStableIdentity identity = productionInOccurrenceOrder[occurrence];
                if (string.IsNullOrWhiteSpace(identity.StablePath) || identity.ComponentOrdinal < 0 ||
                    !productionOccurrenceByIdentity.TryAdd(identity, occurrence))
                {
                    throw new InvalidOperationException(
                        label + " has an incomplete or duplicate production identity '" + identity + "'.");
                }
            }

            var seenCaptures = new HashSet<RendererStableIdentity>();
            var result = new int[capturesInAnyOrder.Count];
            for (int captureIndex = 0; captureIndex < capturesInAnyOrder.Count; captureIndex++)
            {
                RendererStableIdentity identity = capturesInAnyOrder[captureIndex];
                if (string.IsNullOrWhiteSpace(identity.StablePath) || identity.ComponentOrdinal < 0 ||
                    !seenCaptures.Add(identity))
                {
                    throw new InvalidOperationException(
                        label + " has an incomplete or duplicate capture identity '" + identity + "'.");
                }
                if (!productionOccurrenceByIdentity.TryGetValue(identity, out int occurrence))
                {
                    throw new InvalidOperationException(
                        label + " capture identity '" + identity + "' has no exact production occurrence.");
                }
                result[captureIndex] = occurrence;
            }
            return result;
        }

        private static int BindCaptureBucketByExactProductionIdentity(
            string roomId,
            string relativePath,
            IReadOnlyList<CanonicalRendererWork> productionBucket,
            IReadOnlyList<CapturePair> capturePairs)
        {
            var productionIdentities = new RendererStableIdentity[productionBucket.Count];
            for (int i = 0; i < productionIdentities.Length; i++)
            {
                CanonicalRendererWork work = productionBucket[i];
                productionIdentities[i] = new RendererStableIdentity(
                    work.ProductionStablePath,
                    work.ProductionComponentOrdinal);
            }

            var captureIdentities = new RendererStableIdentity[capturePairs.Count];
            for (int i = 0; i < captureIdentities.Length; i++)
            {
                CapturePair pair = capturePairs[i];
                if (!SameCaptureIdentity(pair.Baseline, pair.Full))
                {
                    throw new InvalidOperationException(
                        "Receiver Baseline/Full exact identity differs in '" + roomId + "' path '" +
                        relativePath + "' capture " + i + ".");
                }
                captureIdentities[i] = new RendererStableIdentity(
                    pair.Baseline.relativePath,
                    pair.Baseline.componentOrdinal);
            }

            int[] productionOccurrences = MatchCaptureIdentitiesToProductionOccurrences(
                roomId + " path '" + relativePath + "'",
                productionIdentities,
                captureIdentities);
            for (int captureIndex = 0; captureIndex < capturePairs.Count; captureIndex++)
            {
                CanonicalRendererWork target = productionBucket[productionOccurrences[captureIndex]];
                CapturePair pair = capturePairs[captureIndex];
                if (target.BaselineCapture.HasValue || target.FullCapture.HasValue)
                {
                    throw new InvalidOperationException(
                        "Receiver capture identity was assigned twice for '" + roomId + "' key '" +
                        target.CanonicalKey + "'.");
                }
                ValidateCaptureMeshIdentity(target, pair.Baseline, roomId);
                target.BaselineCapture = pair.Baseline;
                target.FullCapture = pair.Full;
            }
            return capturePairs.Count;
        }

        internal readonly struct BaseOccurrenceBindingForTest
        {
            public readonly string CanonicalKey;
            public readonly string ProductionStablePath;
            public readonly int P0LightmapIndex;
            public readonly Vector4 P0ScaleOffset;
            public readonly int P100LightmapIndex;
            public readonly Vector4 P100ScaleOffset;

            internal BaseOccurrenceBindingForTest(
                string canonicalKey,
                string productionStablePath,
                int p0LightmapIndex,
                Vector4 p0ScaleOffset,
                int p100LightmapIndex,
                Vector4 p100ScaleOffset)
            {
                CanonicalKey = canonicalKey;
                ProductionStablePath = productionStablePath;
                P0LightmapIndex = p0LightmapIndex;
                P0ScaleOffset = p0ScaleOffset;
                P100LightmapIndex = p100LightmapIndex;
                P100ScaleOffset = p100ScaleOffset;
            }
        }

        internal static BaseOccurrenceBindingForTest[] BindBaseOccurrencesForTest(
            string relativePath,
            string[] stablePathsInRuntimeTraversalOrder,
            DungeonTileBakeData.RendererBakeEntry[] p0EntriesInOccurrenceOrder,
            DungeonTileBakeData.RendererBakeEntry[] p100EntriesInOccurrenceOrder)
        {
            stablePathsInRuntimeTraversalOrder = stablePathsInRuntimeTraversalOrder ?? Array.Empty<string>();
            p0EntriesInOccurrenceOrder = p0EntriesInOccurrenceOrder ??
                Array.Empty<DungeonTileBakeData.RendererBakeEntry>();
            p100EntriesInOccurrenceOrder = p100EntriesInOccurrenceOrder ??
                Array.Empty<DungeonTileBakeData.RendererBakeEntry>();
            if (p0EntriesInOccurrenceOrder.Length != p100EntriesInOccurrenceOrder.Length ||
                stablePathsInRuntimeTraversalOrder.Length != p0EntriesInOccurrenceOrder.Length)
            {
                throw new ArgumentException("Test occurrence buckets must have identical cardinality.");
            }

            var work = new List<CanonicalRendererWork>(p0EntriesInOccurrenceOrder.Length);
            var identities = new List<ProductionRendererIdentity>(stablePathsInRuntimeTraversalOrder.Length);
            for (int i = 0; i < p0EntriesInOccurrenceOrder.Length; i++)
            {
                work.Add(new CanonicalRendererWork
                {
                    CanonicalKey = relativePath + "#" + i.ToString(CultureInfo.InvariantCulture),
                    ProductionRelativePath = relativePath,
                    Occurrence = i,
                    P0 = p0EntriesInOccurrenceOrder[i],
                    P100 = p100EntriesInOccurrenceOrder[i]
                });
                identities.Add(new ProductionRendererIdentity(
                    stablePathsInRuntimeTraversalOrder[i],
                    0,
                    null,
                    string.Empty,
                    0L,
                    string.Empty,
                    default));
            }

            BindProductionRendererBucketInRuntimeTraversalOrder(
                "TEST",
                relativePath,
                work,
                identities);
            var result = new BaseOccurrenceBindingForTest[work.Count];
            for (int i = 0; i < result.Length; i++)
            {
                CanonicalRendererWork source = work[i];
                result[i] = new BaseOccurrenceBindingForTest(
                    source.CanonicalKey,
                    source.ProductionStablePath,
                    source.P0.lightmapIndex,
                    source.P0.lightmapScaleOffset,
                    source.P100.lightmapIndex,
                    source.P100.lightmapScaleOffset);
            }
            return result;
        }

        private static Dictionary<string, List<DungeonTileBakeData.RendererBakeEntry>> GroupProductionEntries(
            DungeonTileBakeData.RendererBakeEntry[] entries,
            string label)
        {
            var result = new Dictionary<string, List<DungeonTileBakeData.RendererBakeEntry>>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                DungeonTileBakeData.RendererBakeEntry entry = entries[i];
                if (string.IsNullOrWhiteSpace(entry.relativePath))
                    throw new InvalidOperationException(label + " has an empty renderer relative path at index " + i + ".");
                if (!result.TryGetValue(entry.relativePath, out List<DungeonTileBakeData.RendererBakeEntry> bucket))
                {
                    bucket = new List<DungeonTileBakeData.RendererBakeEntry>();
                    result.Add(entry.relativePath, bucket);
                }
                bucket.Add(entry);
            }
            return result;
        }

        private static void ValidateProductionRendererEntry(
            string label,
            DungeonTileBakeData.RendererBakeEntry entry,
            int lightmapCount)
        {
            if (string.IsNullOrWhiteSpace(entry.relativePath) || entry.lightmapIndex < 0 ||
                entry.lightmapIndex >= lightmapCount || !IsFinite(entry.lightmapScaleOffset) ||
                entry.lightmapScaleOffset.x <= 0f || entry.lightmapScaleOffset.y <= 0f)
            {
                throw new InvalidOperationException(label + " has an invalid renderer lightmap entry at '" +
                                                    entry.relativePath + "'.");
            }
        }

        private static Dictionary<string, List<ProductionRendererIdentity>> LoadProductionRendererIdentities(
            GameObject prefab,
            IEnumerable<string> expectedPaths)
        {
            if (prefab == null)
                throw new ArgumentNullException(nameof(prefab));
            var expected = new HashSet<string>(expectedPaths, StringComparer.Ordinal);
            var result = new Dictionary<string, List<ProductionRendererIdentity>>(StringComparer.Ordinal);
            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Renderer[] renderers = contents.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    // Match the production BakeData writer exactly.  In particular, these
                    // visual-only renderers are deliberately omitted from RendererBakeEntry
                    // and must not consume an occurrence in an otherwise duplicate path
                    // bucket.
                    if (renderer == null ||
                        NewPrisonDecalUtility.IsWallDecalRenderer(renderer) ||
                        NewPrisonReceptionWindowBakeSafeSetup.IsReceptionWindowVisualRenderer(renderer))
                        continue;
                    string relativePath = GetProductionRelativePath(contents.transform, renderer.transform);
                    if (!expected.Contains(relativePath))
                        continue;
                    if (!result.TryGetValue(relativePath, out List<ProductionRendererIdentity> bucket))
                    {
                        bucket = new List<ProductionRendererIdentity>();
                        result.Add(relativePath, bucket);
                    }
                    Mesh mesh = GetRendererMeshForTriangleRaster(renderer);
                    LightmapUvMode lightmapUvMode = GetLightmapUvMode(renderer, mesh);
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string meshGuid, out long meshLocalId) ||
                        string.IsNullOrWhiteSpace(meshGuid) || meshLocalId == 0L)
                    {
                        throw new InvalidOperationException(
                            "Unable to fingerprint the persistent production mesh for renderer '" + renderer.name + "'.");
                    }
                    bucket.Add(new ProductionRendererIdentity(
                        GetStableRelativePath(contents.transform, renderer.transform),
                        GetRendererComponentOrdinal(renderer),
                        mesh,
                        meshGuid,
                        meshLocalId,
                        ComputeUv2Hash(mesh),
                        lightmapUvMode));
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return result;
        }

        private static Dictionary<string, List<CapturePair>> BuildCapturePairs(
            DungeonPortalReceiverResponseCapture.CaptureState baseline,
            DungeonPortalReceiverResponseCapture.CaptureState full,
            string roomId)
        {
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] baselineRenderers = baseline.renderers ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] fullRenderers = full.renderers ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            if (baselineRenderers.Length != fullRenderers.Length)
                throw new InvalidOperationException("Receiver Baseline/Full renderer count differs for '" + roomId + "'.");

            var result = new Dictionary<string, List<CapturePair>>(StringComparer.Ordinal);
            for (int i = 0; i < baselineRenderers.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.CaptureRenderer baselineEntry = baselineRenderers[i];
                DungeonPortalReceiverResponseCapture.CaptureRenderer fullEntry = fullRenderers[i];
                if (!SameCaptureIdentity(baselineEntry, fullEntry))
                    throw new InvalidOperationException("Receiver Baseline/Full mapping identity differs at index " + i +
                                                        " for '" + roomId + "'.");
                string normalizedPath = StripStableSiblingIndices(baselineEntry.relativePath);
                if (!result.TryGetValue(normalizedPath, out List<CapturePair> bucket))
                {
                    bucket = new List<CapturePair>();
                    result.Add(normalizedPath, bucket);
                }
                bucket.Add(new CapturePair(baselineEntry, fullEntry));
            }
            return result;
        }

        private static bool SameCaptureIdentity(
            DungeonPortalReceiverResponseCapture.CaptureRenderer left,
            DungeonPortalReceiverResponseCapture.CaptureRenderer right)
        {
            return string.Equals(left.relativePath, right.relativePath, StringComparison.Ordinal) &&
                   left.rendererBucketIndex == right.rendererBucketIndex &&
                   left.componentOrdinal == right.componentOrdinal &&
                   string.Equals(left.meshAssetGuid, right.meshAssetGuid, StringComparison.Ordinal) &&
                   left.meshLocalId == right.meshLocalId &&
                   string.Equals(left.meshUv2Hash, right.meshUv2Hash, StringComparison.Ordinal) &&
                   left.lightmapIndex == right.lightmapIndex &&
                   Approximately(left.lightmapScaleOffset, right.lightmapScaleOffset);
        }

        private static void ValidateAngleCaptureBindings(ResolvedRoom room)
        {
            if (room == null || room.AngleCapture == null)
                throw new InvalidOperationException("Endpoint-native room has no validated door-angle capture.");
            DungeonPortalBakedBasisDoorAngleCapture.AngleState[] baselines =
                room.AngleCapture.MatchedBaselineAngleStates;
            DungeonPortalBakedBasisDoorAngleCapture.AngleState[] full =
                room.AngleCapture.FullAngleStates;
            if (baselines.Length != RequiredAnglePoseCount || full.Length != RequiredAnglePoseCount)
            {
                throw new InvalidOperationException(
                    "Endpoint-native angle capture must contain exactly four matched D025/D050/D075/D100 pairs for '" +
                    room.Spec.RoomId + "'.");
            }
            for (int pose = 0; pose < RequiredAnglePoseCount; pose++)
            {
                ValidateAngleStateProductionBindings(room, baselines[pose], "Baseline");
                ValidateAngleStateProductionBindings(room, full[pose], "Full");
                if (!DungeonPortalBakedBasisDoorAngleCapture.TryValidateMatchedPosePair(
                        baselines[pose], full[pose], out string pairFailure))
                {
                    throw new InvalidOperationException(
                        "Endpoint-native angle capture pose pair validation failed for '" + room.Spec.RoomId +
                        "' pose " + pose + ": " + pairFailure);
                }
            }
        }

        private static void ValidateAngleStateProductionBindings(
            ResolvedRoom room,
            DungeonPortalBakedBasisDoorAngleCapture.AngleState state,
            string stateLabel)
        {
            var productionByIdentity = new Dictionary<RendererStableIdentity, CanonicalRendererWork>();
            for (int i = 0; i < room.CanonicalRenderers.Length; i++)
            {
                CanonicalRendererWork work = room.CanonicalRenderers[i];
                var identity = new RendererStableIdentity(
                    work.ProductionStablePath,
                    work.ProductionComponentOrdinal);
                if (!productionByIdentity.TryAdd(identity, work))
                {
                    throw new InvalidOperationException(
                        "Production renderer identity is ambiguous while binding angle capture for '" +
                        room.Spec.RoomId + "': " + identity + ".");
                }
            }

            DungeonPortalReceiverResponseCapture.CaptureRenderer[] renderers = state.renderers ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            var seen = new HashSet<RendererStableIdentity>();
            for (int i = 0; i < renderers.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.CaptureRenderer captured = renderers[i];
                var identity = new RendererStableIdentity(captured.relativePath, captured.componentOrdinal);
                if (!seen.Add(identity) || !productionByIdentity.TryGetValue(identity, out CanonicalRendererWork work))
                {
                    throw new InvalidOperationException(
                        "Angle capture " + stateLabel + " renderer has no unique exact production " +
                        "relativePath+componentOrdinal binding for '" + room.Spec.RoomId + "': " + identity + ".");
                }
                ValidateCaptureMeshIdentity(work, captured, room.Spec.RoomId);
            }
            if (renderers.Length != room.Spec.ExpectedCaptureRendererCount)
            {
                throw new InvalidOperationException(
                    "Angle capture " + stateLabel + " renderer count drifted for '" + room.Spec.RoomId +
                    "'. expected=" + room.Spec.ExpectedCaptureRendererCount + " actual=" + renderers.Length + ".");
            }
        }

        private static string GetProductionRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || (target != root && !target.IsChildOf(root)))
                throw new InvalidOperationException("Cannot resolve a production relative path outside the prefab root.");
            if (target == root)
                return string.Empty;
            var segments = new List<string>();
            for (Transform current = target; current != null && current != root; current = current.parent)
                segments.Add(current.name);
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static string GetStableRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || (target != root && !target.IsChildOf(root)))
                throw new InvalidOperationException("Cannot resolve a stable hierarchy path outside the prefab root.");
            if (target == root)
                return ".";
            var segments = new List<string>();
            for (Transform current = target; current != null && current != root; current = current.parent)
            {
                segments.Add(current.name + "[" + current.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) + "]");
            }
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static string StripStableSiblingIndices(string stablePath)
        {
            if (string.IsNullOrWhiteSpace(stablePath))
                throw new InvalidOperationException("Receiver capture has an empty stable hierarchy path.");
            string[] segments = stablePath.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                int bracket = segment.LastIndexOf('[');
                if (bracket <= 0 || !segment.EndsWith("]", StringComparison.Ordinal) ||
                    !int.TryParse(segment.Substring(bracket + 1, segment.Length - bracket - 2),
                        NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    throw new InvalidOperationException(
                        "Receiver capture path is not the required sibling-indexed form: '" + stablePath + "'.");
                }
                segments[i] = segment.Substring(0, bracket);
            }
            return string.Join("/", segments);
        }

        private static int GetRendererComponentOrdinal(Renderer renderer)
        {
            Renderer[] renderers = renderer.GetComponents<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == renderer)
                    return i;
            }
            throw new InvalidOperationException("Renderer component ordinal could not be resolved.");
        }

        private static Mesh GetRendererMeshForTriangleRaster(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                return skinned.sharedMesh;
            if (renderer is MeshRenderer)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    return filter.sharedMesh;
            }
            throw new InvalidOperationException(
                "A canonical lightmapped renderer has no persistent Mesh/UV2 triangle source: '" +
                (renderer != null ? renderer.name : "null") + "'.");
        }

        private static LightmapUvMode GetLightmapUvMode(Renderer renderer, Mesh mesh)
        {
            if (renderer == null || mesh == null)
                throw new ArgumentNullException(renderer == null ? nameof(renderer) : nameof(mesh));

            // `additionalVertexStreams` can override TEXCOORD1 at draw time. The
            // capture identity hashes the persistent base mesh, so accepting an
            // unrecorded stream here would make the generated atlas unverifiable.
            if (renderer is MeshRenderer meshRenderer && meshRenderer.additionalVertexStreams != null &&
                meshRenderer.additionalVertexStreams.HasVertexAttribute(VertexAttribute.TexCoord1))
            {
                throw new InvalidOperationException(
                    "Renderer '" + renderer.name + "' supplies TEXCOORD1 through additionalVertexStreams; " +
                    "the captured base-mesh UV2 identity cannot safely author it.");
            }

            Vector2[] uv2;
            try
            {
                uv2 = mesh.uv2;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Unable to read UV2 for mesh '" + mesh.name + "'.", exception);
            }

            if (uv2 != null && uv2.Length == mesh.vertexCount)
                return LightmapUvMode.StoredUv2Triangles;
            if (uv2 == null || uv2.Length == 0)
            {
                if (mesh.HasVertexAttribute(VertexAttribute.TexCoord1))
                {
                    throw new InvalidOperationException(
                        "Mesh '" + mesh.name + "' advertises TEXCOORD1 but exposes no full readable UV2 array; " +
                        "authoring will not guess its lightmap mapping.");
                }

                // Unity supplies the missing TEXCOORD1 components as zero at draw
                // time. The lightmap lookup is therefore the single ST.zw point;
                // it is represented by a collision-gated finite footprint, never by
                // a whole ST rectangle or UV0 inference.
                return LightmapUvMode.ImplicitZero;
            }

            throw new InvalidOperationException(
                "Mesh '" + mesh.name + "' has a partial UV2 channel (" + uv2.Length + "/" +
                mesh.vertexCount + ") and cannot be authored safely.");
        }

        private static void ValidateCaptureMeshIdentity(
            CanonicalRendererWork work,
            DungeonPortalReceiverResponseCapture.CaptureRenderer capture,
            string roomId)
        {
            if (work == null || work.ProductionMesh == null ||
                !string.Equals(work.ProductionMeshAssetGuid, capture.meshAssetGuid, StringComparison.Ordinal) ||
                work.ProductionMeshLocalId != capture.meshLocalId ||
                !string.Equals(work.ProductionMeshUv2Hash, capture.meshUv2Hash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Receiver capture mesh/UV2 identity does not match the canonical production renderer for '" +
                    roomId + "' key '" + (work != null ? work.CanonicalKey : "<null>") + "'.");
            }
        }

        private static string ComputeUv2Hash(Mesh mesh)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));
            Vector2[] uv2;
            try
            {
                uv2 = mesh.uv2;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Unable to read UV2 for mesh '" + mesh.name + "'.", exception);
            }
            if (uv2 != null && uv2.Length != 0 && uv2.Length != mesh.vertexCount)
            {
                throw new InvalidOperationException(
                    "Receiver mesh has a partial UV2 channel that cannot be fingerprinted safely: '" + mesh.name + "'.");
            }
            var builder = new StringBuilder((uv2 != null ? uv2.Length : 0) * 24 + 256);
            if (uv2 == null || uv2.Length == 0)
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId) ||
                    string.IsNullOrWhiteSpace(guid) || localId == 0L)
                {
                    throw new InvalidOperationException(
                        "Receiver mesh without stored UV2 is not a persistent asset: '" + mesh.name + "'.");
                }
                string assetPath = AssetDatabase.GetAssetPath(mesh);
                if (string.IsNullOrWhiteSpace(assetPath))
                    throw new InvalidOperationException("Receiver mesh has no asset path: '" + mesh.name + "'.");
                ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                AppendString(builder, "NO_STORED_UV2");
                AppendString(builder, guid);
                builder.Append(localId).Append('|');
                AppendString(builder, assetPath);
                AppendString(builder, GetDependencyHash(assetPath));
                AppendString(builder, importer != null ? "ModelImporter" : "NO_MODEL_IMPORTER");
                builder.Append(importer != null && importer.generateSecondaryUV ? '1' : '0').Append('|');
                builder.Append(mesh.vertexCount).Append('|');
                builder.Append(mesh.subMeshCount).Append('|');
                builder.Append((int)mesh.indexFormat).Append('|');
                return ComputeSha256(builder.ToString());
            }
            AppendString(builder, "STORED_UV2");
            builder.Append(mesh.vertexCount).Append('|');
            for (int i = 0; i < uv2.Length; i++)
            {
                AppendFloat(builder, uv2[i].x);
                AppendFloat(builder, uv2[i].y);
            }
            return ComputeSha256(builder.ToString());
        }

        private sealed class ProductionRendererIdentity
        {
            public readonly string StablePath;
            public readonly int ComponentOrdinal;
            public readonly Mesh Mesh;
            public readonly string MeshAssetGuid;
            public readonly long MeshLocalId;
            public readonly string MeshUv2Hash;
            public readonly LightmapUvMode LightmapUvMode;

            public ProductionRendererIdentity(
                string stablePath,
                int componentOrdinal,
                Mesh mesh,
                string meshAssetGuid,
                long meshLocalId,
                string meshUv2Hash,
                LightmapUvMode lightmapUvMode)
            {
                StablePath = stablePath;
                ComponentOrdinal = componentOrdinal;
                Mesh = mesh;
                MeshAssetGuid = meshAssetGuid;
                MeshLocalId = meshLocalId;
                MeshUv2Hash = meshUv2Hash;
                LightmapUvMode = lightmapUvMode;
            }
        }

        private sealed class CapturePair
        {
            public readonly DungeonPortalReceiverResponseCapture.CaptureRenderer Baseline;
            public readonly DungeonPortalReceiverResponseCapture.CaptureRenderer Full;

            public CapturePair(
                DungeonPortalReceiverResponseCapture.CaptureRenderer baseline,
                DungeonPortalReceiverResponseCapture.CaptureRenderer full)
            {
                Baseline = baseline;
                Full = full;
            }
        }

        private static InputFingerprint[] BuildInputFingerprints(IReadOnlyList<ResolvedRoom> rooms)
        {
            var records = new List<InputFingerprint>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            AddInputFingerprint(records, seen, ChartCopyShaderPath, "editor-only GPU chart copy shader");
            for (int i = 0; i < rooms.Count; i++)
            {
                ResolvedRoom room = rooms[i];
                AddInputFingerprint(records, seen, room.Spec.ProductionPrefabPath, room.Spec.RoomId + " production prefab read-only identity");
                AddInputFingerprint(records, seen, room.Spec.P0BakeDataPath, room.Spec.RoomId + " production P0 bake data");
                AddInputFingerprint(records, seen, room.Spec.P100BakeDataPath, room.Spec.RoomId + " production P100 bake data");
                AddInputFingerprint(records, seen, room.Spec.ReceiverCapturePath, room.Spec.RoomId + " raw receiver response capture");
                AddInputFingerprint(records, seen, room.Spec.EndpointProfilePath, room.Spec.RoomId + " K1 source endpoint profile");
            }
            return records.ToArray();
        }

        private static InputFingerprint[] BuildEndpointNativeInputFingerprints(
            IReadOnlyList<ResolvedRoom> rooms)
        {
            var records = new List<InputFingerprint>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            AddInputFingerprint(records, seen, ChartCopyShaderPath, "editor-only GPU chart copy shader");
            for (int i = 0; i < rooms.Count; i++)
            {
                ResolvedRoom room = rooms[i];
                AddInputFingerprint(records, seen, room.Spec.ProductionPrefabPath,
                    room.Spec.RoomId + " production prefab read-only identity");
                AddInputFingerprint(records, seen, room.Spec.P0BakeDataPath,
                    room.Spec.RoomId + " production P0 bake data");
                AddInputFingerprint(records, seen, room.Spec.P100BakeDataPath,
                    room.Spec.RoomId + " production P100 bake data");
                AddInputFingerprint(records, seen, room.Spec.EndpointProfilePath,
                    room.Spec.RoomId + " K1 source endpoint profile");
                AddInputFingerprint(records, seen, room.AngleCaptureAssetPath,
                    room.Spec.RoomId + " immutable D025/D050/D075/D100 angle capture");
            }
            return records.ToArray();
        }

        private static AngleCaptureIntegrityRecord BuildAngleCaptureIntegrityRecord(ResolvedRoom room)
        {
            if (room == null || room.AngleCapture == null)
            {
                throw new InvalidOperationException(
                    "Angle-capture structural validation failed for endpoint-native room '" +
                    (room != null ? room.Spec.RoomId : "<null>") + ": capture is missing.");
            }
            if (!room.AngleCapture.TryValidate(out string validationFailure))
            {
                throw new InvalidOperationException(
                    "Angle-capture structural validation failed for endpoint-native room '" +
                    room.Spec.RoomId + "': " + validationFailure);
            }
            DungeonPortalBakedBasisDoorAngleCapture.AngleState[] baselines =
                room.AngleCapture.MatchedBaselineAngleStates;
            DungeonPortalBakedBasisDoorAngleCapture.AngleState[] full =
                room.AngleCapture.FullAngleStates;
            var result = new AngleCaptureIntegrityRecord
            {
                receiverRoomId = room.Spec.RoomId,
                angleCaptureAssetPath = room.AngleCaptureAssetPath,
                angleCaptureDependencyHash = GetDependencyHash(room.AngleCaptureAssetPath),
                structuralValidationPassed = true,
                structuralValidationError = string.Empty,
                captureSchemaVersion = room.AngleCapture.SchemaVersion,
                canonicalWorkspaceDependencyHash =
                    room.AngleCapture.Provenance.canonicalWorkspaceDependencyHash,
                poses = new AngleCapturePoseIntegrityRecord[RequiredAnglePoseCount]
            };
            for (int i = 0; i < RequiredAnglePoseCount; i++)
            {
                result.poses[i] = new AngleCapturePoseIntegrityRecord
                {
                    poseId = PoseIdForIndex(i),
                    openFraction = full[i].openFraction,
                    angleDegrees = full[i].angleDegrees,
                    baselineStateId = baselines[i].stateId,
                    baselineStateHash = baselines[i].stateHash,
                    fullStateId = full[i].stateId,
                    fullStateHash = full[i].stateHash,
                    probeStencilPolicy = full[i].probeStencilPolicy,
                    probeLocalPositionSignature = full[i].probeLocalPositionSignature,
                    firstNineBaselineShSignature = ComputeFirstNineProbeShSignature(baselines[i]),
                    firstNineFullShSignature = ComputeFirstNineProbeShSignature(full[i])
                };
            }
            return result;
        }

        private static void AddInputFingerprint(
            List<InputFingerprint> records,
            HashSet<string> seen,
            string assetPath,
            string purpose)
        {
            if (!seen.Add(assetPath))
                return;
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                throw new InvalidOperationException("Cannot fingerprint missing required input: '" + assetPath + "'.");
            records.Add(new InputFingerprint
            {
                assetPath = assetPath,
                dependencyHash = GetDependencyHash(assetPath),
                purpose = purpose
            });
        }

        private static CaptureIntegrityRecord BuildCaptureIntegrityRecord(ResolvedRoom room)
        {
            bool structural = room.CaptureAsset.TryValidate(out string structuralError);
            if (!structural)
                throw new InvalidOperationException("Capture structural validation failed for '" + room.Spec.RoomId + "': " + structuralError);

            string baselineHash = ComputeCaptureStateHash(room.Baseline);
            string fullHash = ComputeCaptureStateHash(room.Full);
            bool baselineRecomputed = string.Equals(room.Baseline.stateHash, baselineHash, StringComparison.Ordinal);
            bool fullRecomputed = string.Equals(room.Full.stateHash, fullHash, StringComparison.Ordinal);
            if (!baselineRecomputed || !fullRecomputed)
            {
                throw new InvalidOperationException(
                    "Public receiver-state hash recomputation failed for '" + room.Spec.RoomId +
                    "'. The raw response evidence is not safe to repack.");
            }

            EvaluateCaptureInputWaiver(
                room.CaptureAsset.Provenance,
                room.Spec.RoomId,
                out bool allNonWaivedMatch,
                out bool waiverApplied,
                out string waiverAssetPath,
                out string savedHash,
                out string currentHash);

            return new CaptureIntegrityRecord
            {
                receiverRoomId = room.Spec.RoomId,
                captureAssetPath = room.Spec.ReceiverCapturePath,
                structuralValidationPassed = structural,
                structuralValidationError = structuralError ?? string.Empty,
                baselineStateHash = room.Baseline.stateHash,
                fullStateHash = room.Full.stateHash,
                baselineStateHashRecomputed = baselineRecomputed,
                fullStateHashRecomputed = fullRecomputed,
                allNonWaivedProductionHashesMatch = allNonWaivedMatch,
                rotationBakeToolHashOnlyWaiverApplied = waiverApplied,
                waiverAssetPath = waiverAssetPath ?? string.Empty,
                savedDependencyHash = savedHash ?? string.Empty,
                currentDependencyHash = currentHash ?? string.Empty,
                strictValidationStatus =
                    "NOT_CLAIMED: the workspace-opening strict persisted validator was not invoked. " +
                    "This generated payload records only public structural validation, dependency hashes, and " +
                    "the explicitly bounded non-executing RotationBakeTool hash waiver."
            };
        }

        private static void EvaluateCaptureInputWaiver(
            DungeonPortalReceiverResponseCapture.CaptureProvenance provenance,
            string roomId,
            out bool allNonWaivedMatch,
            out bool waiverApplied,
            out string waiverAssetPath,
            out string savedHash,
            out string currentHash)
        {
            const string permittedWaiverPath = "Assets/Editor/DungeonTileRotationBakeTool.cs";
            DungeonPortalReceiverResponseCapture.AssetFingerprint[] inputs = provenance.productionInputs ??
                Array.Empty<DungeonPortalReceiverResponseCapture.AssetFingerprint>();
            if (inputs.Length == 0)
                throw new InvalidOperationException("Receiver capture provenance has no production input hashes for '" + roomId + "'.");

            allNonWaivedMatch = true;
            waiverApplied = false;
            waiverAssetPath = string.Empty;
            savedHash = string.Empty;
            currentHash = string.Empty;
            int mismatchCount = 0;
            for (int i = 0; i < inputs.Length; i++)
            {
                DungeonPortalReceiverResponseCapture.AssetFingerprint input = inputs[i];
                if (string.IsNullOrWhiteSpace(input.assetPath) || string.IsNullOrWhiteSpace(input.dependencyHash) ||
                    input.wasDirtyBeforeCapture || input.wasDirtyAfterCapture ||
                    AssetDatabase.LoadMainAssetAtPath(input.assetPath) == null)
                {
                    throw new InvalidOperationException(
                        "Receiver capture provenance input is incomplete or no longer exists at index " + i +
                        " for '" + roomId + "'.");
                }

                string actual = GetDependencyHash(input.assetPath);
                if (string.Equals(input.dependencyHash, actual, StringComparison.Ordinal))
                    continue;

                mismatchCount++;
                if (!string.Equals(input.assetPath, permittedWaiverPath, StringComparison.Ordinal))
                {
                    allNonWaivedMatch = false;
                    throw new InvalidOperationException(
                        "Receiver capture provenance changed outside the permitted non-executing waiver for '" +
                        roomId + "': '" + input.assetPath + "'. saved=" + input.dependencyHash +
                        " current=" + actual + ".");
                }

                waiverApplied = true;
                waiverAssetPath = input.assetPath;
                savedHash = input.dependencyHash;
                currentHash = actual;
            }

            if (mismatchCount > 1 || (mismatchCount == 1 && !waiverApplied))
            {
                throw new InvalidOperationException(
                    "Receiver capture provenance mismatch set is broader than the permitted RotationBakeTool waiver for '" +
                    roomId + "'.");
            }
        }

        private static CookieComparisonRecord CompareEndpointCookies(ResolvedRoom left, ResolvedRoom right)
        {
            DungeonPortalEndpointProfile.PortalDirectLightDescriptor leftDescriptor =
                GetSingleDirectDescriptor(left.Endpoint, left.Spec.RoomId);
            DungeonPortalEndpointProfile.PortalDirectLightDescriptor rightDescriptor =
                GetSingleDirectDescriptor(right.Endpoint, right.Spec.RoomId);
            Texture2D leftCookie = leftDescriptor.cookie;
            Texture2D rightCookie = rightDescriptor.cookie;
            if (leftCookie == null || rightCookie == null ||
                leftCookie.width != rightCookie.width || leftCookie.height != rightCookie.height)
            {
                throw new InvalidOperationException("K1 endpoint cookies are missing or have incompatible dimensions.");
            }

            Color[] leftPixels = ReadLinearPixels(leftCookie);
            Color[] rightPixels = ReadLinearPixels(rightCookie);
            double dot = 0d;
            double leftLength = 0d;
            double rightLength = 0d;
            double leftMean = 0d;
            double rightMean = 0d;
            for (int i = 0; i < leftPixels.Length; i++)
            {
                Vector3 a = new Vector3(leftPixels[i].r, leftPixels[i].g, leftPixels[i].b);
                Vector3 b = new Vector3(rightPixels[i].r, rightPixels[i].g, rightPixels[i].b);
                dot += Vector3.Dot(a, b);
                leftLength += Vector3.Dot(a, a);
                rightLength += Vector3.Dot(b, b);
                leftMean += Luminance(a);
                rightMean += Luminance(b);
            }
            if (leftLength <= 0d || rightLength <= 0d)
                throw new InvalidOperationException("K1 endpoint cookie energy is zero.");
            double scale = dot / rightLength;
            double squaredError = 0d;
            for (int i = 0; i < leftPixels.Length; i++)
            {
                Vector3 a = new Vector3(leftPixels[i].r, leftPixels[i].g, leftPixels[i].b);
                Vector3 b = new Vector3(rightPixels[i].r, rightPixels[i].g, rightPixels[i].b) * (float)scale;
                squaredError += Vector3.Dot(a - b, a - b);
            }

            return new CookieComparisonRecord
            {
                leftRoomId = left.Spec.RoomId,
                rightRoomId = right.Spec.RoomId,
                bothCookiesLinearRgbaHalf =
                    leftCookie.format == TextureFormat.RGBAHalf && rightCookie.format == TextureFormat.RGBAHalf &&
                    !GraphicsFormatUtility.IsSRGBFormat(leftCookie.graphicsFormat) &&
                    !GraphicsFormatUtility.IsSRGBFormat(rightCookie.graphicsFormat),
                width = leftCookie.width,
                height = leftCookie.height,
                normalizedCosine = (float)(dot / Math.Sqrt(leftLength * rightLength)),
                bestScale = (float)scale,
                relativeRmse = (float)Math.Sqrt(squaredError / leftLength),
                leftMeanLuminance = (float)(leftMean / leftPixels.Length),
                rightMeanLuminance = (float)(rightMean / rightPixels.Length),
                interpretation =
                    "Source endpoint cookie shape similarity is recorded only as K=1 input provenance; raw receiver " +
                    "Full/Baseline captures used the canonical no-cookie baked injector, so this metric is not a visual acceptance gate."
            };
        }

        private static DungeonPortalEndpointProfile.PortalDirectLightDescriptor GetSingleDirectDescriptor(
            DungeonPortalEndpointProfile profile,
            string roomId)
        {
            DungeonPortalEndpointProfile.PortalDirectLightDescriptor[] descriptors = profile.OutgoingDirectLights;
            if (descriptors == null || descriptors.Length != 1)
                throw new InvalidOperationException("Endpoint profile does not expose exactly one K1 direct descriptor for '" + roomId + "'.");
            return descriptors[0];
        }

        private static float Luminance(Vector3 value)
        {
            return value.x * 0.2126f + value.y * 0.7152f + value.z * 0.0722f;
        }

        private static EndpointNativeRoomRecord BuildEndpointNativeRoomPayload(
            ResolvedRoom room,
            string stagingRoot,
            Material copyMaterial,
            List<ArtifactFingerprint> artifacts)
        {
            string roomFolder = stagingRoot + "/" + room.Spec.RoomId;
            EnsureFolder(roomFolder);
            EnsureFolder(roomFolder + "/Endpoints");

            BucketWork[] buckets = BuildBucketWork(room);
            EndpointNativeEndpointRecord p0 = BuildEndpointNativeEndpointRecord(
                room,
                buckets,
                EndpointLayout.Power0,
                roomFolder,
                copyMaterial,
                stagingRoot,
                artifacts);
            EndpointNativeEndpointRecord p100 = BuildEndpointNativeEndpointRecord(
                room,
                buckets,
                EndpointLayout.Power100,
                roomFolder,
                copyMaterial,
                stagingRoot,
                artifacts);

            var rendererRecords = new EndpointNativeRendererRecord[room.CanonicalRenderers.Length];
            for (int i = 0; i < rendererRecords.Length; i++)
            {
                CanonicalRendererWork renderer = room.CanonicalRenderers[i];
                rendererRecords[i] = new EndpointNativeRendererRecord
                {
                    canonicalKey = renderer.CanonicalKey,
                    p0LocalLightmapIndex = renderer.P0.lightmapIndex,
                    p0ScaleOffset = ToArray(renderer.P0.lightmapScaleOffset),
                    p100LocalLightmapIndex = renderer.P100.lightmapIndex,
                    p100ScaleOffset = ToArray(renderer.P100.lightmapScaleOffset)
                };
            }

            var record = new EndpointNativeRoomRecord
            {
                roomId = room.Spec.RoomId,
                productionPrefabPath = room.Spec.ProductionPrefabPath,
                p0BakeDataPath = room.Spec.P0BakeDataPath,
                p100BakeDataPath = room.Spec.P100BakeDataPath,
                angleCaptureAssetPath = room.AngleCaptureAssetPath,
                angleCaptureDependencyHash = GetDependencyHash(room.AngleCaptureAssetPath),
                canonicalRendererMappingSignature = ComputeCanonicalMappingSignature(room.CanonicalRenderers),
                endpoints = new[] { p0, p100 },
                rendererMappings = rendererRecords
            };
            string basisPath = roomFolder + "/" + room.Spec.RoomId + "_RoomBasis.asset";
            CreateEndpointNativeBasisAsset(
                room,
                record,
                stagingRoot,
                basisPath,
                stagingRoot,
                artifacts);
            record.basisAssetRelativePath = MakeRelativeAssetPath(stagingRoot, basisPath);
            record.basisDependencyHash = GetDependencyHash(basisPath);
            return record;
        }

        private static EndpointNativeEndpointRecord BuildEndpointNativeEndpointRecord(
            ResolvedRoom room,
            BucketWork[] buckets,
            EndpointLayout layout,
            string roomFolder,
            Material copyMaterial,
            string stagingRoot,
            List<ArtifactFingerprint> artifacts)
        {
            string endpointId = layout == EndpointLayout.Power100 ? "P100" : "P0";
            string endpointFolder = roomFolder + "/Endpoints/" + endpointId;
            List<ChartCopyOperation> oppositeBaseCopies = layout == EndpointLayout.Power100
                ? BuildP0CopyOperations(room, buckets)
                : BuildP100CopyOperations(room, buckets);
            string oppositeEndpointId = layout == EndpointLayout.Power100 ? "P0" : "P100";
            RepackedAtlasSet oppositeBase = WriteRepackedAtlasSet(
                endpointFolder + "/BaseTransition/OppositeEndpoint",
                oppositeEndpointId + "In" + endpointId + "Layout",
                buckets,
                oppositeBaseCopies,
                copyMaterial,
                stagingRoot,
                artifacts,
                true,
                layout,
                true);
            if (room.AngleCapture == null)
                throw new InvalidOperationException("Endpoint-native compact build requires a validated angle capture.");
            DungeonPortalBakedBasisDoorAngleCapture.AngleState[] baselineStates =
                room.AngleCapture.MatchedBaselineAngleStates;
            DungeonPortalBakedBasisDoorAngleCapture.AngleState[] fullStates =
                room.AngleCapture.FullAngleStates;
            var poses = new EndpointNativePoseRecord[RequiredAnglePoseCount];
            for (int poseIndex = 0; poseIndex < RequiredAnglePoseCount; poseIndex++)
            {
                DungeonPortalBakedBasisDoorAngleCapture.AngleState baselineState = baselineStates[poseIndex];
                DungeonPortalBakedBasisDoorAngleCapture.AngleState fullState = fullStates[poseIndex];
                string poseId = PoseIdForIndex(poseIndex);
                List<ChartCopyOperation> baselineCopies = BuildAngleResponseCopyOperations(
                    room, buckets, baselineState, layout, endpointId + " " + poseId + " Baseline");
                List<ChartCopyOperation> fullCopies = BuildAngleResponseCopyOperations(
                    room, buckets, fullState, layout, endpointId + " " + poseId + " Full");
                CompactResponseAtlasSet response = WriteCompactNormalizedResponseAtlasSet(
                    endpointFolder + "/ReceiverBasis/Poses/" + poseId + "/NormalizedSignedDelta",
                    endpointId + " " + poseId + " compact response",
                    buckets,
                    baselineCopies,
                    fullCopies,
                    copyMaterial,
                    stagingRoot,
                    artifacts,
                    layout);

                var atlases = new EndpointNativeAtlasRecord[buckets.Length];
                for (int i = 0; i < buckets.Length; i++)
                {
                    BucketWork bucket = buckets[i];
                    Texture2D baseColor = layout == EndpointLayout.Power100 ? bucket.P100Color : bucket.P0Color;
                    Texture2D baseDirection = layout == EndpointLayout.Power100 ? bucket.P100Direction : bucket.P0Direction;
                    atlases[i] = CreateEndpointNativeCompactAtlasRecord(
                        bucket,
                        baseColor,
                        baseDirection,
                        oppositeBase,
                        response,
                        i);
                }
                poses[poseIndex] = new EndpointNativePoseRecord
                {
                    poseId = poseId,
                    openFraction = fullState.openFraction,
                    angleDegrees = fullState.angleDegrees,
                    baselineStateId = baselineState.stateId,
                    baselineStateHash = baselineState.stateHash,
                    fullStateId = fullState.stateId,
                    fullStateHash = fullState.stateHash,
                    probeStencilPolicy = fullState.probeStencilPolicy,
                    probeLocalPositionSignature = fullState.probeLocalPositionSignature,
                    firstNineBaselineShSignature = ComputeFirstNineProbeShSignature(baselineState),
                    firstNineFullShSignature = ComputeFirstNineProbeShSignature(fullState),
                    atlases = atlases
                };
            }

            return new EndpointNativeEndpointRecord
            {
                endpointId = endpointId,
                exactBaseReferences = true,
                baseTransitionPolicy = EndpointNativeEndpointBaseTransitionPolicy,
                responseMipPolicy = EndpointNativeEndpointResponseMipPolicy,
                poses = poses,
                // Reject V1's flat PrecomputedDelta payload even if it is copied into a
                // folder named like this V2 endpoint record.
                atlases = Array.Empty<EndpointNativeAtlasRecord>()
            };
        }

        private static EndpointNativeAtlasRecord CreateEndpointNativeCompactAtlasRecord(
            BucketWork bucket,
            Texture2D baseColor,
            Texture2D baseDirection,
            RepackedAtlasSet oppositeBase,
            CompactResponseAtlasSet response,
            int index)
        {
            if (bucket == null || baseColor == null || baseDirection == null || oppositeBase == null ||
                response == null || index < 0 || index >= response.ColorPositive.Length)
            {
                throw new ArgumentException("Endpoint-native compact atlas inputs are incomplete.");
            }
            return new EndpointNativeAtlasRecord
            {
                localLightmapIndex = bucket.LocalLightmapIndex,
                baseColorAssetPath = AssetDatabase.GetAssetPath(baseColor),
                baseColorDependencyHash = GetDependencyHash(AssetDatabase.GetAssetPath(baseColor)),
                baseDirectionAssetPath = AssetDatabase.GetAssetPath(baseDirection),
                baseDirectionDependencyHash = GetDependencyHash(AssetDatabase.GetAssetPath(baseDirection)),
                width = baseColor.width,
                height = baseColor.height,
                baseColorMipmapCount = baseColor.mipmapCount,
                baseDirectionMipmapCount = baseDirection.mipmapCount,
                oppositeBaseColorRelativePath = oppositeBase.ColorRelativePaths[index],
                oppositeBaseDirectionRelativePath = oppositeBase.DirectionRelativePaths[index],
                oppositeBaseColorStorageFormat = oppositeBase.Colors[index].format.ToString(),
                oppositeBaseMipmapCount = oppositeBase.Colors[index].mipmapCount,
                oppositeBaseOperationCount = oppositeBase.ColorRasterization[index].OperationCount,
                oppositeBaseOwnedCoreTexelCount = oppositeBase.ColorRasterization[index].OwnedCoreTexelCount,
                oppositeBaseImplicitZeroCoreTexelCount =
                    oppositeBase.ColorRasterization[index].ImplicitZeroCoreTexelCount,
                oppositeBaseOwnerCollisionCount = oppositeBase.ColorRasterization[index].OwnerCollisionCount,
                oppositeBaseCanonicalOrientationProofSampledTexelCount =
                    oppositeBase.ColorRasterization[index].CanonicalOrientationProofSampledTexelCount,
                oppositeBaseCanonicalOrientationProofMaxAbsoluteChannelError =
                    oppositeBase.ColorRasterization[index].CanonicalOrientationProofMaxAbsoluteChannelError,
                responseEncoding = "NormalizedPositiveNegativeDeltaCanonicalPositiveZeroSelectiveCompactPairFallbacks",
                colorStorageFormat = response.ColorPositive[index].format.ToString(),
                directionalMomentStorageFormat = response.MomentPositive[index].format.ToString(),
                colorPositiveRelativePath = response.ColorPositiveRelativePaths[index],
                colorNegativeRelativePath = response.ColorNegativeRelativePaths[index],
                directionalMomentPositiveRelativePath = response.MomentPositiveRelativePaths[index],
                directionalMomentNegativeRelativePath = response.MomentNegativeRelativePaths[index],
                colorPositiveScales = CloneVector4Array(response.ColorPositiveScales[index]),
                colorNegativeScales = CloneVector4Array(response.ColorNegativeScales[index]),
                directionalMomentPositiveScales = CloneVector4Array(response.MomentPositiveScales[index]),
                directionalMomentNegativeScales = CloneVector4Array(response.MomentNegativeScales[index]),
                colorReconstructionByMip = response.ColorReconstruction[index],
                directionalMomentReconstructionByMip = response.MomentReconstruction[index],
                responseMipmapCount = response.ColorPositive[index].mipmapCount,
                operationCount = response.BaselineColorRasterization[index].OperationCount,
                baselineOwnedCoreTexelCount = response.BaselineColorRasterization[index].OwnedCoreTexelCount,
                baselineImplicitZeroCoreTexelCount =
                    response.BaselineColorRasterization[index].ImplicitZeroCoreTexelCount,
                baselineOwnerCollisionCount = response.BaselineColorRasterization[index].OwnerCollisionCount,
                fullOwnedCoreTexelCount = response.FullColorRasterization[index].OwnedCoreTexelCount,
                fullImplicitZeroCoreTexelCount = response.FullColorRasterization[index].ImplicitZeroCoreTexelCount,
                fullOwnerCollisionCount = response.FullColorRasterization[index].OwnerCollisionCount,
                // Legacy signed-delta fields must remain empty in a V2 record.
                colorDeltaRelativePath = string.Empty,
                directionalMomentDeltaRelativePath = string.Empty,
                colorDeltaOwnedMip0Quantization = null,
                directionalMomentDeltaOwnedMip0Quantization = null
            };
        }

        private static RoomRecord BuildRoomPayload(
            ResolvedRoom room,
            string stagingRoot,
            Material copyMaterial,
            List<ArtifactFingerprint> artifacts)
        {
            string roomFolder = stagingRoot + "/" + room.Spec.RoomId;
            EnsureFolder(roomFolder);
            EnsureFolder(roomFolder + "/Atlases");

            BucketWork[] buckets = BuildBucketWork(room);
            List<ChartCopyOperation> p100Copies = BuildP100CopyOperations(room, buckets);
            RepackedAtlasSet p100Atlases = WriteRepackedAtlasSet(
                roomFolder + "/Atlases/P100",
                "P100",
                buckets,
                p100Copies,
                copyMaterial,
                stagingRoot,
                artifacts,
                true);

            List<ChartCopyOperation> baselineCopies = BuildResponseCopyOperations(room, buckets, false);
            RepackedAtlasSet baselineAtlases = WriteRepackedAtlasSet(
                roomFolder + "/Atlases/ReceiverBasis/Baseline",
                "ReceiverBaseline",
                buckets,
                baselineCopies,
                copyMaterial,
                stagingRoot,
                artifacts,
                false);

            List<ChartCopyOperation> fullCopies = BuildResponseCopyOperations(room, buckets, true);
            RepackedAtlasSet fullAtlases = WriteRepackedAtlasSet(
                roomFolder + "/Atlases/ReceiverBasis/Full",
                "ReceiverFull",
                buckets,
                fullCopies,
                copyMaterial,
                stagingRoot,
                artifacts,
                false);

            DungeonPortalBakedRoomBasisData.OptionalDoorwayProbeResponse doorwayProbe =
                BuildDoorwayProbeResponse(room, out DoorwayProbeRecord doorwayProbeRecord);
            DungeonPortalBakedRoomBasisData.OptionalAmbientProbeStates ambientStates =
                BuildAmbientProbeStates(room, out AmbientProbeRecord ambientProbeRecord);

            string basisPath = roomFolder + "/" + room.Spec.RoomId + "_RoomBasis.asset";
            if (AssetDatabase.LoadMainAssetAtPath(basisPath) != null)
                throw new InvalidOperationException("Staging basis target unexpectedly exists: '" + basisPath + "'.");
            var basis = ScriptableObject.CreateInstance<DungeonPortalBakedRoomBasisData>();
            basis.name = room.Spec.RoomId + "_BakedBasis";
            ConfigureBasisData(
                basis,
                room,
                buckets,
                p100Atlases,
                baselineAtlases,
                fullAtlases,
                doorwayProbe,
                ambientStates);
            if (!basis.TryValidateDefinition(out string basisFailure))
            {
                UnityEngine.Object.DestroyImmediate(basis);
                throw new InvalidOperationException(
                    "Generated room-basis definition did not satisfy the runtime contract for '" + room.Spec.RoomId +
                    "': " + basisFailure);
            }
            AssetDatabase.CreateAsset(basis, basisPath);
            EditorUtility.SetDirty(basis);
            AssetDatabase.SaveAssetIfDirty(basis);
            AddArtifactFingerprint(artifacts, stagingRoot, basisPath, "RoomBasisData", null);

            var atlasRecords = new AtlasRecord[buckets.Length];
            for (int i = 0; i < buckets.Length; i++)
            {
                BucketWork bucket = buckets[i];
                atlasRecords[i] = new AtlasRecord
                {
                    canonicalBucketIndex = bucket.BucketIndex,
                    canonicalLocalLightmapIndex = bucket.LocalLightmapIndex,
                    p0ColorSourceAssetPath = AssetDatabase.GetAssetPath(bucket.P0Color),
                    p0DirectionSourceAssetPath = AssetDatabase.GetAssetPath(bucket.P0Direction),
                    p100ColorCanonicalRelativePath = p100Atlases.ColorRelativePaths[i],
                    p100DirectionCanonicalRelativePath = p100Atlases.DirectionRelativePaths[i],
                    responseBaselineColorRelativePath = baselineAtlases.ColorRelativePaths[i],
                    responseBaselineDirectionRelativePath = baselineAtlases.DirectionRelativePaths[i],
                    responseFullColorRelativePath = fullAtlases.ColorRelativePaths[i],
                    responseFullDirectionRelativePath = fullAtlases.DirectionRelativePaths[i],
                    width = bucket.Width,
                    height = bucket.Height,
                    p0ColorMipmapCount = bucket.P0Color.mipmapCount,
                    p0DirectionMipmapCount = bucket.P0Direction.mipmapCount,
                    p100ColorCanonicalMipmapCount = p100Atlases.Colors[i].mipmapCount,
                    p100DirectionCanonicalMipmapCount = p100Atlases.Directions[i].mipmapCount,
                    responseBaselineColorMipmapCount = baselineAtlases.Colors[i].mipmapCount,
                    responseBaselineDirectionMipmapCount = baselineAtlases.Directions[i].mipmapCount,
                    responseFullColorMipmapCount = fullAtlases.Colors[i].mipmapCount,
                    responseFullDirectionMipmapCount = fullAtlases.Directions[i].mipmapCount,
                    p100OperationCount = p100Atlases.ColorRasterization[i].OperationCount,
                    p100OwnedCoreTexelCount = p100Atlases.ColorRasterization[i].OwnedCoreTexelCount,
                    p100ImplicitZeroCoreTexelCount = p100Atlases.ColorRasterization[i].ImplicitZeroCoreTexelCount,
                    p100OwnerCollisionCount = p100Atlases.ColorRasterization[i].OwnerCollisionCount,
                    responseBaselineOperationCount = baselineAtlases.ColorRasterization[i].OperationCount,
                    responseBaselineOwnedCoreTexelCount = baselineAtlases.ColorRasterization[i].OwnedCoreTexelCount,
                    responseBaselineImplicitZeroCoreTexelCount =
                        baselineAtlases.ColorRasterization[i].ImplicitZeroCoreTexelCount,
                    responseBaselineOwnerCollisionCount = baselineAtlases.ColorRasterization[i].OwnerCollisionCount,
                    responseFullOperationCount = fullAtlases.ColorRasterization[i].OperationCount,
                    responseFullOwnedCoreTexelCount = fullAtlases.ColorRasterization[i].OwnedCoreTexelCount,
                    responseFullImplicitZeroCoreTexelCount = fullAtlases.ColorRasterization[i].ImplicitZeroCoreTexelCount,
                    responseFullOwnerCollisionCount = fullAtlases.ColorRasterization[i].OwnerCollisionCount,
                    canonicalOrientationProofSampledTexelCount =
                        p100Atlases.ColorRasterization[i].CanonicalOrientationProofSampledTexelCount,
                    canonicalOrientationProofMaxAbsoluteChannelError =
                        p100Atlases.ColorRasterization[i].CanonicalOrientationProofMaxAbsoluteChannelError
                };
            }

            RendererMappingRecord[] mappingRecords = BuildMappingRecords(room);
            return new RoomRecord
            {
                roomId = room.Spec.RoomId,
                basisAssetRelativePath = MakeRelativeAssetPath(stagingRoot, basisPath),
                productionPrefabPath = room.Spec.ProductionPrefabPath,
                p0BakeDataPath = room.Spec.P0BakeDataPath,
                p100BakeDataPath = room.Spec.P100BakeDataPath,
                receiverCapturePath = room.Spec.ReceiverCapturePath,
                endpointProfilePath = room.Spec.EndpointProfilePath,
                doorwayId = StableDoorwayId,
                basisId = K1BasisId,
                canonicalRendererCount = room.CanonicalRenderers.Length,
                captureRendererCount = room.CanonicalRenderers.Count(item => item.BaselineCapture.HasValue),
                unmatchedProductionRendererCount = room.CanonicalRenderers.Count(item => !item.BaselineCapture.HasValue),
                receiverInjectorCookieIsNull = true,
                chartOwnershipPolicy =
                    "UV2_TRIANGLE_RASTER_RFLOAT_MINMAX_OWNER_GATE; genuinely absent TEXCOORD1 uses only one collision-gated ST.zw texel with constant source sampling; 2px deterministic 8-neighbour dilation only where owner==0; unmatched response triangles remain neutral owners.",
                canonicalRendererMappingSignature = ComputeCanonicalMappingSignature(room.CanonicalRenderers),
                receiverDoorwayProbe = doorwayProbeRecord,
                ambientProbe = ambientProbeRecord,
                atlases = atlasRecords,
                rendererMappings = mappingRecords
            };
        }

        private static BucketWork[] BuildBucketWork(ResolvedRoom room)
        {
            Texture2D[] p0Colors = room.P0.lightmapColors ?? Array.Empty<Texture2D>();
            Texture2D[] p0Directions = room.P0.lightmapDirections ?? Array.Empty<Texture2D>();
            Texture2D[] p100Colors = room.P100.lightmapColors ?? Array.Empty<Texture2D>();
            Texture2D[] p100Directions = room.P100.lightmapDirections ?? Array.Empty<Texture2D>();
            if (p0Colors.Length != p100Colors.Length || p0Directions.Length != p100Directions.Length)
            {
                throw new InvalidOperationException(
                    "P0/P100 source-atlas cardinality differs for '" + room.Spec.RoomId + "'.");
            }

            var buckets = new BucketWork[p0Colors.Length];
            for (int i = 0; i < buckets.Length; i++)
            {
                if (p0Colors[i].width != p0Directions[i].width || p0Colors[i].height != p0Directions[i].height)
                    throw new InvalidOperationException("P0 canonical color/direction dimensions differ at " + i + ".");
                if (p100Colors[i].width != p100Directions[i].width ||
                    p100Colors[i].height != p100Directions[i].height ||
                    p100Colors[i].mipmapCount != p100Directions[i].mipmapCount)
                {
                    throw new InvalidOperationException(
                        "P100 endpoint-native color/direction dimensions or mip counts differ at " + i + ".");
                }
                buckets[i] = new BucketWork(
                    i,
                    i,
                    p0Colors[i],
                    p0Directions[i],
                    p100Colors[i],
                    p100Directions[i]);
            }
            return buckets;
        }

        private static List<ChartCopyOperation> BuildP100CopyOperations(
            ResolvedRoom room,
            BucketWork[] buckets)
        {
            var result = new List<ChartCopyOperation>(room.CanonicalRenderers.Length);
            for (int i = 0; i < room.CanonicalRenderers.Length; i++)
            {
                CanonicalRendererWork renderer = room.CanonicalRenderers[i];
                int destinationBucket = renderer.P0.lightmapIndex;
                int sourceBucket = renderer.P100.lightmapIndex;
                AssertBucketIndex(buckets, destinationBucket, renderer.CanonicalKey + " P0 target");
                AssertBucketIndex(buckets, sourceBucket, renderer.CanonicalKey + " P100 source");
                result.Add(new ChartCopyOperation(
                    destinationBucket,
                    renderer.P0.lightmapScaleOffset,
                    buckets[sourceBucket].P100Color,
                    buckets[sourceBucket].P100Direction,
                    renderer.P100.lightmapScaleOffset,
                    renderer.ProductionMesh,
                    renderer.LightmapUvMode,
                    renderer.CanonicalKey + " P100"));
            }
            return result;
        }

        private static List<ChartCopyOperation> BuildP0CopyOperations(
            ResolvedRoom room,
            BucketWork[] buckets)
        {
            var result = new List<ChartCopyOperation>(room.CanonicalRenderers.Length);
            for (int i = 0; i < room.CanonicalRenderers.Length; i++)
            {
                CanonicalRendererWork renderer = room.CanonicalRenderers[i];
                int destinationBucket = renderer.P100.lightmapIndex;
                int sourceBucket = renderer.P0.lightmapIndex;
                AssertBucketIndex(buckets, destinationBucket, renderer.CanonicalKey + " P100 target");
                AssertBucketIndex(buckets, sourceBucket, renderer.CanonicalKey + " P0 source");
                result.Add(new ChartCopyOperation(
                    destinationBucket,
                    renderer.P100.lightmapScaleOffset,
                    buckets[sourceBucket].P0Color,
                    buckets[sourceBucket].P0Direction,
                    renderer.P0.lightmapScaleOffset,
                    renderer.ProductionMesh,
                    renderer.LightmapUvMode,
                    renderer.CanonicalKey + " P0 in P100 layout"));
            }
            return result;
        }

        private static List<ChartCopyOperation> BuildResponseCopyOperations(
            ResolvedRoom room,
            BucketWork[] buckets,
            bool full)
        {
            return BuildResponseCopyOperations(room, buckets, full, EndpointLayout.Power0);
        }

        private static List<ChartCopyOperation> BuildResponseCopyOperations(
            ResolvedRoom room,
            BucketWork[] buckets,
            bool full,
            EndpointLayout destinationLayout)
        {
            DungeonPortalReceiverResponseCapture.CaptureState state = full ? room.Full : room.Baseline;
            Dictionary<int, DungeonPortalReceiverResponseCapture.CaptureLightmap> lightmaps =
                BuildCaptureLightmapLookup(state, room.Spec.RoomId + (full ? " Full" : " Baseline"));
            var result = new List<ChartCopyOperation>(room.CanonicalRenderers.Length);
            for (int i = 0; i < room.CanonicalRenderers.Length; i++)
            {
                CanonicalRendererWork renderer = room.CanonicalRenderers[i];
                DungeonPortalReceiverResponseCapture.CaptureRenderer? nullableCapture =
                    full ? renderer.FullCapture : renderer.BaselineCapture;
                DungeonTileBakeData.RendererBakeEntry destination =
                    destinationLayout == EndpointLayout.Power100 ? renderer.P100 : renderer.P0;
                int destinationBucket = destination.lightmapIndex;
                string endpointLabel = destinationLayout == EndpointLayout.Power100 ? "P100" : "P0";
                AssertBucketIndex(
                    buckets,
                    destinationBucket,
                    renderer.CanonicalKey + " " + endpointLabel + " response target");
                if (!nullableCapture.HasValue)
                {
                    // Every production mesh reserves its exact UV2 triangle coverage in
                    // both response states. The clear color/direction is deliberately
                    // retained here so no mapped chart or dilation can leak into an
                    // unmatched production receiver surface.
                    result.Add(new ChartCopyOperation(
                        destinationBucket,
                        destination.lightmapScaleOffset,
                        null,
                        null,
                        Vector4.zero,
                        renderer.ProductionMesh,
                        renderer.LightmapUvMode,
                        renderer.CanonicalKey + " " + endpointLabel + " " +
                        (full ? "Full" : "Baseline") + " neutral unmatched"));
                    continue;
                }
                DungeonPortalReceiverResponseCapture.CaptureRenderer capture = nullableCapture.Value;
                if (!lightmaps.TryGetValue(capture.lightmapIndex, out DungeonPortalReceiverResponseCapture.CaptureLightmap source))
                {
                    throw new InvalidOperationException(
                        "Receiver capture renderer refers to absent source atlas " + capture.lightmapIndex +
                        " for '" + room.Spec.RoomId + "'.");
                }
                ValidateSampleTexture(source.colorTexture, room.Spec.RoomId + " receiver response color", false);
                ValidateSampleTexture(source.directionTexture, room.Spec.RoomId + " receiver response direction", true);
                result.Add(new ChartCopyOperation(
                    destinationBucket,
                    destination.lightmapScaleOffset,
                    source.colorTexture,
                    source.directionTexture,
                    capture.lightmapScaleOffset,
                    renderer.ProductionMesh,
                    renderer.LightmapUvMode,
                    renderer.CanonicalKey + " " + endpointLabel + (full ? " Full" : " Baseline")));
            }
            return result;
        }

        /// <summary>
        /// Converts one immutable angle-capture state into destination-native chart-copy
        /// operations.  This deliberately does not use legacy capture bucket order: every
        /// source renderer is matched exactly by its sibling-indexed relativePath and
        /// componentOrdinal before its mesh fingerprint/ST is accepted.
        /// </summary>
        private static List<ChartCopyOperation> BuildAngleResponseCopyOperations(
            ResolvedRoom room,
            BucketWork[] buckets,
            DungeonPortalBakedBasisDoorAngleCapture.AngleState state,
            EndpointLayout destinationLayout,
            string label)
        {
            var sourceByIdentity = new Dictionary<RendererStableIdentity,
                DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            DungeonPortalReceiverResponseCapture.CaptureRenderer[] captured = state.renderers ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            for (int i = 0; i < captured.Length; i++)
            {
                var identity = new RendererStableIdentity(
                    captured[i].relativePath,
                    captured[i].componentOrdinal);
                if (!sourceByIdentity.TryAdd(identity, captured[i]))
                {
                    throw new InvalidOperationException(
                        label + " has a duplicate angle-capture renderer identity " + identity + ".");
                }
            }
            Dictionary<int, DungeonPortalReceiverResponseCapture.CaptureLightmap> maps =
                BuildCaptureLightmapLookup(
                    new DungeonPortalReceiverResponseCapture.CaptureState
                    {
                        lightmaps = state.lightmaps
                    },
                    label);
            var result = new List<ChartCopyOperation>(room.CanonicalRenderers.Length);
            var consumed = new HashSet<RendererStableIdentity>();
            for (int i = 0; i < room.CanonicalRenderers.Length; i++)
            {
                CanonicalRendererWork renderer = room.CanonicalRenderers[i];
                DungeonTileBakeData.RendererBakeEntry destination = destinationLayout == EndpointLayout.Power100
                    ? renderer.P100
                    : renderer.P0;
                AssertBucketIndex(buckets, destination.lightmapIndex, label + " destination");
                var identity = new RendererStableIdentity(
                    renderer.ProductionStablePath,
                    renderer.ProductionComponentOrdinal);
                if (!sourceByIdentity.TryGetValue(identity, out DungeonPortalReceiverResponseCapture.CaptureRenderer source))
                {
                    result.Add(new ChartCopyOperation(
                        destination.lightmapIndex,
                        destination.lightmapScaleOffset,
                        null,
                        null,
                        Vector4.zero,
                        renderer.ProductionMesh,
                        renderer.LightmapUvMode,
                        label + " neutral unmatched " + renderer.CanonicalKey));
                    continue;
                }
                if (!consumed.Add(identity))
                    throw new InvalidOperationException(label + " consumes an angle renderer twice: " + identity + ".");
                ValidateCaptureMeshIdentity(renderer, source, room.Spec.RoomId);
                if (!maps.TryGetValue(source.lightmapIndex, out DungeonPortalReceiverResponseCapture.CaptureLightmap map))
                {
                    throw new InvalidOperationException(
                        label + " renderer " + identity + " refers to a missing source lightmap " +
                        source.lightmapIndex + ".");
                }
                ValidateSampleTexture(map.colorTexture, label + " color", false);
                ValidateSampleTexture(map.directionTexture, label + " direction", true);
                result.Add(new ChartCopyOperation(
                    destination.lightmapIndex,
                    destination.lightmapScaleOffset,
                    map.colorTexture,
                    map.directionTexture,
                    source.lightmapScaleOffset,
                    renderer.ProductionMesh,
                    renderer.LightmapUvMode,
                    label + " " + renderer.CanonicalKey));
            }
            if (consumed.Count != sourceByIdentity.Count)
            {
                throw new InvalidOperationException(
                    label + " did not consume every exact angle-capture renderer binding.");
            }
            return result;
        }

        private static Dictionary<int, DungeonPortalReceiverResponseCapture.CaptureLightmap> BuildCaptureLightmapLookup(
            DungeonPortalReceiverResponseCapture.CaptureState state,
            string label)
        {
            DungeonPortalReceiverResponseCapture.CaptureLightmap[] source = state.lightmaps ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            var result = new Dictionary<int, DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i].sourceLightmapIndex < 0 || !result.TryAdd(source[i].sourceLightmapIndex, source[i]))
                {
                    throw new InvalidOperationException(label + " has an invalid or duplicate source lightmap index.");
                }
            }
            return result;
        }

        private static void AssertBucketIndex(BucketWork[] buckets, int index, string label)
        {
            if (index < 0 || index >= buckets.Length)
                throw new InvalidOperationException("Lightmap bucket index is out of range for " + label + ": " + index + ".");
        }

        private static void CreateEndpointNativeBasisAsset(
            ResolvedRoom room,
            EndpointNativeRoomRecord record,
            string responsePayloadRoot,
            string basisAssetPath,
            string fingerprintRoot,
            List<ArtifactFingerprint> artifacts)
        {
            if (room == null || record == null ||
                !string.Equals(room.Spec.RoomId, record.roomId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Endpoint-native basis room/manifest identity differs.");
            }
            if (AssetDatabase.LoadMainAssetAtPath(basisAssetPath) != null)
            {
                throw new InvalidOperationException(
                    "Refusing to overwrite an endpoint-native staged room-basis asset: '" +
                    basisAssetPath + "'.");
            }

            DungeonPortalBakedRoomBasisData.OptionalAmbientProbeStates ambientStates =
                BuildAmbientProbeStates(room, out _);
            var basis = ScriptableObject.CreateInstance<DungeonPortalBakedRoomBasisData>();
            bool persisted = false;
            try
            {
                basis.name = room.Spec.RoomId + "_EndpointNativeBakedBasis";
                ConfigureEndpointNativeBasisData(
                    basis,
                    room,
                    record,
                    responsePayloadRoot,
                    ambientStates);
                if (!basis.TryValidateDefinition(out string basisFailure))
                {
                    throw new InvalidOperationException(
                        "Endpoint-native DPBB-2 definition did not satisfy its runtime contract for '" +
                        room.Spec.RoomId + "': " + basisFailure);
                }
                VerifyEndpointNativeAngleBasisDefinition(room, record, responsePayloadRoot, basis);

                AssetDatabase.CreateAsset(basis, basisAssetPath);
                persisted = true;
                EditorUtility.SetDirty(basis);
                AssetDatabase.SaveAssetIfDirty(basis);
                AssetDatabase.ImportAsset(basisAssetPath, ImportAssetOptions.ForceSynchronousImport);
                DungeonPortalBakedRoomBasisData persistedBasis =
                    LoadRequired<DungeonPortalBakedRoomBasisData>(basisAssetPath);
                VerifyEndpointNativeAngleBasisDefinition(room, record, responsePayloadRoot, persistedBasis);
                AddArtifactFingerprint(
                    artifacts,
                    fingerprintRoot,
                    basisAssetPath,
                    "EndpointNativeDPBB2RoomBasisData",
                    persistedBasis);
            }
            finally
            {
                if (!persisted && basis != null)
                    UnityEngine.Object.DestroyImmediate(basis);
            }
        }

        private static void ConfigureEndpointNativeBasisData(
            DungeonPortalBakedRoomBasisData basis,
            ResolvedRoom room,
            EndpointNativeRoomRecord record,
            string responsePayloadRoot,
            DungeonPortalBakedRoomBasisData.OptionalAmbientProbeStates ambientStates)
        {
            BucketWork[] buckets = BuildBucketWork(room);
            EndpointNativeEndpointRecord p0Record = GetEndpointNativeEndpointRecord(
                record,
                EndpointLayout.Power0);
            EndpointNativeEndpointRecord p100Record = GetEndpointNativeEndpointRecord(
                record,
                EndpointLayout.Power100);
            EndpointNativePoseRecord p0FirstPose = GetEndpointNativePoseRecord(p0Record, 0);
            EndpointNativePoseRecord p100FirstPose = GetEndpointNativePoseRecord(p100Record, 0);
            if (p0FirstPose.atlases == null || p100FirstPose.atlases == null ||
                p0FirstPose.atlases.Length != buckets.Length || p100FirstPose.atlases.Length != buckets.Length)
            {
                throw new InvalidOperationException(
                    "Endpoint-native continuous base transition currently requires equal " +
                    $"P0/P100 atlas cardinality for '{room.Spec.RoomId}'.");
            }

            var canonicalBuckets =
                new DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket[buckets.Length];
            var originalP100 =
                new DungeonPortalBakedRoomBasisData.Power100SourceAtlas[buckets.Length];
            for (int i = 0; i < buckets.Length; i++)
            {
                BucketWork bucket = buckets[i];
                EndpointNativeAtlasRecord p0Atlas = p0FirstPose.atlases[i];
                EndpointNativeAtlasRecord p100Atlas = p100FirstPose.atlases[i];
                if (p0Atlas == null || p100Atlas == null ||
                    p0Atlas.localLightmapIndex != i || p100Atlas.localLightmapIndex != i)
                {
                    throw new InvalidOperationException(
                        "Endpoint-native base-transition records are missing or unordered for '" +
                        room.Spec.RoomId + "' bucket " + i + ".");
                }
                Texture2D p100InP0Color = LoadRequired<Texture2D>(
                    CombinePayloadPath(responsePayloadRoot, p0Atlas.oppositeBaseColorRelativePath));
                Texture2D p100InP0Direction = LoadRequired<Texture2D>(
                    CombinePayloadPath(responsePayloadRoot, p0Atlas.oppositeBaseDirectionRelativePath));
                Texture2D p0InP100Color = LoadRequired<Texture2D>(
                    CombinePayloadPath(responsePayloadRoot, p100Atlas.oppositeBaseColorRelativePath));
                Texture2D p0InP100Direction = LoadRequired<Texture2D>(
                    CombinePayloadPath(responsePayloadRoot, p100Atlas.oppositeBaseDirectionRelativePath));
                canonicalBuckets[i] = new DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket();
                canonicalBuckets[i].ConfigureAuthoring(
                    "P0_LM" + i.ToString("00", CultureInfo.InvariantCulture),
                    i,
                    bucket.P0Color,
                    p100InP0Color,
                    bucket.P0Direction,
                    p100InP0Direction);
                originalP100[i] = new DungeonPortalBakedRoomBasisData.Power100SourceAtlas();
                originalP100[i].ConfigureTransitionAuthoring(
                    i,
                    bucket.P100Color,
                    bucket.P100Direction,
                    p0InP100Color,
                    p0InP100Direction);
            }

            var canonicalEntries = new DungeonPortalBakedRoomBasisData.CanonicalRendererEntry[
                room.CanonicalRenderers.Length];
            for (int i = 0; i < canonicalEntries.Length; i++)
            {
                CanonicalRendererWork source = room.CanonicalRenderers[i];
                canonicalEntries[i] = new DungeonPortalBakedRoomBasisData.CanonicalRendererEntry();
                canonicalEntries[i].ConfigureAuthoring(
                    source.CanonicalKey,
                    source.P0.lightmapIndex,
                    source.P0.lightmapIndex,
                    source.P0.lightmapScaleOffset,
                    source.P100.lightmapIndex,
                    source.P100.lightmapScaleOffset);
            }

            var poseResponses = new DungeonPortalBakedRoomBasisData.ReceiverDoorPoseResponse[
                RequiredAnglePoseCount];
            for (int poseIndex = 0; poseIndex < RequiredAnglePoseCount; poseIndex++)
            {
                EndpointNativePoseRecord p0Pose = GetEndpointNativePoseRecord(p0Record, poseIndex);
                EndpointNativePoseRecord p100Pose = GetEndpointNativePoseRecord(p100Record, poseIndex);
                DungeonPortalBakedRoomBasisData.ResponseAtlas[] p0Responses =
                    BuildEndpointNativeCompactResponseArray(
                        responsePayloadRoot, p0Pose, buckets.Length, "P0 " + p0Pose.poseId);
                DungeonPortalBakedRoomBasisData.ResponseAtlas[] p100Responses =
                    BuildEndpointNativeCompactResponseArray(
                        responsePayloadRoot, p100Pose, buckets.Length, "P100 " + p100Pose.poseId);
                var lobe = new DungeonPortalBakedRoomBasisData.ReceiverResponseLobe();
                lobe.ConfigureEndpointAuthoring(
                    K1BasisId,
                    p0Responses,
                    p100Responses,
                    BuildAnglePoseDoorwayProbeResponse(room, p0Pose, p100Pose));
                poseResponses[poseIndex] = new DungeonPortalBakedRoomBasisData.ReceiverDoorPoseResponse();
                poseResponses[poseIndex].ConfigureAuthoring(
                    p0Pose.openFraction,
                    new[] { lobe });
            }
            var receiverDoor = new DungeonPortalBakedRoomBasisData.ReceiverDoorBasis();
            receiverDoor.ConfigurePoseAuthoring(StableDoorwayId, poseResponses);

            DungeonPortalEndpointProfile.PortalDirectLightDescriptor descriptor =
                GetSingleDirectDescriptor(room.Endpoint, room.Spec.RoomId);
            var coefficient = new DungeonPortalBakedRoomBasisData.SourceBasisCoefficient();
            coefficient.ConfigureAuthoring(
                K1BasisId,
                MultiplyRadiance(descriptor.power0Color, descriptor.power0Intensity),
                MultiplyRadiance(descriptor.power100Color, descriptor.power100Intensity));
            var sourceDoor = new DungeonPortalBakedRoomBasisData.SourceDoorBasis();
            sourceDoor.ConfigureAuthoring(StableDoorwayId, new[] { coefficient });

            basis.ConfigureAuthoring(
                room.Spec.RoomId,
                canonicalBuckets,
                canonicalEntries,
                originalP100,
                new[] { receiverDoor },
                new[] { sourceDoor },
                ambientStates,
                Array.Empty<DungeonPortalBakedRoomBasisData.OptionalReflectionStates>());
            if (!basis.TryValidateBaseTransitionLayouts(out string transitionFailure))
            {
                throw new InvalidOperationException(
                    "Endpoint-native continuous base-transition contract failed for '" +
                    room.Spec.RoomId + "': " + transitionFailure);
            }
        }

        private static DungeonPortalBakedRoomBasisData.ResponseAtlas[] BuildEndpointNativeResponseArray(
            string payloadRoot,
            EndpointNativeEndpointRecord endpoint,
            int expectedCount,
            string endpointLabel)
        {
            EndpointNativeAtlasRecord[] atlases = endpoint.atlases ??
                Array.Empty<EndpointNativeAtlasRecord>();
            if (atlases.Length != expectedCount)
            {
                throw new InvalidOperationException(
                    endpointLabel + " endpoint-native response count is " + atlases.Length +
                    "; expected " + expectedCount + ".");
            }

            var result = new DungeonPortalBakedRoomBasisData.ResponseAtlas[expectedCount];
            for (int i = 0; i < atlases.Length; i++)
            {
                EndpointNativeAtlasRecord atlas = atlases[i];
                if (atlas == null || atlas.localLightmapIndex < 0 ||
                    atlas.localLightmapIndex >= expectedCount || result[atlas.localLightmapIndex] != null)
                {
                    throw new InvalidOperationException(
                        endpointLabel + " endpoint-native response has an invalid/duplicate bucket at " + i + ".");
                }
                Texture2D colorDelta = LoadRequired<Texture2D>(
                    CombinePayloadPath(payloadRoot, atlas.colorDeltaRelativePath));
                Texture2D momentDelta = LoadRequired<Texture2D>(
                    CombinePayloadPath(payloadRoot, atlas.directionalMomentDeltaRelativePath));
                var response = new DungeonPortalBakedRoomBasisData.ResponseAtlas();
                response.ConfigurePrecomputedDeltaAuthoring(
                    atlas.localLightmapIndex,
                    colorDelta,
                    momentDelta);
                result[atlas.localLightmapIndex] = response;
            }
            return result;
        }

        private static DungeonPortalBakedRoomBasisData.ResponseAtlas[]
            BuildEndpointNativeCompactResponseArray(
                string payloadRoot,
                EndpointNativePoseRecord pose,
                int expectedCount,
                string label)
        {
            EndpointNativeAtlasRecord[] atlases = pose != null ? pose.atlases : null;
            atlases = atlases ?? Array.Empty<EndpointNativeAtlasRecord>();
            if (atlases.Length != expectedCount)
            {
                throw new InvalidOperationException(
                    label + " compact response count is " + atlases.Length + "; expected " + expectedCount + ".");
            }
            var result = new DungeonPortalBakedRoomBasisData.ResponseAtlas[expectedCount];
            for (int i = 0; i < atlases.Length; i++)
            {
                EndpointNativeAtlasRecord atlas = atlases[i];
                if (atlas == null || atlas.localLightmapIndex < 0 ||
                    atlas.localLightmapIndex >= expectedCount || result[atlas.localLightmapIndex] != null)
                {
                    throw new InvalidOperationException(
                        label + " compact response has an invalid/duplicate bucket at " + i + ".");
                }
                Texture2D colorPositive = LoadRequired<Texture2D>(
                    CombinePayloadPath(payloadRoot, atlas.colorPositiveRelativePath));
                Texture2D colorNegative = LoadRequired<Texture2D>(
                    CombinePayloadPath(payloadRoot, atlas.colorNegativeRelativePath));
                Texture2D momentPositive = LoadRequired<Texture2D>(
                    CombinePayloadPath(payloadRoot, atlas.directionalMomentPositiveRelativePath));
                Texture2D momentNegative = LoadRequired<Texture2D>(
                    CombinePayloadPath(payloadRoot, atlas.directionalMomentNegativeRelativePath));
                var response = new DungeonPortalBakedRoomBasisData.ResponseAtlas();
                response.ConfigureNormalizedPositiveNegativeDeltaAuthoring(
                    atlas.localLightmapIndex,
                    colorPositive,
                    colorNegative,
                    CloneVector4Array(atlas.colorPositiveScales),
                    CloneVector4Array(atlas.colorNegativeScales),
                    momentPositive,
                    momentNegative,
                    CloneVector4Array(atlas.directionalMomentPositiveScales),
                    CloneVector4Array(atlas.directionalMomentNegativeScales));
                result[atlas.localLightmapIndex] = response;
            }
            return result;
        }

        private static EndpointNativePoseRecord GetEndpointNativePoseRecord(
            EndpointNativeEndpointRecord endpoint,
            int poseIndex)
        {
            EndpointNativePoseRecord[] poses = endpoint != null ? endpoint.poses : null;
            poses = poses ?? Array.Empty<EndpointNativePoseRecord>();
            if (poseIndex < 0 || poseIndex >= RequiredAnglePoseCount ||
                poses.Length != RequiredAnglePoseCount || poses[poseIndex] == null ||
                !string.Equals(poses[poseIndex].poseId, PoseIdForIndex(poseIndex), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Endpoint-native compact pose set is missing deterministic " +
                                                    PoseIdForIndex(poseIndex) + ".");
            }
            return poses[poseIndex];
        }

        private static DungeonPortalBakedRoomBasisData.OptionalDoorwayProbeResponse
            BuildAnglePoseDoorwayProbeResponse(
                ResolvedRoom room,
                EndpointNativePoseRecord p0Pose,
                EndpointNativePoseRecord p100Pose)
        {
            if (room == null || room.AngleCapture == null || p0Pose == null || p100Pose == null ||
                !string.Equals(p0Pose.poseId, p100Pose.poseId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Angle-pose doorway SH inputs are incomplete.");
            }
            int poseIndex = PoseIndexForId(p0Pose.poseId);
            DungeonPortalBakedBasisDoorAngleCapture.AngleState baseline =
                room.AngleCapture.MatchedBaselineAngleStates[poseIndex];
            DungeonPortalBakedBasisDoorAngleCapture.AngleState full =
                room.AngleCapture.FullAngleStates[poseIndex];
            float[] off = ExtractFirstNineProbeSh(baseline);
            float[] on = ExtractFirstNineProbeSh(full);
            if (!string.Equals(p0Pose.firstNineBaselineShSignature, ComputeFloatArrayHash(off), StringComparison.Ordinal) ||
                !string.Equals(p0Pose.firstNineFullShSignature, ComputeFloatArrayHash(on), StringComparison.Ordinal) ||
                !string.Equals(p100Pose.firstNineBaselineShSignature, ComputeFloatArrayHash(off), StringComparison.Ordinal) ||
                !string.Equals(p100Pose.firstNineFullShSignature, ComputeFloatArrayHash(on), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Angle-pose first-nine doorway SH provenance drifted for '" +
                                                    room.Spec.RoomId + "' " + p0Pose.poseId + ".");
            }
            var response = new DungeonPortalBakedRoomBasisData.OptionalDoorwayProbeResponse();
            response.ConfigureAuthoring(off, on);
            return response;
        }

        private static string PoseIdForIndex(int poseIndex)
        {
            switch (poseIndex)
            {
                case 0: return "D025";
                case 1: return "D050";
                case 2: return "D075";
                case 3: return "D100";
                default: throw new ArgumentOutOfRangeException(nameof(poseIndex));
            }
        }

        private static int PoseIndexForId(string poseId)
        {
            for (int i = 0; i < RequiredAnglePoseCount; i++)
            {
                if (string.Equals(PoseIdForIndex(i), poseId, StringComparison.Ordinal))
                    return i;
            }
            throw new InvalidOperationException("Unknown deterministic angle-pose id '" + poseId + "'.");
        }

        private static Vector4[] CloneVector4Array(Vector4[] source)
        {
            return source != null ? (Vector4[])source.Clone() : Array.Empty<Vector4>();
        }

        private static float[] ExtractFirstNineProbeSh(
            DungeonPortalBakedBasisDoorAngleCapture.AngleState state)
        {
            DungeonPortalReceiverResponseCapture.ProbeSample[] probes = state.probes ??
                Array.Empty<DungeonPortalReceiverResponseCapture.ProbeSample>();
            if (probes.Length < 9)
                throw new InvalidOperationException("Angle capture has fewer than the required first nine probes.");
            var result = new float[DungeonPortalBakedRoomBasisData.SphericalHarmonicsCoefficientCount];
            for (int probeIndex = 0; probeIndex < 9; probeIndex++)
            {
                if (probes[probeIndex].probeIndex != probeIndex)
                {
                    throw new InvalidOperationException(
                        "Angle capture first-nine probe indices are not deterministic at index " + probeIndex + ".");
                }
                for (int coefficient = 0; coefficient < 9; coefficient++)
                {
                    Vector3 value = probes[probeIndex].GetCoefficient(coefficient);
                    result[coefficient] += value.x;
                    result[9 + coefficient] += value.y;
                    result[18 + coefficient] += value.z;
                }
            }
            for (int i = 0; i < result.Length; i++)
            {
                result[i] /= 9f;
                if (!IsFinite(result[i]))
                    throw new InvalidOperationException("Angle capture first-nine doorway SH contains a non-finite value.");
            }
            return result;
        }

        private static string ComputeFirstNineProbeShSignature(
            DungeonPortalBakedBasisDoorAngleCapture.AngleState state)
        {
            return ComputeFloatArrayHash(ExtractFirstNineProbeSh(state));
        }

        private static EndpointNativeEndpointRecord GetEndpointNativeEndpointRecord(
            EndpointNativeRoomRecord room,
            EndpointLayout layout)
        {
            string endpointId = layout == EndpointLayout.Power100 ? "P100" : "P0";
            EndpointNativeEndpointRecord[] endpoints = room.endpoints ??
                Array.Empty<EndpointNativeEndpointRecord>();
            EndpointNativeEndpointRecord result = null;
            for (int i = 0; i < endpoints.Length; i++)
            {
                EndpointNativeEndpointRecord candidate = endpoints[i];
                if (candidate == null ||
                    !string.Equals(candidate.endpointId, endpointId, StringComparison.Ordinal))
                {
                    continue;
                }
                if (result != null)
                    throw new InvalidOperationException("Duplicate endpoint-native " + endpointId + " record.");
                result = candidate;
            }
            if (result == null)
                throw new InvalidOperationException("Missing endpoint-native " + endpointId + " record.");
            return result;
        }

        private static void VerifyEndpointNativeAngleBasisDefinition(
            ResolvedRoom room,
            EndpointNativeRoomRecord record,
            string responsePayloadRoot,
            DungeonPortalBakedRoomBasisData basis)
        {
            if (room == null || record == null || basis == null || room.AngleCapture == null)
            {
                throw new InvalidOperationException("Endpoint-native compact basis definition has a missing input.");
            }
            if (!basis.TryValidateDefinition(out string definitionFailure))
            {
                throw new InvalidOperationException("Endpoint-native compact basis definition is invalid: " + definitionFailure);
            }
            BucketWork[] buckets = BuildBucketWork(room);
            EndpointNativeEndpointRecord p0 = GetEndpointNativeEndpointRecord(record, EndpointLayout.Power0);
            EndpointNativeEndpointRecord p100 = GetEndpointNativeEndpointRecord(record, EndpointLayout.Power100);
            if (basis.CanonicalAtlases.Length != buckets.Length ||
                basis.Power100SourceAtlases.Length != buckets.Length ||
                basis.CanonicalRenderers.Length != room.CanonicalRenderers.Length)
            {
                throw new InvalidOperationException("Endpoint-native compact basis cardinality drifted for '" + room.Spec.RoomId + "'.");
            }
            for (int bucket = 0; bucket < buckets.Length; bucket++)
            {
                EndpointNativeAtlasRecord p0Atlas = GetEndpointNativePoseRecord(p0, 0).atlases[bucket];
                EndpointNativeAtlasRecord p100Atlas = GetEndpointNativePoseRecord(p100, 0).atlases[bucket];
                DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket actualP0 = basis.CanonicalAtlases[bucket];
                DungeonPortalBakedRoomBasisData.Power100SourceAtlas actualP100 = basis.Power100SourceAtlases[bucket];
                if (actualP0 == null || actualP100 == null ||
                    actualP0.Power0Color != buckets[bucket].P0Color ||
                    actualP0.Power0Direction != buckets[bucket].P0Direction ||
                    actualP0.Power100Color != LoadRequired<Texture2D>(CombinePayloadPath(responsePayloadRoot,
                        p0Atlas.oppositeBaseColorRelativePath)) ||
                    actualP0.Power100Direction != LoadRequired<Texture2D>(CombinePayloadPath(responsePayloadRoot,
                        p0Atlas.oppositeBaseDirectionRelativePath)) ||
                    actualP100.Color != buckets[bucket].P100Color ||
                    actualP100.Direction != buckets[bucket].P100Direction ||
                    actualP100.Power0ColorInPower100Layout != LoadRequired<Texture2D>(CombinePayloadPath(responsePayloadRoot,
                        p100Atlas.oppositeBaseColorRelativePath)) ||
                    actualP100.Power0DirectionInPower100Layout != LoadRequired<Texture2D>(CombinePayloadPath(responsePayloadRoot,
                        p100Atlas.oppositeBaseDirectionRelativePath)))
                {
                    throw new InvalidOperationException("Endpoint-native compact base transition binding drifted for '" +
                                                        room.Spec.RoomId + "' bucket " + bucket + ".");
                }
            }
            DungeonPortalBakedRoomBasisData.ReceiverDoorBasis[] doors = basis.ReceiverDoors;
            if (doors.Length != 1 || doors[0] == null || !doors[0].UsesPoseResponses ||
                doors[0].ResponseLobes.Length != 0 || doors[0].PoseResponses.Length != RequiredAnglePoseCount ||
                !string.Equals(doors[0].ReceiverDoorId, StableDoorwayId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Endpoint-native compact basis must contain exactly four pose responses and no legacy lobe.");
            }
            for (int pose = 0; pose < RequiredAnglePoseCount; pose++)
            {
                DungeonPortalBakedRoomBasisData.ReceiverDoorPoseResponse actualPose = doors[0].PoseResponses[pose];
                EndpointNativePoseRecord p0Pose = GetEndpointNativePoseRecord(p0, pose);
                EndpointNativePoseRecord p100Pose = GetEndpointNativePoseRecord(p100, pose);
                if (actualPose == null || !Mathf.Approximately(actualPose.OpenFraction, p0Pose.openFraction) ||
                    actualPose.ResponseLobes.Length != 1 || actualPose.ResponseLobes[0] == null ||
                    !string.Equals(actualPose.ResponseLobes[0].BasisId, K1BasisId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Endpoint-native compact pose binding drifted at " + PoseIdForIndex(pose) + ".");
                }
                VerifyEndpointNativeCompactBasisResponseSet(
                    responsePayloadRoot, p0Pose, actualPose.ResponseLobes[0].Power0AtlasResponses,
                    "P0 " + p0Pose.poseId);
                VerifyEndpointNativeCompactBasisResponseSet(
                    responsePayloadRoot, p100Pose, actualPose.ResponseLobes[0].Power100AtlasResponses,
                    "P100 " + p100Pose.poseId);
                DungeonPortalBakedRoomBasisData.OptionalDoorwayProbeResponse probe =
                    actualPose.ResponseLobes[0].DoorwayProbeResponse;
                if (probe == null || !string.Equals(ComputeFloatArrayHash(probe.OffCoefficients),
                        p0Pose.firstNineBaselineShSignature, StringComparison.Ordinal) ||
                    !string.Equals(ComputeFloatArrayHash(probe.OnCoefficients),
                        p0Pose.firstNineFullShSignature, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Endpoint-native compact pose doorway SH drifted at " + p0Pose.poseId + ".");
                }
            }
        }

        private static void VerifyEndpointNativeCompactBasisResponseSet(
            string payloadRoot,
            EndpointNativePoseRecord pose,
            DungeonPortalBakedRoomBasisData.ResponseAtlas[] responses,
            string label)
        {
            EndpointNativeAtlasRecord[] atlases = pose != null ? pose.atlases : null;
            atlases = atlases ?? Array.Empty<EndpointNativeAtlasRecord>();
            responses = responses ?? Array.Empty<DungeonPortalBakedRoomBasisData.ResponseAtlas>();
            if (responses.Length != atlases.Length)
                throw new InvalidOperationException(label + " compact basis response cardinality differs.");
            for (int i = 0; i < atlases.Length; i++)
            {
                EndpointNativeAtlasRecord atlas = atlases[i];
                DungeonPortalBakedRoomBasisData.ResponseAtlas response = responses[i];
                if (atlas == null || response == null || response.BucketIndex != i ||
                    response.Encoding != DungeonPortalBakedRoomBasisData.ResponseAtlasEncoding.NormalizedPositiveNegativeDelta ||
                    response.ColorDeltaPositive != LoadRequired<Texture2D>(CombinePayloadPath(payloadRoot, atlas.colorPositiveRelativePath)) ||
                    response.ColorDeltaNegative != LoadRequired<Texture2D>(CombinePayloadPath(payloadRoot, atlas.colorNegativeRelativePath)) ||
                    response.DirectionalMomentDeltaPositive != LoadRequired<Texture2D>(CombinePayloadPath(payloadRoot, atlas.directionalMomentPositiveRelativePath)) ||
                    response.DirectionalMomentDeltaNegative != LoadRequired<Texture2D>(CombinePayloadPath(payloadRoot, atlas.directionalMomentNegativeRelativePath)) ||
                    !Vector4ArrayEquals(response.ColorDeltaPositiveScales, atlas.colorPositiveScales) ||
                    !Vector4ArrayEquals(response.ColorDeltaNegativeScales, atlas.colorNegativeScales) ||
                    !Vector4ArrayEquals(response.DirectionalMomentDeltaPositiveScales, atlas.directionalMomentPositiveScales) ||
                    !Vector4ArrayEquals(response.DirectionalMomentDeltaNegativeScales, atlas.directionalMomentNegativeScales))
                {
                    throw new InvalidOperationException(label + " compact basis response binding failed at bucket " + i + ".");
                }
            }
        }

        private static bool Vector4ArrayEquals(Vector4[] left, Vector4[] right)
        {
            left = left ?? Array.Empty<Vector4>();
            right = right ?? Array.Empty<Vector4>();
            if (left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                if ((left[i] - right[i]).sqrMagnitude > 1e-12f)
                    return false;
            }
            return true;
        }

        private static void VerifyEndpointNativeBasisDefinition(
            ResolvedRoom room,
            EndpointNativeRoomRecord record,
            string responsePayloadRoot,
            DungeonPortalBakedRoomBasisData basis)
        {
            string failure = basis == null ? "basis asset is missing" : string.Empty;
            if (basis == null || !basis.TryValidateDefinition(out failure) ||
                !string.Equals(basis.Schema, DungeonPortalBakedRoomBasisData.CurrentSchema, StringComparison.Ordinal) ||
                !string.Equals(basis.RoomId, room.Spec.RoomId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Endpoint-native room-basis definition failed for '" + room.Spec.RoomId + "': " + failure);
            }
            if (!basis.TryValidateBaseTransitionLayouts(out string transitionFailure))
            {
                throw new InvalidOperationException(
                    "Endpoint-native room-basis continuous transition failed for '" +
                    room.Spec.RoomId + "': " + transitionFailure);
            }

            BucketWork[] buckets = BuildBucketWork(room);
            EndpointNativeEndpointRecord p0Record = GetEndpointNativeEndpointRecord(
                record,
                EndpointLayout.Power0);
            EndpointNativeEndpointRecord p100Record = GetEndpointNativeEndpointRecord(
                record,
                EndpointLayout.Power100);
            if (basis.CanonicalAtlases.Length != buckets.Length ||
                basis.Power100SourceAtlases.Length != buckets.Length ||
                basis.CanonicalRenderers.Length != room.CanonicalRenderers.Length)
            {
                throw new InvalidOperationException(
                    "Endpoint-native room-basis base/reference cardinality changed for '" +
                    room.Spec.RoomId + "'.");
            }
            for (int i = 0; i < buckets.Length; i++)
            {
                DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket p0 = basis.CanonicalAtlases[i];
                DungeonPortalBakedRoomBasisData.Power100SourceAtlas p100 = basis.Power100SourceAtlases[i];
                Texture2D expectedP100InP0Color = LoadRequired<Texture2D>(
                    CombinePayloadPath(
                        responsePayloadRoot,
                        p0Record.atlases[i].oppositeBaseColorRelativePath));
                Texture2D expectedP100InP0Direction = LoadRequired<Texture2D>(
                    CombinePayloadPath(
                        responsePayloadRoot,
                        p0Record.atlases[i].oppositeBaseDirectionRelativePath));
                Texture2D expectedP0InP100Color = LoadRequired<Texture2D>(
                    CombinePayloadPath(
                        responsePayloadRoot,
                        p100Record.atlases[i].oppositeBaseColorRelativePath));
                Texture2D expectedP0InP100Direction = LoadRequired<Texture2D>(
                    CombinePayloadPath(
                        responsePayloadRoot,
                        p100Record.atlases[i].oppositeBaseDirectionRelativePath));
                if (p0 == null || p100 == null || p0.CanonicalLocalLightmapIndex != i ||
                    p0.Power0Color != buckets[i].P0Color || p0.Power0Direction != buckets[i].P0Direction ||
                    p0.Power100Color != expectedP100InP0Color ||
                    p0.Power100Direction != expectedP100InP0Direction ||
                    p100.SourceLocalLightmapIndex != i || p100.Color != buckets[i].P100Color ||
                    p100.Direction != buckets[i].P100Direction ||
                    p100.Power0ColorInPower100Layout != expectedP0InP100Color ||
                    p100.Power0DirectionInPower100Layout != expectedP0InP100Direction ||
                    p0.UnchangedShadowMask != null || p100.ShadowMask != null)
                {
                    throw new InvalidOperationException(
                        "Endpoint-native exact base/dual-transition contract failed for '" + room.Spec.RoomId +
                        "' bucket " + i + ".");
                }
            }

            for (int i = 0; i < room.CanonicalRenderers.Length; i++)
            {
                CanonicalRendererWork expected = room.CanonicalRenderers[i];
                DungeonPortalBakedRoomBasisData.CanonicalRendererEntry actual =
                    basis.CanonicalRenderers[i];
                if (actual == null ||
                    !string.Equals(actual.CanonicalRendererKey, expected.CanonicalKey, StringComparison.Ordinal) ||
                    actual.BucketIndex != expected.P0.lightmapIndex ||
                    actual.CanonicalLocalLightmapIndex != expected.P0.lightmapIndex ||
                    actual.LightmapScaleOffset != expected.P0.lightmapScaleOffset ||
                    actual.Power100SourceLocalLightmapIndex != expected.P100.lightmapIndex ||
                    actual.Power100SourceScaleOffset != expected.P100.lightmapScaleOffset)
                {
                    throw new InvalidOperationException(
                        "Endpoint-native exact renderer binding failed at index " + i + " for '" +
                        room.Spec.RoomId + "'.");
                }
            }

            DungeonPortalBakedRoomBasisData.ReceiverDoorBasis[] receiverDoors = basis.ReceiverDoors;
            if (receiverDoors.Length != 1 || receiverDoors[0] == null ||
                receiverDoors[0].UsesPoseResponses || receiverDoors[0].ResponseLobes.Length != 1 ||
                !string.Equals(receiverDoors[0].ReceiverDoorId, StableDoorwayId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Endpoint-native pre-angle basis must preserve exactly one legacy D100 receiver response.");
            }
            DungeonPortalBakedRoomBasisData.ReceiverResponseLobe lobe =
                receiverDoors[0].ResponseLobes[0];
            if (lobe == null || !string.Equals(lobe.BasisId, K1BasisId, StringComparison.Ordinal))
                throw new InvalidOperationException("Endpoint-native K1 receiver lobe is missing.");
            VerifyEndpointNativeBasisResponseSet(
                responsePayloadRoot,
                GetEndpointNativeEndpointRecord(record, EndpointLayout.Power0),
                lobe.Power0AtlasResponses,
                "P0");
            VerifyEndpointNativeBasisResponseSet(
                responsePayloadRoot,
                GetEndpointNativeEndpointRecord(record, EndpointLayout.Power100),
                lobe.Power100AtlasResponses,
                "P100");

            DungeonPortalBakedRoomBasisData.SourceDoorBasis[] sourceDoors = basis.SourceDoors;
            if (sourceDoors.Length != 1 || sourceDoors[0] == null ||
                !string.Equals(sourceDoors[0].SourceDoorId, StableDoorwayId, StringComparison.Ordinal) ||
                sourceDoors[0].Coefficients.Length != 1 || sourceDoors[0].Coefficients[0] == null ||
                !string.Equals(sourceDoors[0].Coefficients[0].BasisId, K1BasisId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Endpoint-native K1 source-door coefficient is missing.");
            }
        }

        private static void VerifyEndpointNativeBasisResponseSet(
            string payloadRoot,
            EndpointNativeEndpointRecord record,
            DungeonPortalBakedRoomBasisData.ResponseAtlas[] responses,
            string endpointLabel)
        {
            EndpointNativeAtlasRecord[] atlases = record.atlases ??
                Array.Empty<EndpointNativeAtlasRecord>();
            responses = responses ?? Array.Empty<DungeonPortalBakedRoomBasisData.ResponseAtlas>();
            if (responses.Length != atlases.Length)
                throw new InvalidOperationException(endpointLabel + " DPBB-2 response cardinality differs.");
            for (int i = 0; i < atlases.Length; i++)
            {
                EndpointNativeAtlasRecord atlas = atlases[i];
                DungeonPortalBakedRoomBasisData.ResponseAtlas response = responses[i];
                Texture2D expectedColor = LoadRequired<Texture2D>(
                    CombinePayloadPath(payloadRoot, atlas.colorDeltaRelativePath));
                Texture2D expectedMoment = LoadRequired<Texture2D>(
                    CombinePayloadPath(payloadRoot, atlas.directionalMomentDeltaRelativePath));
                if (response == null || response.BucketIndex != i ||
                    response.Encoding != DungeonPortalBakedRoomBasisData.ResponseAtlasEncoding.PrecomputedDelta ||
                    response.ColorDelta != expectedColor ||
                    response.DirectionalMomentDelta != expectedMoment)
                {
                    throw new InvalidOperationException(
                        endpointLabel + " DPBB-2 response binding failed at bucket " + i + ".");
                }
            }
        }

        private static void ConfigureBasisData(
            DungeonPortalBakedRoomBasisData basis,
            ResolvedRoom room,
            BucketWork[] buckets,
            RepackedAtlasSet p100Atlases,
            RepackedAtlasSet baselineAtlases,
            RepackedAtlasSet fullAtlases,
            DungeonPortalBakedRoomBasisData.OptionalDoorwayProbeResponse doorwayProbe,
            DungeonPortalBakedRoomBasisData.OptionalAmbientProbeStates ambientStates)
        {
            var canonicalBuckets = new DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket[buckets.Length];
            var originalP100 = new DungeonPortalBakedRoomBasisData.Power100SourceAtlas[buckets.Length];
            for (int i = 0; i < buckets.Length; i++)
            {
                BucketWork bucket = buckets[i];
                canonicalBuckets[i] = new DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket();
                canonicalBuckets[i].ConfigureAuthoring(
                    "P0_LM" + bucket.LocalLightmapIndex.ToString("00", CultureInfo.InvariantCulture),
                    bucket.LocalLightmapIndex,
                    bucket.P0Color,
                    p100Atlases.Colors[i],
                    bucket.P0Direction,
                    p100Atlases.Directions[i]);
                originalP100[i] = new DungeonPortalBakedRoomBasisData.Power100SourceAtlas();
                originalP100[i].ConfigureAuthoring(
                    bucket.LocalLightmapIndex,
                    bucket.P100Color,
                    bucket.P100Direction);
            }

            var canonicalEntries = new DungeonPortalBakedRoomBasisData.CanonicalRendererEntry[
                room.CanonicalRenderers.Length];
            for (int i = 0; i < canonicalEntries.Length; i++)
            {
                CanonicalRendererWork source = room.CanonicalRenderers[i];
                canonicalEntries[i] = new DungeonPortalBakedRoomBasisData.CanonicalRendererEntry();
                canonicalEntries[i].ConfigureAuthoring(
                    source.CanonicalKey,
                    source.P0.lightmapIndex,
                    source.P0.lightmapIndex,
                    source.P0.lightmapScaleOffset,
                    source.P100.lightmapIndex,
                    source.P100.lightmapScaleOffset);
            }

            var responses = new DungeonPortalBakedRoomBasisData.ResponseAtlas[buckets.Length];
            for (int i = 0; i < buckets.Length; i++)
            {
                responses[i] = new DungeonPortalBakedRoomBasisData.ResponseAtlas();
                responses[i].ConfigureOnOffAuthoring(
                    i,
                    baselineAtlases.Colors[i],
                    fullAtlases.Colors[i],
                    baselineAtlases.Directions[i],
                    fullAtlases.Directions[i]);
            }
            var lobe = new DungeonPortalBakedRoomBasisData.ReceiverResponseLobe();
            lobe.ConfigureAuthoring(K1BasisId, responses, doorwayProbe);
            var receiverDoor = new DungeonPortalBakedRoomBasisData.ReceiverDoorBasis();
            receiverDoor.ConfigureAuthoring(StableDoorwayId, new[] { lobe });

            DungeonPortalEndpointProfile.PortalDirectLightDescriptor descriptor =
                GetSingleDirectDescriptor(room.Endpoint, room.Spec.RoomId);
            var coefficient = new DungeonPortalBakedRoomBasisData.SourceBasisCoefficient();
            coefficient.ConfigureAuthoring(
                K1BasisId,
                MultiplyRadiance(descriptor.power0Color, descriptor.power0Intensity),
                MultiplyRadiance(descriptor.power100Color, descriptor.power100Intensity));
            var sourceDoor = new DungeonPortalBakedRoomBasisData.SourceDoorBasis();
            sourceDoor.ConfigureAuthoring(StableDoorwayId, new[] { coefficient });

            basis.ConfigureAuthoring(
                room.Spec.RoomId,
                canonicalBuckets,
                canonicalEntries,
                originalP100,
                new[] { receiverDoor },
                new[] { sourceDoor },
                ambientStates,
                Array.Empty<DungeonPortalBakedRoomBasisData.OptionalReflectionStates>());
        }

        private static Color MultiplyRadiance(Color color, float intensity)
        {
            if (!IsFinite(intensity) || intensity < 0f || !IsFinite(color))
                throw new InvalidOperationException("Endpoint radiance contains a non-finite or negative value.");
            return new Color(color.r * intensity, color.g * intensity, color.b * intensity, 1f);
        }

        private static RendererMappingRecord[] BuildMappingRecords(ResolvedRoom room)
        {
            var records = new RendererMappingRecord[room.CanonicalRenderers.Length];
            for (int i = 0; i < records.Length; i++)
            {
                CanonicalRendererWork work = room.CanonicalRenderers[i];
                var record = new RendererMappingRecord
                {
                    canonicalKey = work.CanonicalKey,
                    productionRelativePath = work.ProductionRelativePath,
                    canonicalOccurrence = work.Occurrence,
                    productionMeshAssetGuid = work.ProductionMeshAssetGuid,
                    productionMeshLocalId = work.ProductionMeshLocalId,
                    productionMeshUv2Hash = work.ProductionMeshUv2Hash,
                    lightmapUvMode = work.LightmapUvMode.ToString(),
                    canonicalLocalLightmapIndex = work.P0.lightmapIndex,
                    canonicalScaleOffset = ToArray(work.P0.lightmapScaleOffset),
                    originalP100LocalLightmapIndex = work.P100.lightmapIndex,
                    originalP100ScaleOffset = ToArray(work.P100.lightmapScaleOffset),
                    hasReceiverCapture = work.BaselineCapture.HasValue
                };
                if (work.BaselineCapture.HasValue)
                {
                    DungeonPortalReceiverResponseCapture.CaptureRenderer capture = work.BaselineCapture.Value;
                    record.captureStableRelativePath = capture.relativePath;
                    record.captureLocalLightmapIndex = capture.lightmapIndex;
                    record.captureScaleOffset = ToArray(capture.lightmapScaleOffset);
                }
                records[i] = record;
            }
            return records;
        }

        private static string ComputeCanonicalMappingSignature(CanonicalRendererWork[] values)
        {
            var builder = new StringBuilder(values.Length * 160);
            for (int i = 0; i < values.Length; i++)
            {
                CanonicalRendererWork value = values[i];
                AppendString(builder, value.CanonicalKey);
                AppendString(builder, value.ProductionStablePath);
                AppendInt(builder, value.ProductionComponentOrdinal);
                AppendString(builder, value.ProductionMeshAssetGuid);
                builder.Append(value.ProductionMeshLocalId).Append('|');
                AppendString(builder, value.ProductionMeshUv2Hash);
                AppendString(builder, value.LightmapUvMode.ToString());
                AppendInt(builder, value.P0.lightmapIndex);
                AppendVector4(builder, value.P0.lightmapScaleOffset);
                AppendInt(builder, value.P100.lightmapIndex);
                AppendVector4(builder, value.P100.lightmapScaleOffset);
                builder.Append(value.BaselineCapture.HasValue ? '1' : '0').Append('|');
                if (value.BaselineCapture.HasValue)
                {
                    DungeonPortalReceiverResponseCapture.CaptureRenderer capture = value.BaselineCapture.Value;
                    AppendString(builder, capture.relativePath);
                    AppendInt(builder, capture.componentOrdinal);
                    AppendInt(builder, capture.lightmapIndex);
                    AppendVector4(builder, capture.lightmapScaleOffset);
                }
            }
            return ComputeSha256(builder.ToString());
        }

        private static DungeonPortalBakedRoomBasisData.OptionalDoorwayProbeResponse BuildDoorwayProbeResponse(
            ResolvedRoom room,
            out DoorwayProbeRecord record)
        {
            const float nearDoorPlaneZ = -0.25f;
            float[] off = AverageDoorwayProbePlane(room.Baseline, nearDoorPlaneZ, out int offCount);
            float[] on = AverageDoorwayProbePlane(room.Full, nearDoorPlaneZ, out int onCount);
            if (offCount != 9 || onCount != 9)
            {
                throw new InvalidOperationException(
                    "Receiver response must expose exactly nine canonical near-door probes at z=-0.25 for '" +
                    room.Spec.RoomId + "'. baseline=" + offCount + " full=" + onCount + ".");
            }
            var result = new DungeonPortalBakedRoomBasisData.OptionalDoorwayProbeResponse();
            result.ConfigureAuthoring(off, on);
            record = new DoorwayProbeRecord
            {
                baselineNearDoorSampleCount = offCount,
                fullNearDoorSampleCount = onCount,
                localPlaneZ = nearDoorPlaneZ,
                baselineSh27Signature = ComputeFloatArrayHash(off),
                fullSh27Signature = ComputeFloatArrayHash(on)
            };
            return result;
        }

        private static float[] AverageDoorwayProbePlane(
            DungeonPortalReceiverResponseCapture.CaptureState state,
            float localPlaneZ,
            out int sampleCount)
        {
            DungeonPortalReceiverResponseCapture.ProbeSample[] probes = state.probes ??
                Array.Empty<DungeonPortalReceiverResponseCapture.ProbeSample>();
            var output = new float[DungeonPortalBakedRoomBasisData.SphericalHarmonicsCoefficientCount];
            sampleCount = 0;
            for (int i = 0; i < probes.Length; i++)
            {
                if (Mathf.Abs(probes[i].localPosition.z - localPlaneZ) > 0.0001f)
                    continue;
                sampleCount++;
                for (int coefficient = 0; coefficient < 9; coefficient++)
                {
                    Vector3 value = probes[i].GetCoefficient(coefficient);
                    output[coefficient] += value.x;
                    output[9 + coefficient] += value.y;
                    output[18 + coefficient] += value.z;
                }
            }
            if (sampleCount <= 0)
                throw new InvalidOperationException("Receiver response contains no probes on the requested doorway plane.");
            for (int i = 0; i < output.Length; i++)
            {
                output[i] /= sampleCount;
                if (!IsFinite(output[i]))
                    throw new InvalidOperationException("Doorway-probe response contains a non-finite SH value.");
            }
            return output;
        }

        private static DungeonPortalBakedRoomBasisData.OptionalAmbientProbeStates BuildAmbientProbeStates(
            ResolvedRoom room,
            out AmbientProbeRecord record)
        {
            DungeonTileBakeData.LightProbeBakeEntry[] p0 = room.P0.lightProbeEntries ??
                Array.Empty<DungeonTileBakeData.LightProbeBakeEntry>();
            DungeonTileBakeData.LightProbeBakeEntry[] p100 = room.P100.lightProbeEntries ??
                Array.Empty<DungeonTileBakeData.LightProbeBakeEntry>();
            if (p0.Length < 4 || p100.Length != p0.Length)
            {
                throw new InvalidOperationException(
                    "Production P0/P100 light-probe layouts are incomplete or incompatible for '" + room.Spec.RoomId + "'.");
            }
            for (int i = 0; i < p0.Length; i++)
            {
                if (!Approximately(p0[i].localPosition, p100[i].localPosition))
                {
                    throw new InvalidOperationException(
                        "Production P0/P100 light-probe positions differ at index " + i + " for '" + room.Spec.RoomId + "'.");
                }
            }

            Vector3 targetLocal = FindDoorwayInteriorLocalPosition(room.Prefab);
            var nearest = new List<ProbeDistance>(p0.Length);
            for (int i = 0; i < p0.Length; i++)
                nearest.Add(new ProbeDistance(i, (p0[i].localPosition - targetLocal).sqrMagnitude));
            nearest.Sort(ProbeDistanceComparer.Instance);
            const int selectedCount = 4;
            var indices = new int[selectedCount];
            var weights = new float[selectedCount];
            float weightSum = 0f;
            for (int i = 0; i < selectedCount; i++)
            {
                indices[i] = nearest[i].Index;
                float distance = Mathf.Sqrt(nearest[i].SquaredDistance);
                weights[i] = 1f / Mathf.Max(distance, 0.05f);
                weightSum += weights[i];
            }
            if (!IsFinite(weightSum) || weightSum <= 0f)
                throw new InvalidOperationException("Doorway interior representative probe weights are invalid.");
            for (int i = 0; i < weights.Length; i++)
                weights[i] /= weightSum;

            float[] p0Sh = BlendProbeEntries(p0, indices, weights);
            float[] p100Sh = BlendProbeEntries(p100, indices, weights);
            var states = new DungeonPortalBakedRoomBasisData.OptionalAmbientProbeStates();
            states.ConfigureAuthoring(true, p0Sh, p100Sh);
            record = new AmbientProbeRecord
            {
                targetLocalPosition = new[] { targetLocal.x, targetLocal.y, targetLocal.z },
                selectedP0Indices = (int[])indices.Clone(),
                selectedP100Indices = (int[])indices.Clone(),
                normalizedWeights = (float[])weights.Clone(),
                p0Sh27Signature = ComputeFloatArrayHash(p0Sh),
                p100Sh27Signature = ComputeFloatArrayHash(p100Sh)
            };
            return states;
        }

        private static Vector3 FindDoorwayInteriorLocalPosition(GameObject prefab)
        {
            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform[] transforms = contents.GetComponentsInChildren<Transform>(true);
                Transform doorway = null;
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (string.Equals(GetStableRelativePath(contents.transform, transforms[i]), StableDoorwayId, StringComparison.Ordinal))
                    {
                        doorway = transforms[i];
                        break;
                    }
                }
                if (doorway == null)
                {
                    throw new InvalidOperationException(
                        "Production prefab does not contain the canonical doorway stable path '" + StableDoorwayId + "'.");
                }
                Vector3 worldInterior = doorway.TransformPoint(new Vector3(0f, 1f, -0.25f));
                return contents.transform.InverseTransformPoint(worldInterior);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static float[] BlendProbeEntries(
            DungeonTileBakeData.LightProbeBakeEntry[] entries,
            int[] indices,
            float[] weights)
        {
            if (indices == null || weights == null || indices.Length != weights.Length || indices.Length == 0)
                throw new ArgumentException("Probe blend selection is invalid.");
            var output = new float[DungeonPortalBakedRoomBasisData.SphericalHarmonicsCoefficientCount];
            for (int selected = 0; selected < indices.Length; selected++)
            {
                int index = indices[selected];
                if (index < 0 || index >= entries.Length || !IsFinite(weights[selected]) || weights[selected] < 0f)
                    throw new InvalidOperationException("Probe blend index or weight is invalid.");
                for (int coefficient = 0; coefficient < 9; coefficient++)
                {
                    Vector3 value = entries[index].GetCoefficient(coefficient);
                    output[coefficient] += value.x * weights[selected];
                    output[9 + coefficient] += value.y * weights[selected];
                    output[18 + coefficient] += value.z * weights[selected];
                }
            }
            for (int i = 0; i < output.Length; i++)
            {
                if (!IsFinite(output[i]))
                    throw new InvalidOperationException("Blended doorway representative SH contains a non-finite value.");
            }
            return output;
        }

        private sealed class ProbeDistance
        {
            public readonly int Index;
            public readonly float SquaredDistance;

            public ProbeDistance(int index, float squaredDistance)
            {
                Index = index;
                SquaredDistance = squaredDistance;
            }
        }

        private sealed class ProbeDistanceComparer : IComparer<ProbeDistance>
        {
            public static readonly ProbeDistanceComparer Instance = new ProbeDistanceComparer();

            public int Compare(ProbeDistance left, ProbeDistance right)
            {
                int distance = left.SquaredDistance.CompareTo(right.SquaredDistance);
                return distance != 0 ? distance : left.Index.CompareTo(right.Index);
            }
        }

        private static EndpointNativeResponseDeltaAtlasSet WriteEndpointNativeResponseDeltaAtlasSet(
            string outputFolder,
            string label,
            BucketWork[] buckets,
            List<ChartCopyOperation> baselineOperations,
            List<ChartCopyOperation> fullOperations,
            Material copyMaterial,
            string payloadRoot,
            List<ArtifactFingerprint> artifacts,
            EndpointLayout destinationLayout)
        {
            EnsureFolder(outputFolder);
            var result = new EndpointNativeResponseDeltaAtlasSet(buckets.Length);
            for (int i = 0; i < buckets.Length; i++)
            {
                BucketWork bucket = buckets[i];
                Texture2D targetColor = destinationLayout == EndpointLayout.Power100
                    ? bucket.P100Color
                    : bucket.P0Color;
                Texture2D targetDirection = destinationLayout == EndpointLayout.Power100
                    ? bucket.P100Direction
                    : bucket.P0Direction;
                List<ChartCopyOperation> baselineBucketOperations = baselineOperations
                    .Where(operation => operation.DestinationBucketIndex == bucket.BucketIndex)
                    .ToList();
                List<ChartCopyOperation> fullBucketOperations = fullOperations
                    .Where(operation => operation.DestinationBucketIndex == bucket.BucketIndex)
                    .ToList();
                Texture2D baselineColor = null;
                Texture2D baselineDirection = null;
                Texture2D fullColor = null;
                Texture2D fullDirection = null;
                try
                {
                    baselineColor = RenderEndpointNativeResponseStateTexture(
                        targetColor,
                        baselineBucketOperations,
                        false,
                        copyMaterial,
                        label + " baseline color bucket " + i,
                        out result.BaselineColorRasterization[i]);
                    baselineDirection = RenderEndpointNativeResponseStateTexture(
                        targetDirection,
                        baselineBucketOperations,
                        true,
                        copyMaterial,
                        label + " baseline direction bucket " + i,
                        out result.BaselineDirectionRasterization[i]);
                    fullColor = RenderEndpointNativeResponseStateTexture(
                        targetColor,
                        fullBucketOperations,
                        false,
                        copyMaterial,
                        label + " full color bucket " + i,
                        out result.FullColorRasterization[i]);
                    fullDirection = RenderEndpointNativeResponseStateTexture(
                        targetDirection,
                        fullBucketOperations,
                        true,
                        copyMaterial,
                        label + " full direction bucket " + i,
                        out result.FullDirectionRasterization[i]);
                    ValidateMatchingRasterizationCoverage(
                        result.BaselineColorRasterization[i],
                        result.BaselineDirectionRasterization[i],
                        label + " baseline bucket " + i);
                    ValidateMatchingRasterizationCoverage(
                        result.FullColorRasterization[i],
                        result.FullDirectionRasterization[i],
                        label + " full bucket " + i);
                    ValidateMatchingEndpointResponseStateCoverage(
                        result.BaselineColorRasterization[i],
                        result.FullColorRasterization[i],
                        label + " bucket " + i);

                    string stem = "LM" + bucket.LocalLightmapIndex.ToString("00", CultureInfo.InvariantCulture);
                    string colorPath = outputFolder + "/" + stem + "_ColorDelta.asset";
                    string momentPath = outputFolder + "/" + stem + "_DirectionalMomentDelta.asset";
                    result.ColorDeltas[i] = WriteEndpointNativeDeltaTexture(
                        colorPath,
                        baselineColor,
                        fullColor,
                        baselineDirection,
                        fullDirection,
                        false,
                        targetColor,
                        result.BaselineColorRasterization[i].OwnedCoreMask,
                        label + " color delta bucket " + i,
                        out result.ColorDeltaQuantization[i]);
                    result.DirectionalMomentDeltas[i] = WriteEndpointNativeDeltaTexture(
                        momentPath,
                        baselineColor,
                        fullColor,
                        baselineDirection,
                        fullDirection,
                        true,
                        targetDirection,
                        result.BaselineColorRasterization[i].OwnedCoreMask,
                        label + " directional-moment delta bucket " + i,
                        out result.DirectionalMomentDeltaQuantization[i]);
                    result.ColorDeltaRelativePaths[i] = MakeRelativeAssetPath(payloadRoot, colorPath);
                    result.DirectionalMomentDeltaRelativePaths[i] = MakeRelativeAssetPath(payloadRoot, momentPath);
                    AddArtifactFingerprint(
                        artifacts,
                        payloadRoot,
                        colorPath,
                        label + "ColorDeltaRGBAHalf",
                        result.ColorDeltas[i]);
                    AddArtifactFingerprint(
                        artifacts,
                        payloadRoot,
                        momentPath,
                        label + "DirectionalMomentDeltaRGBAHalf",
                        result.DirectionalMomentDeltas[i]);
                }
                finally
                {
                    if (fullDirection != null)
                        UnityEngine.Object.DestroyImmediate(fullDirection);
                    if (fullColor != null)
                        UnityEngine.Object.DestroyImmediate(fullColor);
                    if (baselineDirection != null)
                        UnityEngine.Object.DestroyImmediate(baselineDirection);
                    if (baselineColor != null)
                        UnityEngine.Object.DestroyImmediate(baselineColor);
                }
            }
            return result;
        }

        /// <summary>
        /// DPBB-2/v2 compact writer. It rasterizes collision-free destination mip0 once,
        /// generates a verified linear box-filtered chain, encodes Full-Baseline as
        /// normalized positive/negative magnitudes, then reconstructs every conservatively
        /// covered texel of every mip. No lower-mip chart ownership is claimed.
        /// </summary>
        private static CompactResponseAtlasSet WriteCompactNormalizedResponseAtlasSet(
            string outputFolder,
            string label,
            BucketWork[] buckets,
            List<ChartCopyOperation> baselineOperations,
            List<ChartCopyOperation> fullOperations,
            Material copyMaterial,
            string payloadRoot,
            List<ArtifactFingerprint> artifacts,
            EndpointLayout destinationLayout)
        {
            EnsureFolder(outputFolder);
            var result = new CompactResponseAtlasSet(buckets.Length);
            for (int i = 0; i < buckets.Length; i++)
            {
                BucketWork bucket = buckets[i];
                Texture2D targetColor = destinationLayout == EndpointLayout.Power100
                    ? bucket.P100Color : bucket.P0Color;
                Texture2D targetDirection = destinationLayout == EndpointLayout.Power100
                    ? bucket.P100Direction : bucket.P0Direction;
                Texture2D baselineColor = null;
                Texture2D baselineDirection = null;
                Texture2D fullColor = null;
                Texture2D fullDirection = null;
                try
                {
                    List<ChartCopyOperation> baselineBucket = baselineOperations
                        .Where(value => value.DestinationBucketIndex == bucket.BucketIndex).ToList();
                    List<ChartCopyOperation> fullBucket = fullOperations
                        .Where(value => value.DestinationBucketIndex == bucket.BucketIndex).ToList();
                    baselineColor = RenderAngleResponseStateTexture(
                        targetColor, baselineBucket, false, copyMaterial, label + " baseline color LM" + i,
                        out result.BaselineColorRasterization[i]);
                    baselineDirection = RenderAngleResponseStateTexture(
                        targetDirection, baselineBucket, true, copyMaterial, label + " baseline direction LM" + i,
                        out result.BaselineDirectionRasterization[i]);
                    fullColor = RenderAngleResponseStateTexture(
                        targetColor, fullBucket, false, copyMaterial, label + " full color LM" + i,
                        out result.FullColorRasterization[i]);
                    fullDirection = RenderAngleResponseStateTexture(
                        targetDirection, fullBucket, true, copyMaterial, label + " full direction LM" + i,
                        out result.FullDirectionRasterization[i]);
                    ValidateMatchingRasterizationCoverage(
                        result.BaselineColorRasterization[i], result.BaselineDirectionRasterization[i],
                        label + " baseline LM" + i);
                    ValidateMatchingRasterizationCoverage(
                        result.FullColorRasterization[i], result.FullDirectionRasterization[i],
                        label + " full LM" + i);
                    ValidateMatchingAllMipResponseCoverage(
                        result.BaselineColorRasterization[i], result.FullColorRasterization[i],
                        label + " same-pose Full-Baseline LM" + i);

                    string stem = "LM" + bucket.LocalLightmapIndex.ToString("00", CultureInfo.InvariantCulture);
                    CompactMagnitudePayload compact = WriteNormalizedSignedCompactTextures(
                        outputFolder,
                        stem,
                        baselineColor,
                        fullColor,
                        baselineDirection,
                        fullDirection,
                        targetColor,
                        targetDirection,
                        result.BaselineColorRasterization[i],
                        label + " LM" + i);
                    result.Assign(i, compact, payloadRoot);
                    AddArtifactFingerprint(artifacts, payloadRoot, compact.ColorPositivePath,
                        label + "ColorPositiveNormalized" + compact.ColorPositive.format,
                        compact.ColorPositive);
                    AddArtifactFingerprint(artifacts, payloadRoot, compact.ColorNegativePath,
                        label + "ColorNegativeNormalized" + compact.ColorNegative.format,
                        compact.ColorNegative);
                    AddArtifactFingerprint(artifacts, payloadRoot, compact.MomentPositivePath,
                        label + "DirectionalMomentPositiveNormalized" + compact.MomentPositive.format,
                        compact.MomentPositive);
                    AddArtifactFingerprint(artifacts, payloadRoot, compact.MomentNegativePath,
                        label + "DirectionalMomentNegativeNormalized" + compact.MomentNegative.format,
                        compact.MomentNegative);
                }
                finally
                {
                    if (fullDirection != null) UnityEngine.Object.DestroyImmediate(fullDirection);
                    if (fullColor != null) UnityEngine.Object.DestroyImmediate(fullColor);
                    if (baselineDirection != null) UnityEngine.Object.DestroyImmediate(baselineDirection);
                    if (baselineColor != null) UnityEngine.Object.DestroyImmediate(baselineColor);
                }
            }
            return result;
        }

        private static Texture2D RenderAngleResponseStateTexture(
            Texture2D targetReference,
            List<ChartCopyOperation> operations,
            bool direction,
            Material copyMaterial,
            string label,
            out RasterizationStats rasterization)
        {
            if (targetReference == null || copyMaterial == null)
                throw new ArgumentNullException(targetReference == null ? nameof(targetReference) : nameof(copyMaterial));
            List<ChartCopyOperation> copies = ResolveAndValidateTriangleOperations(operations, direction, label);
            if (targetReference.mipmapCount <= 1 ||
                targetReference.mipmapCount > DungeonPortalBakedRoomBasisData.NormalizedDeltaMaximumMipCount)
            {
                throw new InvalidOperationException("Compact response requires 2.." +
                                                    DungeonPortalBakedRoomBasisData.NormalizedDeltaMaximumMipCount +
                                                    " destination mips for " + label + ".");
            }
            RenderTexture previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture target = null;
            RenderTexture ownerMin = null;
            RenderTexture ownerMax = null;
            Texture2D readable = null;
            Texture2D destinationMipChain = null;
            var exactSourceMips = new Dictionary<Texture2D, Texture2D>();
            try
            {
                GL.sRGBWrite = false;
                target = new RenderTexture(
                    targetReference.width,
                    targetReference.height,
                    0,
                    RenderTextureFormat.ARGBHalf,
                    RenderTextureReadWrite.Linear)
                {
                    name = "__DPBB_AngleStateMip0_" + label.Replace(' ', '_'),
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave
                };
                target.Create();
                if (!target.IsCreated())
                    throw new InvalidOperationException("Unable to allocate angle response mip0 target for " + label + ".");

                ownerMin = CreateTriangleRasterTarget(
                    targetReference.width,
                    targetReference.height,
                    RenderTextureFormat.RFloat,
                    "AngleOwnerMin_" + label + "_Mip0");
                ownerMax = CreateTriangleRasterTarget(
                    targetReference.width,
                    targetReference.height,
                    RenderTextureFormat.RFloat,
                    "AngleOwnerMax_" + label + "_Mip0");
                PrepareRasterizationMeshes(copies, targetReference.width, targetReference.height);
                RenderOwnerExtrema(copies, ownerMin, ownerMax, copyMaterial);
                RasterizationStats mip0 = ValidateTriangleOwnership(
                    copies,
                    ownerMin,
                    ownerMax,
                    label + " collision-free destination mip0");
                mip0.MipLevel = 0;

                Texture2D exactCanonicalMip0 = GetExactSourceMipTexture(
                    targetReference,
                    0,
                    exactSourceMips,
                    label + " destination orientation source mip0");
                ValidateCanonicalOrientationProof(
                    copies,
                    exactCanonicalMip0,
                    ownerMax,
                    copyMaterial,
                    label + " destination mip0",
                    mip0);

                Graphics.SetRenderTarget(target);
                GL.Clear(
                    true,
                    true,
                    direction ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.black);
                RenderTriangleValues(copies, target, direction, copyMaterial, 0, exactSourceMips, label);
                DilateOnlyIntoUnownedTexels(
                    target,
                    ownerMax,
                    copyMaterial,
                    label + " destination mip0");

                RenderTexture.active = target;
                readable = new Texture2D(
                    targetReference.width,
                    targetReference.height,
                    direction ? TextureFormat.RGBA32 : TextureFormat.RGBAHalf,
                    false,
                    true)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                readable.ReadPixels(
                    new Rect(0f, 0f, targetReference.width, targetReference.height),
                    0,
                    0,
                    false);
                readable.Apply(false, false);

                destinationMipChain = new Texture2D(
                    targetReference.width,
                    targetReference.height,
                    direction ? TextureFormat.RGBA32 : TextureFormat.RGBAHalf,
                    true,
                    true)
                {
                    name = "__DPBB_AngleStateMipChain_" + label.Replace(' ', '_'),
                    filterMode = targetReference.filterMode,
                    wrapModeU = targetReference.wrapModeU,
                    wrapModeV = targetReference.wrapModeV,
                    wrapModeW = targetReference.wrapModeW,
                    anisoLevel = targetReference.anisoLevel,
                    mipMapBias = targetReference.mipMapBias,
                    hideFlags = HideFlags.HideAndDontSave
                };
                destinationMipChain.SetPixels(readable.GetPixels(), 0);
                destinationMipChain.Apply(true, false);
                ValidateGeneratedDestinationMipChain(
                    destinationMipChain,
                    targetReference,
                    direction,
                    label + " angle response");
                rasterization = BuildGeneratedMipRasterization(
                    mip0,
                    targetReference.width,
                    targetReference.height,
                    targetReference.mipmapCount,
                    label);

                Texture2D result = destinationMipChain;
                destinationMipChain = null;
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgbWrite;
                ReleaseTemporaryRasterizationMeshes(copies);
                ReleaseExactSourceMipTextures(exactSourceMips);
                if (destinationMipChain != null) UnityEngine.Object.DestroyImmediate(destinationMipChain);
                if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
                if (ownerMax != null) UnityEngine.Object.DestroyImmediate(ownerMax);
                if (ownerMin != null) UnityEngine.Object.DestroyImmediate(ownerMin);
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static CompactMagnitudePayload WriteNormalizedSignedCompactTextures(
            string outputFolder,
            string stem,
            Texture2D baselineColor,
            Texture2D fullColor,
            Texture2D baselineDirection,
            Texture2D fullDirection,
            Texture2D colorReference,
            Texture2D directionReference,
            RasterizationStats ownership,
            string label)
        {
            ValidateCompactStateInputs(
                baselineColor, fullColor, baselineDirection, fullDirection, colorReference, directionReference,
                ownership, label);
            Texture2D colorPositive = null;
            Texture2D colorNegative = null;
            Texture2D momentPositive = null;
            Texture2D momentNegative = null;
            try
            {
                colorPositive = CreateNormalizedMagnitudeTexture(colorReference, false, label + " color positive");
                colorNegative = CreateNormalizedMagnitudeTexture(colorReference, false, label + " color negative");
                momentPositive = CreateNormalizedMagnitudeTexture(directionReference, true, label + " moment positive");
                momentNegative = CreateNormalizedMagnitudeTexture(directionReference, true, label + " moment negative");
                int mipCount = colorReference.mipmapCount;
                var colorPositiveScales = new Vector4[mipCount];
                var colorNegativeScales = new Vector4[mipCount];
                var momentPositiveScales = new Vector4[mipCount];
                var momentNegativeScales = new Vector4[mipCount];
                for (int mip = 0; mip < mipCount; mip++)
                {
                    Color[] offColor = baselineColor.GetPixels(mip);
                    Color[] onColor = fullColor.GetPixels(mip);
                    Color[] offDirection = baselineDirection.GetPixels(mip);
                    Color[] onDirection = fullDirection.GetPixels(mip);
                    var colorDelta = new Color[offColor.Length];
                    var momentDelta = new Color[offColor.Length];
                    for (int pixel = 0; pixel < colorDelta.Length; pixel++)
                    {
                        colorDelta[pixel] = ComputeSignedResponseDelta(
                            offColor[pixel], onColor[pixel], offDirection[pixel], onDirection[pixel], false);
                        momentDelta[pixel] = ComputeSignedResponseDelta(
                            offColor[pixel], onColor[pixel], offDirection[pixel], onDirection[pixel], true);
                    }
                    colorPositiveScales[mip] = ComputeMagnitudeScale(colorDelta, false, true, label, mip);
                    colorNegativeScales[mip] = ComputeMagnitudeScale(colorDelta, false, false, label, mip);
                    momentPositiveScales[mip] = ComputeMagnitudeScale(momentDelta, true, true, label, mip);
                    momentNegativeScales[mip] = ComputeMagnitudeScale(momentDelta, true, false, label, mip);
                    colorPositive.SetPixels(NormalizeMagnitude(colorDelta, colorPositiveScales[mip], false, true), mip);
                    colorNegative.SetPixels(NormalizeMagnitude(colorDelta, colorNegativeScales[mip], false, false), mip);
                    momentPositive.SetPixels(NormalizeMagnitude(momentDelta, momentPositiveScales[mip], true, true), mip);
                    momentNegative.SetPixels(NormalizeMagnitude(momentDelta, momentNegativeScales[mip], true, false), mip);
                }
                colorPositive.Apply(false, false);
                colorNegative.Apply(false, false);
                momentPositive.Apply(false, false);
                momentNegative.Apply(false, false);

                string colorPositivePath = outputFolder + "/" + stem + "_ColorPositive.asset";
                string colorNegativePath = outputFolder + "/" + stem + "_ColorNegative.asset";
                string momentPositivePath = outputFolder + "/" + stem + "_DirectionalMomentPositive.asset";
                string momentNegativePath = outputFolder + "/" + stem + "_DirectionalMomentNegative.asset";
                TextureFormat colorStorageFormat = SelectCompactColorStorageFormat(true);
                Texture2D persistedColorPositive = CreateCompactMagnitudeAsset(
                    colorPositivePath, colorPositive, colorStorageFormat, colorReference, label + " color positive");
                Texture2D persistedColorNegative = CreateCompactMagnitudeAsset(
                    colorNegativePath, colorNegative, colorStorageFormat, colorReference, label + " color negative");
                CompactDeltaReconstructionStats[] colorReconstruction;
                if (!TryMeasureCompactReconstruction(
                        baselineColor, fullColor, baselineDirection, fullDirection,
                        persistedColorPositive, persistedColorNegative,
                        colorPositiveScales, colorNegativeScales, false, ownership, label + " color",
                        out colorReconstruction))
                {
                    colorStorageFormat = SelectCompactColorStorageFormat(false);
                    DeleteCompactMagnitudeAsset(colorPositivePath, label + " failed BC6H color positive");
                    DeleteCompactMagnitudeAsset(colorNegativePath, label + " failed BC6H color negative");
                    persistedColorPositive = CreateCompactMagnitudeAsset(
                        colorPositivePath, colorPositive, colorStorageFormat, colorReference,
                        label + " fallback color positive");
                    persistedColorNegative = CreateCompactMagnitudeAsset(
                        colorNegativePath, colorNegative, colorStorageFormat, colorReference,
                        label + " fallback color negative");
                    colorReconstruction = MeasureCompactReconstruction(
                        baselineColor, fullColor, baselineDirection, fullDirection,
                        persistedColorPositive, persistedColorNegative,
                        colorPositiveScales, colorNegativeScales, false, ownership,
                        label + " fallback color");
                }
                TextureFormat momentStorageFormat = SelectCompactMomentStorageFormat(true);
                Texture2D persistedMomentPositive = CreateCompactMagnitudeAsset(
                    momentPositivePath, momentPositive, momentStorageFormat, directionReference,
                    label + " moment positive");
                Texture2D persistedMomentNegative = CreateCompactMagnitudeAsset(
                    momentNegativePath, momentNegative, momentStorageFormat, directionReference,
                    label + " moment negative");
                CompactDeltaReconstructionStats[] momentReconstruction;
                if (!TryMeasureCompactReconstruction(
                        baselineColor, fullColor, baselineDirection, fullDirection,
                        persistedMomentPositive, persistedMomentNegative,
                        momentPositiveScales, momentNegativeScales, true, ownership,
                        label + " directional moment", out momentReconstruction))
                {
                    momentStorageFormat = SelectCompactMomentStorageFormat(false);
                    DeleteCompactMagnitudeAsset(momentPositivePath, label + " failed BC7 moment positive");
                    DeleteCompactMagnitudeAsset(momentNegativePath, label + " failed BC7 moment negative");
                    persistedMomentPositive = CreateCompactMagnitudeAsset(
                        momentPositivePath, momentPositive, momentStorageFormat, directionReference,
                        label + " fallback moment positive");
                    persistedMomentNegative = CreateCompactMagnitudeAsset(
                        momentNegativePath, momentNegative, momentStorageFormat, directionReference,
                        label + " fallback moment negative");
                    momentReconstruction = MeasureCompactReconstruction(
                        baselineColor, fullColor, baselineDirection, fullDirection,
                        persistedMomentPositive, persistedMomentNegative,
                        momentPositiveScales, momentNegativeScales, true, ownership,
                        label + " fallback directional moment");
                }

                return new CompactMagnitudePayload
                {
                    ColorPositive = persistedColorPositive,
                    ColorNegative = persistedColorNegative,
                    MomentPositive = persistedMomentPositive,
                    MomentNegative = persistedMomentNegative,
                    ColorPositivePath = colorPositivePath,
                    ColorNegativePath = colorNegativePath,
                    MomentPositivePath = momentPositivePath,
                    MomentNegativePath = momentNegativePath,
                    ColorPositiveScales = colorPositiveScales,
                    ColorNegativeScales = colorNegativeScales,
                    MomentPositiveScales = momentPositiveScales,
                    MomentNegativeScales = momentNegativeScales,
                    ColorReconstruction = colorReconstruction,
                    MomentReconstruction = momentReconstruction
                };
            }
            finally
            {
                if (momentNegative != null) UnityEngine.Object.DestroyImmediate(momentNegative);
                if (momentPositive != null) UnityEngine.Object.DestroyImmediate(momentPositive);
                if (colorNegative != null) UnityEngine.Object.DestroyImmediate(colorNegative);
                if (colorPositive != null) UnityEngine.Object.DestroyImmediate(colorPositive);
            }
        }

        private static void ValidateCompactStateInputs(
            Texture2D baselineColor,
            Texture2D fullColor,
            Texture2D baselineDirection,
            Texture2D fullDirection,
            Texture2D colorReference,
            Texture2D directionReference,
            RasterizationStats ownership,
            string label)
        {
            if (baselineColor == null || fullColor == null || baselineDirection == null || fullDirection == null ||
                colorReference == null || directionReference == null || ownership == null ||
                baselineColor.width != fullColor.width || baselineColor.height != fullColor.height ||
                baselineColor.mipmapCount != fullColor.mipmapCount ||
                baselineColor.width != baselineDirection.width || baselineColor.height != baselineDirection.height ||
                baselineColor.mipmapCount != baselineDirection.mipmapCount ||
                baselineColor.width != colorReference.width || baselineColor.height != colorReference.height ||
                baselineColor.mipmapCount != colorReference.mipmapCount ||
                baselineDirection.width != directionReference.width || baselineDirection.height != directionReference.height ||
                baselineDirection.mipmapCount != directionReference.mipmapCount ||
                ownership.MipLevels == null || ownership.MipLevels.Length != baselineColor.mipmapCount)
            {
                throw new InvalidOperationException("Compact response state/input/mip ownership is inconsistent for " + label + ".");
            }
        }

        private static Texture2D CreateNormalizedMagnitudeTexture(
            Texture2D reference,
            bool direction,
            string label)
        {
            var texture = new Texture2D(reference.width, reference.height,
                direction ? TextureFormat.RGBA32 : TextureFormat.RGBAHalf, true, true)
            {
                name = "__DPBB_Normalized_" + label.Replace(' ', '_'),
                filterMode = reference.filterMode,
                wrapModeU = reference.wrapModeU,
                wrapModeV = reference.wrapModeV,
                wrapModeW = reference.wrapModeW,
                anisoLevel = reference.anisoLevel,
                mipMapBias = reference.mipMapBias,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (texture.mipmapCount != reference.mipmapCount)
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw new InvalidOperationException("Normalized compact texture mip cardinality differs for " + label + ".");
            }
            return texture;
        }

        private static Color ComputeSignedResponseDelta(
            Color offColor,
            Color onColor,
            Color offDirection,
            Color onDirection,
            bool directionalMoment)
        {
            Color value = directionalMoment
                ? Subtract(DecodeDirectionalMoment(onColor, onDirection),
                    DecodeDirectionalMoment(offColor, offDirection))
                : new Color(onColor.r - offColor.r, onColor.g - offColor.g, onColor.b - offColor.b, 0f);
            if (!IsFinite(value) || MaxAbsoluteChannel(value) > 65504f)
                throw new InvalidOperationException("Compact signed response delta is non-finite or exceeds RGBAHalf range.");
            return value;
        }

        private static Vector4 ComputeMagnitudeScale(
            Color[] source,
            bool includeAlpha,
            bool positive,
            string label,
            int mip)
        {
            int channelCount = includeAlpha ? 4 : 3;
            Vector4 scale = Vector4.zero;
            for (int pixel = 0; pixel < source.Length; pixel++)
            {
                Color value = source[pixel];
                for (int channel = 0; channel < channelCount; channel++)
                {
                    float signed = value[channel];
                    if (!IsFinite(signed))
                        throw new InvalidOperationException("Compact response source contains a non-finite channel for " + label + ".");
                    float magnitude = ComputeUnsignedMagnitude(signed, positive);
                    scale[channel] = Mathf.Max(scale[channel], magnitude);
                }
            }
            for (int channel = 0; channel < 4; channel++)
            {
                if (channel >= channelCount)
                    scale[channel] = DungeonPortalBakedRoomBasisData.NormalizedDeltaZeroChannelScale;
                else if (scale[channel] < DungeonPortalBakedRoomBasisData.NormalizedDeltaZeroChannelScale)
                    scale[channel] = DungeonPortalBakedRoomBasisData.NormalizedDeltaZeroChannelScale;
                if (!IsFinite(scale[channel]) || scale[channel] <= 0f)
                {
                    throw new InvalidOperationException("Compact response scale is invalid for " + label + " mip " + mip + ".");
                }
            }
            return scale;
        }

        private static Color[] NormalizeMagnitude(
            Color[] signed,
            Vector4 scale,
            bool includeAlpha,
            bool positive)
        {
            int channelCount = includeAlpha ? 4 : 3;
            var output = new Color[signed.Length];
            for (int pixel = 0; pixel < output.Length; pixel++)
            {
                for (int channel = 0; channel < channelCount; channel++)
                {
                    float magnitude = ComputeUnsignedMagnitude(
                        signed[pixel][channel], positive);
                    float normalized = magnitude / scale[channel];
                    if (!IsFinite(normalized) || normalized < -0.00001f || normalized > 1.00001f)
                        throw new InvalidOperationException("Compact response normalization escaped [0,1].");
                    output[pixel][channel] = Mathf.Clamp01(normalized);
                }
                if (!includeAlpha)
                    output[pixel].a = 0f;
            }
            return output;
        }

        private static float ComputeUnsignedMagnitude(float signed, bool positive)
        {
            float magnitude = positive
                ? Mathf.Max(0f, signed)
                : Mathf.Max(0f, -signed);
            // Mathf.Max may return the second operand for equal values, so negating +0
            // can leak IEEE -0 into the negative-magnitude atlas. Unity's unsigned BC6H
            // compressor can interpret that sign bit as its maximum finite value. Always
            // canonicalize zero before any compact texture is uploaded or compressed.
            return magnitude <= 0f ? 0f : magnitude;
        }

        internal static float ComputeUnsignedMagnitudeForTest(float signed, bool positive)
        {
            return ComputeUnsignedMagnitude(signed, positive);
        }

        internal static Vector4 ComputeMagnitudeScaleForTest(
            Color[] source,
            bool includeAlpha,
            bool positive)
        {
            return ComputeMagnitudeScale(source, includeAlpha, positive, "test", 0);
        }

        private static TextureFormat SelectCompactColorStorageFormat(bool bc6hReconstructionPassed)
        {
            return bc6hReconstructionPassed ? TextureFormat.BC6H : TextureFormat.RGBAHalf;
        }

        internal static TextureFormat SelectCompactColorStorageFormatForTest(bool bc6hReconstructionPassed)
        {
            return SelectCompactColorStorageFormat(bc6hReconstructionPassed);
        }

        private static TextureFormat SelectCompactMomentStorageFormat(bool bc7ReconstructionPassed)
        {
            return bc7ReconstructionPassed ? TextureFormat.BC7 : TextureFormat.RGBAHalf;
        }

        internal static TextureFormat SelectCompactMomentStorageFormatForTest(bool bc7ReconstructionPassed)
        {
            return SelectCompactMomentStorageFormat(bc7ReconstructionPassed);
        }

        private static TextureFormat SelectBaseTransitionColorStorageFormat(bool bc6hReconstructionPassed)
        {
            return bc6hReconstructionPassed ? TextureFormat.BC6H : TextureFormat.RGBAHalf;
        }

        internal static TextureFormat SelectBaseTransitionColorStorageFormatForTest(bool bc6hReconstructionPassed)
        {
            return SelectBaseTransitionColorStorageFormat(bc6hReconstructionPassed);
        }

        private static void DeleteCompactMagnitudeAsset(string assetPath, string label)
        {
            if (string.IsNullOrWhiteSpace(assetPath) ||
                !AssetDatabase.DeleteAsset(assetPath) ||
                AssetDatabase.LoadMainAssetAtPath(assetPath) != null ||
                File.Exists(AssetPathToPhysicalPath(assetPath)))
            {
                throw new InvalidOperationException(
                    "Unable to replace the failed compact response asset for " + label + ": '" +
                    assetPath + "'.");
            }
        }

        private static Texture2D CreateCompactMagnitudeAsset(
            string assetPath,
            Texture2D source,
            TextureFormat format,
            Texture2D samplingReference,
            string label)
        {
            if (source == null || samplingReference == null ||
                AssetDatabase.LoadMainAssetAtPath(assetPath) != null || File.Exists(AssetPathToPhysicalPath(assetPath)))
            {
                throw new InvalidOperationException("Refusing to overwrite/create an invalid compact response target: '" + assetPath + "'.");
            }
            Texture2D compressed = null;
            try
            {
                compressed = UnityEngine.Object.Instantiate(source);
                compressed.name = Path.GetFileNameWithoutExtension(assetPath);
                compressed.hideFlags = HideFlags.None;
                compressed.filterMode = samplingReference.filterMode;
                compressed.wrapModeU = samplingReference.wrapModeU;
                compressed.wrapModeV = samplingReference.wrapModeV;
                compressed.wrapModeW = samplingReference.wrapModeW;
                compressed.anisoLevel = samplingReference.anisoLevel;
                compressed.mipMapBias = samplingReference.mipMapBias;
                if (compressed.format != format)
                    EditorUtility.CompressTexture(compressed, format, TextureCompressionQuality.Best);
                if (compressed.format != format || compressed.mipmapCount != source.mipmapCount)
                    throw new InvalidOperationException("Compact response compression changed its format/mip contract for " + label + ".");
                AssetDatabase.CreateAsset(compressed, assetPath);
                EditorUtility.SetDirty(compressed);
                AssetDatabase.SaveAssetIfDirty(compressed);
                compressed = null;
            }
            finally
            {
                if (compressed != null) UnityEngine.Object.DestroyImmediate(compressed);
            }
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            Texture2D imported = LoadRequired<Texture2D>(assetPath);
            ValidateSampleTexture(imported, label, format == TextureFormat.BC7);
            if (imported.format != format || imported.width != samplingReference.width ||
                imported.height != samplingReference.height || imported.mipmapCount != samplingReference.mipmapCount ||
                imported.filterMode != samplingReference.filterMode || imported.wrapModeU != samplingReference.wrapModeU ||
                imported.wrapModeV != samplingReference.wrapModeV || imported.wrapModeW != samplingReference.wrapModeW ||
                imported.anisoLevel != samplingReference.anisoLevel ||
                !Mathf.Approximately(imported.mipMapBias, samplingReference.mipMapBias))
            {
                throw new InvalidOperationException("Compact response imported texture violates its native sampling contract for " + label + ".");
            }
            return imported;
        }

        private static bool TryMeasureCompactReconstruction(
            Texture2D baselineColor,
            Texture2D fullColor,
            Texture2D baselineDirection,
            Texture2D fullDirection,
            Texture2D positive,
            Texture2D negative,
            Vector4[] positiveScales,
            Vector4[] negativeScales,
            bool directionalMoment,
            RasterizationStats ownership,
            string label,
            out CompactDeltaReconstructionStats[] result)
        {
            try
            {
                result = MeasureCompactReconstruction(
                    baselineColor,
                    fullColor,
                    baselineDirection,
                    fullDirection,
                    positive,
                    negative,
                    positiveScales,
                    negativeScales,
                    directionalMoment,
                    ownership,
                    label);
                return true;
            }
            catch (CompactReconstructionQualityException)
            {
                result = null;
                return false;
            }
        }

        private static CompactDeltaReconstructionStats[] MeasureCompactReconstruction(
            Texture2D baselineColor,
            Texture2D fullColor,
            Texture2D baselineDirection,
            Texture2D fullDirection,
            Texture2D positive,
            Texture2D negative,
            Vector4[] positiveScales,
            Vector4[] negativeScales,
            bool directionalMoment,
            RasterizationStats ownership,
            string label)
        {
            int mipCount = baselineColor != null ? baselineColor.mipmapCount : 0;
            if (positive == null || negative == null || positiveScales == null || negativeScales == null ||
                ownership == null || ownership.MipLevels == null ||
                positive.mipmapCount != mipCount || negative.mipmapCount != mipCount ||
                positiveScales.Length != mipCount || negativeScales.Length != mipCount ||
                ownership.MipLevels.Length != mipCount)
            {
                throw new InvalidOperationException("Compact reconstruction inputs are incomplete for " + label + ".");
            }
            int channelCount = directionalMoment ? 4 : 3;
            var result = new CompactDeltaReconstructionStats[mipCount];
            for (int mip = 0; mip < mipCount; mip++)
            {
                RasterizationStats level = ownership.MipLevels[mip];
                Color[] offColor = baselineColor.GetPixels(mip);
                Color[] onColor = fullColor.GetPixels(mip);
                Color[] offDirection = baselineDirection.GetPixels(mip);
                Color[] onDirection = fullDirection.GetPixels(mip);
                Color[] positivePixels = positive.GetPixels(mip);
                Color[] negativePixels = negative.GetPixels(mip);
                if (level == null || level.SampledCoverageMask == null ||
                    level.SampledCoverageMask.Length != offColor.Length ||
                    positivePixels.Length != offColor.Length || negativePixels.Length != offColor.Length)
                {
                    throw new InvalidOperationException("Compact reconstruction sampled coverage/readback is incomplete for " + label + " mip " + mip + ".");
                }
                double squaredError = 0d;
                double squaredExpected = 0d;
                float maxAbsoluteError = 0f;
                int sampledTexels = 0;
                int wrongSignCount = 0;
                int quantizedToZeroCount = 0;
                int wrongSignExpectedAbove1e4Count = 0;
                int wrongSignExpectedAbove1e3Count = 0;
                float wrongSignMaxExpectedMagnitude = 0f;
                float wrongSignMaxReconstructedMagnitude = 0f;
                float wrongSignMaxAbsoluteError = 0f;
                int errorAbove01Count = 0;
                int errorAbove02Count = 0;
                int errorAbove025Count = 0;
                int coreErrorAbove025Count = 0;
                int maxErrorPixel = -1;
                int maxErrorChannel = -1;
                float maxErrorExpectedValue = 0f;
                float maxErrorReconstructedValue = 0f;
                bool maxErrorIsCore = false;
                bool maxErrorIsImplicitZeroCore = false;
                for (int pixel = 0; pixel < offColor.Length; pixel++)
                {
                    if (!level.SampledCoverageMask[pixel])
                        continue;
                    sampledTexels++;
                    Color expected = ComputeSignedResponseDelta(
                        offColor[pixel], onColor[pixel], offDirection[pixel], onDirection[pixel], directionalMoment);
                    for (int channel = 0; channel < channelCount; channel++)
                    {
                        float reconstructed = positivePixels[pixel][channel] * positiveScales[mip][channel] -
                                              negativePixels[pixel][channel] * negativeScales[mip][channel];
                        float expectedValue = expected[channel];
                        if (!IsFinite(reconstructed) || !IsFinite(expectedValue))
                            throw new InvalidOperationException("Compact reconstruction produced a non-finite channel for " + label + ".");
                        float error = reconstructed - expectedValue;
                        squaredError += (double)error * error;
                        squaredExpected += (double)expectedValue * expectedValue;
                        float absoluteError = Mathf.Abs(error);
                        bool isCore = level.OwnedCoreMask != null &&
                                      pixel < level.OwnedCoreMask.Length &&
                                      level.OwnedCoreMask[pixel];
                        bool isImplicitZeroCore = level.ImplicitZeroCoreMask != null &&
                                                  pixel < level.ImplicitZeroCoreMask.Length &&
                                                  level.ImplicitZeroCoreMask[pixel];
                        if (absoluteError > 0.1f)
                            errorAbove01Count++;
                        if (absoluteError > 0.2f)
                            errorAbove02Count++;
                        if (absoluteError > 0.25f)
                        {
                            errorAbove025Count++;
                            if (isCore)
                                coreErrorAbove025Count++;
                        }
                        if (absoluteError > maxAbsoluteError)
                        {
                            maxAbsoluteError = absoluteError;
                            maxErrorPixel = pixel;
                            maxErrorChannel = channel;
                            maxErrorExpectedValue = expectedValue;
                            maxErrorReconstructedValue = reconstructed;
                            maxErrorIsCore = isCore;
                            maxErrorIsImplicitZeroCore = isImplicitZeroCore;
                        }
                        CompactSignClassification sign = ClassifyCompactSign(
                            expectedValue,
                            reconstructed);
                        if (sign == CompactSignClassification.Opposite)
                        {
                            wrongSignCount++;
                            float expectedMagnitude = Mathf.Abs(expectedValue);
                            float reconstructedMagnitude = Mathf.Abs(reconstructed);
                            if (expectedMagnitude > 0.0001f)
                                wrongSignExpectedAbove1e4Count++;
                            if (expectedMagnitude > 0.001f)
                                wrongSignExpectedAbove1e3Count++;
                            wrongSignMaxExpectedMagnitude = Mathf.Max(
                                wrongSignMaxExpectedMagnitude,
                                expectedMagnitude);
                            wrongSignMaxReconstructedMagnitude = Mathf.Max(
                                wrongSignMaxReconstructedMagnitude,
                                reconstructedMagnitude);
                            wrongSignMaxAbsoluteError = Mathf.Max(
                                wrongSignMaxAbsoluteError,
                                absoluteError);
                        }
                        else if (sign == CompactSignClassification.QuantizedToZero)
                        {
                            quantizedToZeroCount++;
                        }
                    }
                }
                if (sampledTexels <= 0)
                    throw new InvalidOperationException("Compact reconstruction has no sampled texels for " + label + " mip " + mip + ".");
                int samples = sampledTexels * channelCount;
                float absoluteRmse = (float)Math.Sqrt(squaredError / samples);
                float relativeRmse = (float)Math.Sqrt(squaredError / Math.Max(1e-12d, squaredExpected));
                bool exceedsThreshold = directionalMoment
                    ? absoluteRmse > DirectionMipCompressionMaxAbsoluteRmse ||
                      maxAbsoluteError > ResponseDirectionMipCompressionMaxAbsoluteError
                    : absoluteRmse > ResponseColorMipCompressionMaxAbsoluteRmse ||
                      (relativeRmse > ResponseColorMipCompressionMaxRelativeRmse &&
                       absoluteRmse > ResponseColorMipCompressionRelativeErrorFloor) ||
                      maxAbsoluteError > ResponseColorMipCompressionMaxAbsoluteError;
                if (wrongSignCount != 0 || exceedsThreshold)
                {
                    throw new CompactReconstructionQualityException(
                        "Compact response reconstruction failed for " + label + " mip " + mip +
                        ". rmse=" + absoluteRmse.ToString("R", CultureInfo.InvariantCulture) +
                        " relative=" + relativeRmse.ToString("R", CultureInfo.InvariantCulture) +
                        " max=" + maxAbsoluteError.ToString("R", CultureInfo.InvariantCulture) +
                        " wrongSign=" + wrongSignCount +
                        " quantizedToZero=" + quantizedToZeroCount +
                        " wrongSignExpectedAbove1e-4=" + wrongSignExpectedAbove1e4Count +
                        " wrongSignExpectedAbove1e-3=" + wrongSignExpectedAbove1e3Count +
                        " wrongSignMaxExpected=" + wrongSignMaxExpectedMagnitude.ToString("R", CultureInfo.InvariantCulture) +
                        " wrongSignMaxReconstructed=" + wrongSignMaxReconstructedMagnitude.ToString("R", CultureInfo.InvariantCulture) +
                        " wrongSignMaxError=" + wrongSignMaxAbsoluteError.ToString("R", CultureInfo.InvariantCulture) +
                        " errorAbove0.1=" + errorAbove01Count +
                        " errorAbove0.2=" + errorAbove02Count +
                        " errorAbove0.25=" + errorAbove025Count +
                        " coreErrorAbove0.25=" + coreErrorAbove025Count +
                        " maxPixel=" + maxErrorPixel +
                        " maxChannel=" + maxErrorChannel +
                        " maxExpected=" + maxErrorExpectedValue.ToString("R", CultureInfo.InvariantCulture) +
                        " maxReconstructed=" + maxErrorReconstructedValue.ToString("R", CultureInfo.InvariantCulture) +
                        " maxIsCore=" + maxErrorIsCore +
                        " maxIsImplicitZeroCore=" + maxErrorIsImplicitZeroCore + ".");
                }
                result[mip] = new CompactDeltaReconstructionStats
                {
                    mipLevel = mip,
                    ownedTexelCount = sampledTexels,
                    channelSampleCount = samples,
                    absoluteRmse = absoluteRmse,
                    relativeRmse = relativeRmse,
                    absoluteMax = maxAbsoluteError,
                    wrongSignCount = wrongSignCount
                };
            }
            return result;
        }

        internal static CompactSignClassification ClassifyCompactSign(
            float expected,
            float reconstructed)
        {
            if (expected > CompactResponseWrongSignEpsilon)
            {
                if (reconstructed < -CompactResponseWrongSignEpsilon)
                    return CompactSignClassification.Opposite;
                if (reconstructed <= CompactResponseWrongSignEpsilon)
                    return CompactSignClassification.QuantizedToZero;
            }
            else if (expected < -CompactResponseWrongSignEpsilon)
            {
                if (reconstructed > CompactResponseWrongSignEpsilon)
                    return CompactSignClassification.Opposite;
                if (reconstructed >= -CompactResponseWrongSignEpsilon)
                    return CompactSignClassification.QuantizedToZero;
            }
            return CompactSignClassification.Preserved;
        }

        private static void ValidateMatchingAllMipResponseCoverage(
            RasterizationStats baseline,
            RasterizationStats full,
            string label)
        {
            RasterizationStats[] baselineLevels = baseline != null ? baseline.MipLevels : null;
            RasterizationStats[] fullLevels = full != null ? full.MipLevels : null;
            if (baselineLevels == null || fullLevels == null || baselineLevels.Length == 0 ||
                baselineLevels.Length != fullLevels.Length)
            {
                throw new InvalidOperationException("Same-pose response mip coverage is incomplete for " + label + ".");
            }
            for (int mip = 0; mip < baselineLevels.Length; mip++)
            {
                RasterizationStats left = baselineLevels[mip];
                RasterizationStats right = fullLevels[mip];
                if (left == null || right == null || left.MipLevel != mip || right.MipLevel != mip ||
                    left.OperationCount != right.OperationCount ||
                    left.OwnedCoreTexelCount != right.OwnedCoreTexelCount ||
                    left.ImplicitZeroCoreTexelCount != right.ImplicitZeroCoreTexelCount ||
                    left.OwnerCollisionCount != 0 || right.OwnerCollisionCount != 0 ||
                    left.OwnedCoreMask == null || right.OwnedCoreMask == null ||
                    left.OwnedCoreMask.Length != right.OwnedCoreMask.Length ||
                    left.ImplicitZeroCoreMask == null || right.ImplicitZeroCoreMask == null ||
                    left.ImplicitZeroCoreMask.Length != right.ImplicitZeroCoreMask.Length ||
                    left.SampledCoverageMask == null || right.SampledCoverageMask == null ||
                    left.SampledCoverageMask.Length != right.SampledCoverageMask.Length)
                {
                    throw new InvalidOperationException("Same-pose response coverage drifted for " + label + " mip " + mip + ".");
                }
                for (int texel = 0; texel < left.OwnedCoreMask.Length; texel++)
                {
                    if (left.OwnedCoreMask[texel] != right.OwnedCoreMask[texel] ||
                        left.ImplicitZeroCoreMask[texel] != right.ImplicitZeroCoreMask[texel] ||
                        left.SampledCoverageMask[texel] != right.SampledCoverageMask[texel])
                    {
                        throw new InvalidOperationException(
                            "Same-pose response coverage mask drifted for " + label +
                            " mip " + mip + " texel " + texel + ".");
                    }
                }
            }
        }

        private static Texture2D RenderEndpointNativeResponseStateTexture(
            Texture2D targetReference,
            List<ChartCopyOperation> operations,
            bool direction,
            Material copyMaterial,
            string label,
            out RasterizationStats rasterization)
        {
            rasterization = null;
            if (targetReference == null || copyMaterial == null)
                throw new ArgumentNullException(targetReference == null ? nameof(targetReference) : nameof(copyMaterial));
            List<ChartCopyOperation> copies = ResolveAndValidateTriangleOperations(operations, direction, label);
            RenderTexture previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture target = null;
            RenderTexture ownerMin = null;
            RenderTexture ownerMax = null;
            Texture2D readable = null;
            Texture2D destinationMipChain = null;
            var exactSourceMips = new Dictionary<Texture2D, Texture2D>();
            try
            {
                GL.sRGBWrite = false;
                target = new RenderTexture(
                    targetReference.width,
                    targetReference.height,
                    0,
                    RenderTextureFormat.ARGBHalf,
                    RenderTextureReadWrite.Linear)
                {
                    name = "__DPBB_EndpointNativeState_" + label.Replace(' ', '_'),
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave
                };
                target.Create();
                if (!target.IsCreated())
                    throw new InvalidOperationException("Unable to allocate endpoint-native response state for " + label + ".");
                ownerMin = CreateTriangleRasterTarget(
                    targetReference.width,
                    targetReference.height,
                    RenderTextureFormat.RFloat,
                    "EndpointNativeStateOwnerMin_" + label);
                ownerMax = CreateTriangleRasterTarget(
                    targetReference.width,
                    targetReference.height,
                    RenderTextureFormat.RFloat,
                    "EndpointNativeStateOwnerMax_" + label);
                PrepareRasterizationMeshes(copies, targetReference.width, targetReference.height);
                RenderOwnerExtrema(copies, ownerMin, ownerMax, copyMaterial);
                RasterizationStats level = ValidateTriangleOwnership(
                    copies,
                    ownerMin,
                    ownerMax,
                    label + " destination-native mip0");
                level.MipLevel = 0;
                level.MipLevels = new[] { level };
                rasterization = level;

                Graphics.SetRenderTarget(target);
                GL.Clear(
                    true,
                    true,
                    direction ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.black);
                RenderTriangleValues(
                    copies,
                    target,
                    direction,
                    copyMaterial,
                    0,
                    exactSourceMips,
                    label);
                DilateOnlyIntoUnownedTexels(target, ownerMax, copyMaterial, label + " destination-native mip0");

                RenderTexture.active = target;
                readable = new Texture2D(
                    targetReference.width,
                    targetReference.height,
                    direction ? TextureFormat.RGBA32 : TextureFormat.RGBAHalf,
                    false,
                    true)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                readable.ReadPixels(
                    new Rect(0f, 0f, targetReference.width, targetReference.height),
                    0,
                    0,
                    false);
                readable.Apply(false, false);
                destinationMipChain = new Texture2D(
                    targetReference.width,
                    targetReference.height,
                    direction ? TextureFormat.RGBA32 : TextureFormat.RGBAHalf,
                    true,
                    true)
                {
                    name = "__DPBB_EndpointNativeStateMipChain_" + label.Replace(' ', '_'),
                    filterMode = targetReference.filterMode,
                    wrapModeU = targetReference.wrapModeU,
                    wrapModeV = targetReference.wrapModeV,
                    wrapModeW = targetReference.wrapModeW,
                    anisoLevel = targetReference.anisoLevel,
                    mipMapBias = targetReference.mipMapBias,
                    hideFlags = HideFlags.HideAndDontSave
                };
                destinationMipChain.SetPixels(readable.GetPixels(), 0);
                destinationMipChain.Apply(true, false);
                if (destinationMipChain.mipmapCount != targetReference.mipmapCount)
                {
                    throw new InvalidOperationException(
                        "Endpoint-native response state did not allocate the full destination mip chain for " + label + ".");
                }
                Texture2D result = destinationMipChain;
                destinationMipChain = null;
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgbWrite;
                ReleaseTemporaryRasterizationMeshes(copies);
                ReleaseExactSourceMipTextures(exactSourceMips);
                if (destinationMipChain != null)
                    UnityEngine.Object.DestroyImmediate(destinationMipChain);
                if (readable != null)
                    UnityEngine.Object.DestroyImmediate(readable);
                if (ownerMax != null)
                    UnityEngine.Object.DestroyImmediate(ownerMax);
                if (ownerMin != null)
                    UnityEngine.Object.DestroyImmediate(ownerMin);
                if (target != null)
                    UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static Texture2D WriteEndpointNativeDeltaTexture(
            string assetPath,
            Texture2D baselineColor,
            Texture2D fullColor,
            Texture2D baselineDirection,
            Texture2D fullDirection,
            bool directionalMoment,
            Texture2D endpointReference,
            bool[] ownedMip0Mask,
            string label,
            out DeltaQuantizationStats quantization)
        {
            quantization = null;
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null || File.Exists(AssetPathToPhysicalPath(assetPath)))
                throw new InvalidOperationException("Refusing to overwrite endpoint-native delta target: '" + assetPath + "'.");
            if (baselineColor == null || fullColor == null || baselineDirection == null || fullDirection == null ||
                endpointReference == null || baselineColor.width != fullColor.width ||
                baselineColor.height != fullColor.height || baselineColor.mipmapCount != fullColor.mipmapCount ||
                baselineDirection.width != fullDirection.width || baselineDirection.height != fullDirection.height ||
                baselineDirection.mipmapCount != fullDirection.mipmapCount ||
                baselineColor.width != baselineDirection.width || baselineColor.height != baselineDirection.height ||
                baselineColor.mipmapCount != baselineDirection.mipmapCount ||
                ownedMip0Mask == null || ownedMip0Mask.Length != baselineColor.width * baselineColor.height)
            {
                throw new InvalidOperationException("Endpoint-native delta sources or owned mask are inconsistent for " + label + ".");
            }

            Texture2D delta = null;
            try
            {
                delta = new Texture2D(
                    baselineColor.width,
                    baselineColor.height,
                    TextureFormat.RGBAHalf,
                    true,
                    true)
                {
                    name = Path.GetFileNameWithoutExtension(assetPath),
                    filterMode = endpointReference.filterMode,
                    wrapModeU = endpointReference.wrapModeU,
                    wrapModeV = endpointReference.wrapModeV,
                    wrapModeW = endpointReference.wrapModeW,
                    anisoLevel = endpointReference.anisoLevel,
                    mipMapBias = endpointReference.mipMapBias,
                    hideFlags = HideFlags.HideAndDontSave
                };
                for (int mip = 0; mip < baselineColor.mipmapCount; mip++)
                {
                    Color[] offColor = baselineColor.GetPixels(mip);
                    Color[] onColor = fullColor.GetPixels(mip);
                    Color[] offDirection = baselineDirection.GetPixels(mip);
                    Color[] onDirection = fullDirection.GetPixels(mip);
                    var values = new Color[offColor.Length];
                    for (int pixel = 0; pixel < values.Length; pixel++)
                    {
                        values[pixel] = directionalMoment
                            ? Subtract(DecodeDirectionalMoment(onColor[pixel], onDirection[pixel]),
                                DecodeDirectionalMoment(offColor[pixel], offDirection[pixel]))
                            : new Color(
                                onColor[pixel].r - offColor[pixel].r,
                                onColor[pixel].g - offColor[pixel].g,
                                onColor[pixel].b - offColor[pixel].b,
                                0f);
                        if (!IsFinite(values[pixel]) || MaxAbsoluteChannel(values[pixel]) > 65504f)
                        {
                            throw new InvalidOperationException(
                                "Endpoint-native signed delta exceeds RGBAHalf range for " + label +
                                " mip " + mip + " pixel " + pixel + ".");
                        }
                    }
                    delta.SetPixels(values, mip);
                }
                delta.Apply(false, false);
                quantization = MeasureOwnedMip0HalfQuantization(
                    delta,
                    baselineColor,
                    fullColor,
                    baselineDirection,
                    fullDirection,
                    directionalMoment,
                    ownedMip0Mask,
                    label);
                delta.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(delta, assetPath);
                EditorUtility.SetDirty(delta);
                AssetDatabase.SaveAssetIfDirty(delta);
                delta = null;
            }
            finally
            {
                if (delta != null)
                    UnityEngine.Object.DestroyImmediate(delta);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            Texture2D imported = LoadRequired<Texture2D>(assetPath);
            ValidateSampleTexture(imported, label + " imported signed delta", true);
            if (imported.format != TextureFormat.RGBAHalf || imported.width != endpointReference.width ||
                imported.height != endpointReference.height || imported.mipmapCount != endpointReference.mipmapCount)
            {
                throw new InvalidOperationException(
                    "Endpoint-native signed delta changed its RGBAHalf/dimension/mip contract for " + label + ".");
            }
            return imported;
        }

        private static DeltaQuantizationStats MeasureOwnedMip0HalfQuantization(
            Texture2D quantized,
            Texture2D baselineColor,
            Texture2D fullColor,
            Texture2D baselineDirection,
            Texture2D fullDirection,
            bool directionalMoment,
            bool[] ownedMask,
            string label)
        {
            Color[] actual = quantized.GetPixels(0);
            Color[] offColor = baselineColor.GetPixels(0);
            Color[] onColor = fullColor.GetPixels(0);
            Color[] offDirection = baselineDirection.GetPixels(0);
            Color[] onDirection = fullDirection.GetPixels(0);
            int channelCount = directionalMoment ? 4 : 3;
            var absoluteErrors = new List<float>();
            double squaredError = 0d;
            int ownedTexels = 0;
            for (int pixel = 0; pixel < actual.Length; pixel++)
            {
                if (!ownedMask[pixel])
                    continue;
                ownedTexels++;
                Color expected = directionalMoment
                    ? Subtract(DecodeDirectionalMoment(onColor[pixel], onDirection[pixel]),
                        DecodeDirectionalMoment(offColor[pixel], offDirection[pixel]))
                    : new Color(
                        onColor[pixel].r - offColor[pixel].r,
                        onColor[pixel].g - offColor[pixel].g,
                        onColor[pixel].b - offColor[pixel].b,
                        0f);
                for (int channel = 0; channel < channelCount; channel++)
                {
                    float expectedChannel = expected[channel];
                    float error = Mathf.Abs(actual[pixel][channel] - expectedChannel);
                    float allowed = Mathf.Max(0.00002f, Mathf.Abs(expectedChannel) * 0.0015f);
                    if (!IsFinite(error) || error > allowed)
                    {
                        throw new InvalidOperationException(
                            "Endpoint-native RGBAHalf quantization exceeded its analytic bound for " + label +
                            " at owned mip0 pixel " + pixel + " channel " + channel +
                            ". error=" + error.ToString("R", CultureInfo.InvariantCulture) +
                            " allowed=" + allowed.ToString("R", CultureInfo.InvariantCulture) + ".");
                    }
                    absoluteErrors.Add(error);
                    squaredError += (double)error * error;
                }
            }
            if (ownedTexels <= 0 || absoluteErrors.Count == 0)
                throw new InvalidOperationException("Endpoint-native delta has no owned mip0 quantization samples for " + label + ".");
            absoluteErrors.Sort();
            return new DeltaQuantizationStats
            {
                ownedMip0TexelCount = ownedTexels,
                channelSampleCount = absoluteErrors.Count,
                absoluteRmse = (float)Math.Sqrt(squaredError / absoluteErrors.Count),
                absoluteP95 = Percentile(absoluteErrors, 0.95f),
                absoluteP99 = Percentile(absoluteErrors, 0.99f),
                absoluteMax = absoluteErrors[absoluteErrors.Count - 1]
            };
        }

        private static void ValidateMatchingEndpointResponseStateCoverage(
            RasterizationStats baseline,
            RasterizationStats full,
            string label)
        {
            if (baseline == null || full == null || baseline.OperationCount != full.OperationCount ||
                baseline.OwnedCoreTexelCount != full.OwnedCoreTexelCount ||
                baseline.ImplicitZeroCoreTexelCount != full.ImplicitZeroCoreTexelCount ||
                baseline.OwnerCollisionCount != 0 || full.OwnerCollisionCount != 0 ||
                baseline.OwnedCoreMask == null || full.OwnedCoreMask == null ||
                baseline.OwnedCoreMask.Length != full.OwnedCoreMask.Length)
            {
                throw new InvalidOperationException(
                    "Endpoint-native Baseline/Full ownership differs for " + label + ".");
            }
            for (int i = 0; i < baseline.OwnedCoreMask.Length; i++)
            {
                if (baseline.OwnedCoreMask[i] != full.OwnedCoreMask[i])
                {
                    throw new InvalidOperationException(
                        "Endpoint-native Baseline/Full owned mask differs for " + label +
                        " at mip0 texel " + i + ".");
                }
            }
        }

        private static Color DecodeDirectionalMoment(Color color, Color direction)
        {
            Vector3 rgb = new Vector3(
                Mathf.Max(0f, color.r),
                Mathf.Max(0f, color.g),
                Mathf.Max(0f, color.b));
            float energy = Luminance(rgb);
            float reciprocalA = 1f / Mathf.Max(1e-5f, direction.a);
            return new Color(
                energy * (direction.r - 0.5f) * reciprocalA,
                energy * (direction.g - 0.5f) * reciprocalA,
                energy * (direction.b - 0.5f) * reciprocalA,
                energy * 0.5f * reciprocalA);
        }

        private static Color Subtract(Color left, Color right)
        {
            return new Color(
                left.r - right.r,
                left.g - right.g,
                left.b - right.b,
                left.a - right.a);
        }

        private static float MaxAbsoluteChannel(Color value)
        {
            return Mathf.Max(
                Mathf.Abs(value.r),
                Mathf.Abs(value.g),
                Mathf.Abs(value.b),
                Mathf.Abs(value.a));
        }

        private static float Percentile(List<float> sorted, float percentile)
        {
            if (sorted == null || sorted.Count == 0)
                throw new ArgumentException("Percentile source is empty.");
            int index = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Clamp01(percentile) * sorted.Count) - 1,
                0,
                sorted.Count - 1);
            return sorted[index];
        }

        private static RepackedAtlasSet WriteEndpointNativeResponseAtlasSet(
            string outputFolder,
            string label,
            BucketWork[] buckets,
            List<ChartCopyOperation> operations,
            Material copyMaterial,
            string payloadRoot,
            List<ArtifactFingerprint> artifacts,
            EndpointLayout destinationLayout)
        {
            EnsureFolder(outputFolder);
            var result = new RepackedAtlasSet(buckets.Length);
            for (int i = 0; i < buckets.Length; i++)
            {
                BucketWork bucket = buckets[i];
                Texture2D targetColor = destinationLayout == EndpointLayout.Power100
                    ? bucket.P100Color
                    : bucket.P0Color;
                Texture2D targetDirection = destinationLayout == EndpointLayout.Power100
                    ? bucket.P100Direction
                    : bucket.P0Direction;
                string stem = "LM" + bucket.LocalLightmapIndex.ToString("00", CultureInfo.InvariantCulture);
                string colorPath = outputFolder + "/" + stem + "_Color.asset";
                string directionPath = outputFolder + "/" + stem + "_Direction.asset";
                List<ChartCopyOperation> bucketOperations = operations
                    .Where(operation => operation.DestinationBucketIndex == bucket.BucketIndex)
                    .ToList();

                result.Colors[i] = WriteEndpointNativeResponseTexture(
                    colorPath,
                    targetColor,
                    bucketOperations,
                    false,
                    copyMaterial,
                    label + " color bucket " + i,
                    out result.ColorRasterization[i]);
                result.Directions[i] = WriteEndpointNativeResponseTexture(
                    directionPath,
                    targetDirection,
                    bucketOperations,
                    true,
                    copyMaterial,
                    label + " direction bucket " + i,
                    out result.DirectionRasterization[i]);
                ValidateMatchingRasterizationCoverage(
                    result.ColorRasterization[i],
                    result.DirectionRasterization[i],
                    label + " bucket " + i);
                result.ColorRelativePaths[i] = MakeRelativeAssetPath(payloadRoot, colorPath);
                result.DirectionRelativePaths[i] = MakeRelativeAssetPath(payloadRoot, directionPath);
                AddArtifactFingerprint(artifacts, payloadRoot, colorPath, label + "Color", result.Colors[i]);
                AddArtifactFingerprint(artifacts, payloadRoot, directionPath, label + "Direction", result.Directions[i]);
            }
            return result;
        }

        private static Texture2D WriteEndpointNativeResponseTexture(
            string assetPath,
            Texture2D targetReference,
            List<ChartCopyOperation> operations,
            bool direction,
            Material copyMaterial,
            string label,
            out RasterizationStats rasterization)
        {
            rasterization = null;
            if (targetReference == null || copyMaterial == null)
                throw new ArgumentNullException(targetReference == null ? nameof(targetReference) : nameof(copyMaterial));
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null || File.Exists(AssetPathToPhysicalPath(assetPath)))
                throw new InvalidOperationException("Refusing to overwrite non-empty endpoint-native target: '" + assetPath + "'.");

            List<ChartCopyOperation> copies = ResolveAndValidateTriangleOperations(operations, direction, label);
            RenderTexture previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture target = null;
            RenderTexture ownerMin = null;
            RenderTexture ownerMax = null;
            Texture2D readable = null;
            Texture2D destinationMipChain = null;
            Texture2D compressedTexture = null;
            var exactSourceMips = new Dictionary<Texture2D, Texture2D>();
            try
            {
                GL.sRGBWrite = false;
                target = new RenderTexture(
                    targetReference.width,
                    targetReference.height,
                    0,
                    RenderTextureFormat.ARGBHalf,
                    RenderTextureReadWrite.Linear)
                {
                    name = "__DPBB_EndpointNative_" + label.Replace(' ', '_'),
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave
                };
                target.Create();
                if (!target.IsCreated())
                    throw new InvalidOperationException("Unable to allocate endpoint-native response target for " + label + ".");

                ownerMin = CreateTriangleRasterTarget(
                    targetReference.width,
                    targetReference.height,
                    RenderTextureFormat.RFloat,
                    "EndpointNativeOwnerMin_" + label);
                ownerMax = CreateTriangleRasterTarget(
                    targetReference.width,
                    targetReference.height,
                    RenderTextureFormat.RFloat,
                    "EndpointNativeOwnerMax_" + label);
                PrepareRasterizationMeshes(copies, targetReference.width, targetReference.height);
                RenderOwnerExtrema(copies, ownerMin, ownerMax, copyMaterial);
                RasterizationStats level = ValidateTriangleOwnership(
                    copies,
                    ownerMin,
                    ownerMax,
                    label + " destination-native mip0");
                level.MipLevel = 0;
                level.MipLevels = new[] { level };
                rasterization = level;

                Graphics.SetRenderTarget(target);
                GL.Clear(
                    true,
                    true,
                    direction ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.black);
                RenderTriangleValues(
                    copies,
                    target,
                    direction,
                    copyMaterial,
                    0,
                    exactSourceMips,
                    label);
                DilateOnlyIntoUnownedTexels(target, ownerMax, copyMaterial, label + " destination-native mip0");

                RenderTexture.active = target;
                readable = new Texture2D(
                    targetReference.width,
                    targetReference.height,
                    direction ? TextureFormat.RGBA32 : TextureFormat.RGBAHalf,
                    false,
                    true)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                readable.ReadPixels(
                    new Rect(0f, 0f, targetReference.width, targetReference.height),
                    0,
                    0,
                    false);
                readable.Apply(false, false);

                destinationMipChain = new Texture2D(
                    targetReference.width,
                    targetReference.height,
                    direction ? TextureFormat.RGBA32 : TextureFormat.RGBAHalf,
                    true,
                    true)
                {
                    name = Path.GetFileNameWithoutExtension(assetPath),
                    filterMode = targetReference.filterMode,
                    wrapModeU = targetReference.wrapModeU,
                    wrapModeV = targetReference.wrapModeV,
                    wrapModeW = targetReference.wrapModeW,
                    anisoLevel = targetReference.anisoLevel,
                    mipMapBias = targetReference.mipMapBias,
                    hideFlags = HideFlags.HideAndDontSave
                };
                destinationMipChain.SetPixels(readable.GetPixels(), 0);
                // This is deliberately a destination-layout response mip chain, not a
                // cross-layout source-mip parity claim.  Generating from the collision-free
                // endpoint-native mip0 preserves that atlas's own filtering semantics.
                destinationMipChain.Apply(true, false);
                if (destinationMipChain.mipmapCount != targetReference.mipmapCount)
                {
                    throw new InvalidOperationException(
                        "Endpoint-native response did not allocate the full destination mip chain for " + label + ".");
                }

                compressedTexture = UnityEngine.Object.Instantiate(destinationMipChain);
                compressedTexture.name = Path.GetFileNameWithoutExtension(assetPath);
                compressedTexture.hideFlags = HideFlags.None;
                EditorUtility.CompressTexture(
                    compressedTexture,
                    direction ? TextureFormat.BC7 : TextureFormat.BC6H,
                    TextureCompressionQuality.Best);
                ValidateExplicitMipCompressionParity(
                    destinationMipChain,
                    compressedTexture,
                    direction,
                    label + " destination-native response",
                    true);

                AssetDatabase.CreateAsset(compressedTexture, assetPath);
                EditorUtility.SetDirty(compressedTexture);
                AssetDatabase.SaveAssetIfDirty(compressedTexture);
                compressedTexture = null;
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgbWrite;
                ReleaseTemporaryRasterizationMeshes(copies);
                ReleaseExactSourceMipTextures(exactSourceMips);
                if (compressedTexture != null)
                    UnityEngine.Object.DestroyImmediate(compressedTexture);
                if (destinationMipChain != null)
                    UnityEngine.Object.DestroyImmediate(destinationMipChain);
                if (readable != null)
                    UnityEngine.Object.DestroyImmediate(readable);
                if (ownerMax != null)
                    UnityEngine.Object.DestroyImmediate(ownerMax);
                if (ownerMin != null)
                    UnityEngine.Object.DestroyImmediate(ownerMin);
                if (target != null)
                    UnityEngine.Object.DestroyImmediate(target);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            Texture2D imported = LoadRequired<Texture2D>(assetPath);
            ValidateSampleTexture(imported, label + " imported endpoint-native response", direction);
            if (imported.width != targetReference.width || imported.height != targetReference.height ||
                imported.mipmapCount != targetReference.mipmapCount)
            {
                throw new InvalidOperationException(
                    "Endpoint-native response texture changed destination dimensions or mip count for " + label + ".");
            }
            return imported;
        }

        private static RepackedAtlasSet WriteRepackedAtlasSet(
            string outputFolder,
            string label,
            BucketWork[] buckets,
            List<ChartCopyOperation> operations,
            Material copyMaterial,
            string payloadRoot,
            List<ArtifactFingerprint> artifacts,
            bool requireCanonicalOrientationProof,
            EndpointLayout destinationLayout = EndpointLayout.Power0,
            bool preserveDirectionUncompressed = false)
        {
            EnsureFolder(outputFolder);
            var result = new RepackedAtlasSet(buckets.Length);
            for (int i = 0; i < buckets.Length; i++)
            {
                BucketWork bucket = buckets[i];
                string stem = "LM" + bucket.LocalLightmapIndex.ToString("00", CultureInfo.InvariantCulture);
                string colorPath = outputFolder + "/" + stem + "_Color.asset";
                string directionPath = outputFolder + "/" + stem + "_Direction.asset";
                List<ChartCopyOperation> bucketOperations = operations
                    .Where(operation => operation.DestinationBucketIndex == bucket.BucketIndex)
                    .ToList();
                Texture2D targetColor = destinationLayout == EndpointLayout.Power100
                    ? bucket.P100Color
                    : bucket.P0Color;
                Texture2D targetDirection = destinationLayout == EndpointLayout.Power100
                    ? bucket.P100Direction
                    : bucket.P0Direction;

                result.Colors[i] = WriteRepackedColorTextureWithFallback(
                    colorPath,
                    targetColor,
                    bucketOperations,
                    copyMaterial,
                    label + " color bucket " + i,
                    requireCanonicalOrientationProof,
                    out result.ColorRasterization[i]);
                result.Directions[i] = WriteRepackedTexture(
                    directionPath,
                    targetDirection,
                    bucketOperations,
                    true,
                    copyMaterial,
                    label + " direction bucket " + i,
                    false,
                    preserveDirectionUncompressed,
                    out result.DirectionRasterization[i]);
                ValidateMatchingRasterizationCoverage(
                    result.ColorRasterization[i],
                    result.DirectionRasterization[i],
                    label + " bucket " + i);
                result.ColorRelativePaths[i] = MakeRelativeAssetPath(payloadRoot, colorPath);
                result.DirectionRelativePaths[i] = MakeRelativeAssetPath(payloadRoot, directionPath);
                AddArtifactFingerprint(
                    artifacts,
                    payloadRoot,
                    colorPath,
                    label + "Color" + result.Colors[i].format,
                    result.Colors[i]);
                AddArtifactFingerprint(artifacts, payloadRoot, directionPath, label + "Direction", result.Directions[i]);
            }
            return result;
        }

        private static Texture2D WriteRepackedColorTextureWithFallback(
            string assetPath,
            Texture2D targetReference,
            List<ChartCopyOperation> operations,
            Material copyMaterial,
            string label,
            bool requireCanonicalOrientationProof,
            out RasterizationStats rasterization)
        {
            try
            {
                return WriteRepackedTexture(
                    assetPath,
                    targetReference,
                    operations,
                    false,
                    copyMaterial,
                    label,
                    requireCanonicalOrientationProof,
                    SelectBaseTransitionColorStorageFormat(true) == TextureFormat.RGBAHalf,
                    out rasterization);
            }
            catch (MipCompressionQualityException)
            {
                return WriteRepackedTexture(
                    assetPath,
                    targetReference,
                    operations,
                    false,
                    copyMaterial,
                    label + " RGBAHalf fallback",
                    requireCanonicalOrientationProof,
                    SelectBaseTransitionColorStorageFormat(false) == TextureFormat.RGBAHalf,
                    out rasterization);
            }
        }

        private static Texture2D WriteRepackedTexture(
            string assetPath,
            Texture2D targetReference,
            List<ChartCopyOperation> operations,
            bool direction,
            Material copyMaterial,
            string label,
            bool requireCanonicalOrientationProof,
            bool preserveUncompressed,
            out RasterizationStats rasterization)
        {
            rasterization = null;
            if (targetReference == null || copyMaterial == null)
                throw new ArgumentNullException(targetReference == null ? nameof(targetReference) : nameof(copyMaterial));
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null || File.Exists(AssetPathToPhysicalPath(assetPath)))
                throw new InvalidOperationException("Refusing to overwrite non-empty staging chart target: '" + assetPath + "'.");
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat))
            {
                throw new InvalidOperationException(
                    "ARGBHalf and RFloat targets are required for triangle-raster chart repacking; owner min/max semantics are checked by readback.");
            }

            List<ChartCopyOperation> copies = ResolveAndValidateTriangleOperations(
                operations,
                direction,
                label);
            RenderTexture previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture target = null;
            RenderTexture ownerMin = null;
            RenderTexture ownerMax = null;
            Texture2D readable = null;
            Texture2D destinationMipChain = null;
            Texture2D compressedTexture = null;
            var exactSourceMips = new Dictionary<Texture2D, Texture2D>();
            try
            {
                GL.sRGBWrite = false;
                target = new RenderTexture(
                    targetReference.width,
                    targetReference.height,
                    0,
                    RenderTextureFormat.ARGBHalf,
                    RenderTextureReadWrite.Linear)
                {
                    name = "__DPBB_" + label.Replace(' ', '_') + "_Mip0",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave
                };
                target.Create();
                if (!target.IsCreated())
                {
                    throw new InvalidOperationException(
                        "Unable to allocate an ARGBHalf destination mip0 chart target for " + label + ".");
                }

                ownerMin = CreateTriangleRasterTarget(
                    targetReference.width,
                    targetReference.height,
                    RenderTextureFormat.RFloat,
                    "OwnerMin_" + label + "_Mip0");
                ownerMax = CreateTriangleRasterTarget(
                    targetReference.width,
                    targetReference.height,
                    RenderTextureFormat.RFloat,
                    "OwnerMax_" + label + "_Mip0");
                PrepareRasterizationMeshes(copies, targetReference.width, targetReference.height);
                RenderOwnerExtrema(copies, ownerMin, ownerMax, copyMaterial);
                RasterizationStats mip0 = ValidateTriangleOwnership(
                    copies,
                    ownerMin,
                    ownerMax,
                    label + " collision-free destination mip0");
                mip0.MipLevel = 0;

                if (requireCanonicalOrientationProof)
                {
                    Texture2D exactCanonicalMip0 = GetExactSourceMipTexture(
                        targetReference,
                        0,
                        exactSourceMips,
                        label + " canonical orientation source mip0");
                    ValidateCanonicalOrientationProof(
                        copies,
                        exactCanonicalMip0,
                        ownerMax,
                        copyMaterial,
                        label + " destination mip0",
                        mip0);
                }

                Graphics.SetRenderTarget(target);
                GL.Clear(
                    true,
                    true,
                    direction ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.black);
                RenderTriangleValues(
                    copies,
                    target,
                    direction,
                    copyMaterial,
                    0,
                    exactSourceMips,
                    label);
                DilateOnlyIntoUnownedTexels(
                    target,
                    ownerMax,
                    copyMaterial,
                    label + " destination mip0");

                RenderTexture.active = target;
                readable = new Texture2D(
                    targetReference.width,
                    targetReference.height,
                    direction ? TextureFormat.RGBA32 : TextureFormat.RGBAHalf,
                    false,
                    true)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                readable.ReadPixels(
                    new Rect(0f, 0f, targetReference.width, targetReference.height),
                    0,
                    0,
                    false);
                readable.Apply(false, false);

                destinationMipChain = new Texture2D(
                    targetReference.width,
                    targetReference.height,
                    direction ? TextureFormat.RGBA32 : TextureFormat.RGBAHalf,
                    true,
                    true)
                {
                    name = Path.GetFileNameWithoutExtension(assetPath),
                    filterMode = targetReference.filterMode,
                    wrapModeU = targetReference.wrapModeU,
                    wrapModeV = targetReference.wrapModeV,
                    wrapModeW = targetReference.wrapModeW,
                    anisoLevel = targetReference.anisoLevel,
                    mipMapBias = targetReference.mipMapBias,
                    hideFlags = HideFlags.HideAndDontSave
                };
                destinationMipChain.SetPixels(readable.GetPixels(), 0);
                // Only mip0 owns chart-raster evidence. Apply generates lower levels from
                // that collision-free destination layout; no lower-mip chart owner is claimed.
                destinationMipChain.Apply(true, false);
                ValidateGeneratedDestinationMipChain(
                    destinationMipChain,
                    targetReference,
                    direction,
                    label + " repacked atlas");
                rasterization = BuildGeneratedMipRasterization(
                    mip0,
                    targetReference.width,
                    targetReference.height,
                    targetReference.mipmapCount,
                    label);

                compressedTexture = UnityEngine.Object.Instantiate(destinationMipChain);
                compressedTexture.name = Path.GetFileNameWithoutExtension(assetPath);
                compressedTexture.hideFlags = HideFlags.None;
                compressedTexture.filterMode = targetReference.filterMode;
                compressedTexture.wrapModeU = targetReference.wrapModeU;
                compressedTexture.wrapModeV = targetReference.wrapModeV;
                compressedTexture.wrapModeW = targetReference.wrapModeW;
                compressedTexture.anisoLevel = targetReference.anisoLevel;
                compressedTexture.mipMapBias = targetReference.mipMapBias;
                if (!preserveUncompressed)
                {
                    EditorUtility.CompressTexture(
                        compressedTexture,
                        direction ? TextureFormat.BC7 : TextureFormat.BC6H,
                        TextureCompressionQuality.Best);
                    ValidateExplicitMipCompressionParity(
                        destinationMipChain,
                        compressedTexture,
                        direction,
                        label);
                }
                else if ((direction && compressedTexture.format != TextureFormat.RGBA32) ||
                         (!direction && compressedTexture.format != TextureFormat.RGBAHalf))
                {
                    throw new InvalidOperationException(
                        "Uncompressed opposite-base texture has the wrong linear format for " + label + ".");
                }

                AssetDatabase.CreateAsset(compressedTexture, assetPath);
                EditorUtility.SetDirty(compressedTexture);
                AssetDatabase.SaveAssetIfDirty(compressedTexture);
                compressedTexture = null;
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgbWrite;
                ReleaseTemporaryRasterizationMeshes(copies);
                ReleaseExactSourceMipTextures(exactSourceMips);
                if (compressedTexture != null)
                    UnityEngine.Object.DestroyImmediate(compressedTexture);
                if (destinationMipChain != null)
                    UnityEngine.Object.DestroyImmediate(destinationMipChain);
                if (readable != null)
                    UnityEngine.Object.DestroyImmediate(readable);
                if (ownerMax != null)
                    UnityEngine.Object.DestroyImmediate(ownerMax);
                if (ownerMin != null)
                    UnityEngine.Object.DestroyImmediate(ownerMin);
                if (target != null)
                    UnityEngine.Object.DestroyImmediate(target);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            Texture2D imported = LoadRequired<Texture2D>(assetPath);
            ValidateSampleTexture(imported, label + " imported", direction);
            if (imported.width != targetReference.width || imported.height != targetReference.height ||
                imported.mipmapCount != targetReference.mipmapCount)
            {
                throw new InvalidOperationException(
                    "Generated chart atlas did not preserve the full destination mip chain for " + label +
                    ". expected=" + targetReference.mipmapCount + " actual=" + imported.mipmapCount + ".");
            }
            return imported;
        }

        private static Texture2D GetExactSourceMipTexture(
            Texture2D source,
            int mip,
            Dictionary<Texture2D, Texture2D> cache,
            string label)
        {
            if (source == null || cache == null || mip < 0 || mip >= source.mipmapCount)
                throw new InvalidOperationException("Exact source-mip request is invalid for " + label + ".");
            if (cache.TryGetValue(source, out Texture2D existing))
                return existing;
            if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
            {
                throw new InvalidOperationException(
                    "Exact compressed source-mip copies are unsupported for " + label + ".");
            }

            int width = Mathf.Max(1, source.width >> mip);
            int height = Mathf.Max(1, source.height >> mip);
            var result = new Texture2D(width, height, source.format, false, true)
            {
                name = "__DPBB_SourceMip_" + mip + "_" + source.name,
                filterMode = source.filterMode,
                wrapModeU = source.wrapModeU,
                wrapModeV = source.wrapModeV,
                wrapModeW = source.wrapModeW,
                anisoLevel = source.anisoLevel,
                mipMapBias = 0f,
                hideFlags = HideFlags.HideAndDontSave
            };
            Graphics.CopyTexture(source, 0, mip, result, 0, 0);
            cache.Add(source, result);
            return result;
        }

        private static void ReleaseExactSourceMipTextures(Dictionary<Texture2D, Texture2D> cache)
        {
            if (cache == null)
                return;
            foreach (Texture2D texture in cache.Values)
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
            cache.Clear();
        }

        private static RasterizationStats BuildGeneratedMipRasterization(
            RasterizationStats mip0,
            int width,
            int height,
            int mipCount,
            string label)
        {
            int baseTexelCount = width * height;
            if (mip0 == null || width <= 0 || height <= 0 ||
                mipCount != CalculateFullMipCount(width, height) ||
                mip0.MipLevel != 0 || mip0.OperationCount <= 0 || mip0.OwnerCollisionCount != 0 ||
                mip0.OwnedCoreMask == null || mip0.OwnedCoreMask.Length != baseTexelCount ||
                mip0.ImplicitZeroCoreMask == null || mip0.ImplicitZeroCoreMask.Length != baseTexelCount ||
                mip0.SampledCoverageMask == null || mip0.SampledCoverageMask.Length != baseTexelCount ||
                mip0.OwnedCoreTexelCount != CountTrue(mip0.OwnedCoreMask) ||
                mip0.ImplicitZeroCoreTexelCount != CountTrue(mip0.ImplicitZeroCoreMask))
            {
                throw new InvalidOperationException(
                    "Collision-free destination mip0 evidence is incomplete for " + label + ".");
            }

            for (int texel = 0; texel < baseTexelCount; texel++)
            {
                if ((mip0.ImplicitZeroCoreMask[texel] && !mip0.OwnedCoreMask[texel]) ||
                    (mip0.OwnedCoreMask[texel] && !mip0.SampledCoverageMask[texel]))
                {
                    throw new InvalidOperationException(
                        "Mip0 core/implicit-zero/sampled coverage is inconsistent for " +
                        label + " texel " + texel + ".");
                }
            }

            bool[][] ownedCoverage = BuildConservativeMipMasks(
                mip0.OwnedCoreMask,
                width,
                height,
                mipCount,
                label + " owned coverage");
            bool[][] implicitZeroCoverage = BuildConservativeMipMasks(
                mip0.ImplicitZeroCoreMask,
                width,
                height,
                mipCount,
                label + " implicit-zero coverage");
            bool[][] sampledCoverage = BuildConservativeMipMasks(
                mip0.SampledCoverageMask,
                width,
                height,
                mipCount,
                label + " sampled dilation coverage");
            var levels = new RasterizationStats[mipCount];
            for (int mip = 0; mip < mipCount; mip++)
            {
                bool[] ownedMask = ownedCoverage[mip];
                bool[] implicitZeroMask = implicitZeroCoverage[mip];
                bool[] sampledMask = sampledCoverage[mip];
                int ownedCount = CountTrue(ownedMask);
                int implicitZeroCount = CountTrue(implicitZeroMask);
                if (ownedCount <= 0 || implicitZeroCount > ownedCount)
                {
                    throw new InvalidOperationException(
                        "Generated mip coverage is invalid for " + label + " mip " + mip + ".");
                }
                for (int texel = 0; texel < ownedMask.Length; texel++)
                {
                    if ((implicitZeroMask[texel] && !ownedMask[texel]) ||
                        (ownedMask[texel] && !sampledMask[texel]))
                    {
                        throw new InvalidOperationException(
                            "Generated core/implicit-zero/sampled coverage is inconsistent for " + label +
                            " mip " + mip + " texel " + texel + ".");
                    }
                }

                levels[mip] = new RasterizationStats
                {
                    MipLevel = mip,
                    OperationCount = mip0.OperationCount,
                    OwnedCoreTexelCount = ownedCount,
                    ImplicitZeroCoreTexelCount = implicitZeroCount,
                    // Lower levels are conservative OR coverage, not rerasterized owner claims.
                    OwnerCollisionCount = 0,
                    CanonicalOrientationProofSampledTexelCount = mip == 0
                        ? mip0.CanonicalOrientationProofSampledTexelCount
                        : 0,
                    CanonicalOrientationProofMaxAbsoluteChannelError = mip == 0
                        ? mip0.CanonicalOrientationProofMaxAbsoluteChannelError
                        : 0f,
                    OwnedCoreMask = ownedMask,
                    ImplicitZeroCoreMask = implicitZeroMask,
                    SampledCoverageMask = sampledMask
                };
            }

            RasterizationStats baseLevel = levels[0];
            return new RasterizationStats
            {
                MipLevel = 0,
                OperationCount = baseLevel.OperationCount,
                OwnedCoreTexelCount = baseLevel.OwnedCoreTexelCount,
                ImplicitZeroCoreTexelCount = baseLevel.ImplicitZeroCoreTexelCount,
                OwnerCollisionCount = 0,
                CanonicalOrientationProofSampledTexelCount =
                    baseLevel.CanonicalOrientationProofSampledTexelCount,
                CanonicalOrientationProofMaxAbsoluteChannelError =
                    baseLevel.CanonicalOrientationProofMaxAbsoluteChannelError,
                MipLevels = levels,
                OwnedCoreMask = baseLevel.OwnedCoreMask,
                ImplicitZeroCoreMask = baseLevel.ImplicitZeroCoreMask,
                SampledCoverageMask = baseLevel.SampledCoverageMask
            };
        }

        internal static bool[][] BuildSampledCoverageMipMasksForTest(
            bool[] mip0CoreMask,
            int width,
            int height,
            int mipCount)
        {
            bool[] sampledMip0 = DilateCoverageMask8Neighbour(
                mip0CoreMask,
                width,
                height,
                ChartGutterPixels);
            return BuildConservativeMipMasks(sampledMip0, width, height, mipCount, "sampled test seam");
        }

        private static bool[] DilateCoverageMask8Neighbour(
            bool[] source,
            int width,
            int height,
            int steps)
        {
            if (source == null || width <= 0 || height <= 0 || source.Length != width * height || steps < 0)
                throw new InvalidOperationException("Eight-neighbour dilation source dimensions are invalid.");
            bool[] current = (bool[])source.Clone();
            for (int step = 0; step < steps; step++)
            {
                bool[] next = (bool[])current.Clone();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int destination = y * width + x;
                        if (current[destination])
                            continue;
                        for (int offsetY = -1; offsetY <= 1 && !next[destination]; offsetY++)
                        {
                            int sourceY = Mathf.Clamp(y + offsetY, 0, height - 1);
                            for (int offsetX = -1; offsetX <= 1; offsetX++)
                            {
                                if (offsetX == 0 && offsetY == 0)
                                    continue;
                                int sourceX = Mathf.Clamp(x + offsetX, 0, width - 1);
                                if (!current[sourceY * width + sourceX])
                                    continue;
                                next[destination] = true;
                                break;
                            }
                        }
                    }
                }
                current = next;
            }
            return current;
        }

        internal static bool[][] BuildConservativeMipMasksForTest(
            bool[] mip0Mask,
            int width,
            int height,
            int mipCount)
        {
            return BuildConservativeMipMasks(mip0Mask, width, height, mipCount, "test seam");
        }

        private static bool[][] BuildConservativeMipMasks(
            bool[] mip0Mask,
            int width,
            int height,
            int mipCount,
            string label)
        {
            if (mip0Mask == null || width <= 0 || height <= 0 ||
                mip0Mask.Length != width * height || mipCount != CalculateFullMipCount(width, height))
            {
                throw new InvalidOperationException("Mip0 coverage mask dimensions are invalid for " + label + ".");
            }

            var result = new bool[mipCount][];
            result[0] = (bool[])mip0Mask.Clone();
            int sourceWidth = width;
            int sourceHeight = height;
            for (int mip = 1; mip < mipCount; mip++)
            {
                result[mip] = DownsampleMask2x2Or(
                    result[mip - 1],
                    sourceWidth,
                    sourceHeight,
                    out int destinationWidth,
                    out int destinationHeight);
                sourceWidth = destinationWidth;
                sourceHeight = destinationHeight;
            }
            return result;
        }

        private static bool[] DownsampleMask2x2Or(
            bool[] source,
            int sourceWidth,
            int sourceHeight,
            out int destinationWidth,
            out int destinationHeight)
        {
            if (source == null || sourceWidth <= 0 || sourceHeight <= 0 ||
                source.Length != sourceWidth * sourceHeight)
            {
                throw new InvalidOperationException("2x2 OR-downsample source dimensions are invalid.");
            }
            destinationWidth = Mathf.Max(1, sourceWidth >> 1);
            destinationHeight = Mathf.Max(1, sourceHeight >> 1);
            var result = new bool[destinationWidth * destinationHeight];
            for (int y = 0; y < destinationHeight; y++)
            {
                int y0 = Mathf.Min(sourceHeight - 1, y * 2);
                int y1 = Mathf.Min(sourceHeight - 1, y0 + 1);
                for (int x = 0; x < destinationWidth; x++)
                {
                    int x0 = Mathf.Min(sourceWidth - 1, x * 2);
                    int x1 = Mathf.Min(sourceWidth - 1, x0 + 1);
                    result[y * destinationWidth + x] =
                        source[y0 * sourceWidth + x0] ||
                        source[y0 * sourceWidth + x1] ||
                        source[y1 * sourceWidth + x0] ||
                        source[y1 * sourceWidth + x1];
                }
            }
            return result;
        }

        private static int CountTrue(bool[] values)
        {
            int result = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i])
                    result++;
            }
            return result;
        }

        private static void ValidateGeneratedDestinationMipChain(
            Texture2D texture,
            Texture2D destinationReference,
            bool byteQuantized,
            string label)
        {
            if (texture == null || destinationReference == null ||
                texture.width != destinationReference.width ||
                texture.height != destinationReference.height ||
                texture.mipmapCount != destinationReference.mipmapCount)
            {
                throw new InvalidOperationException(
                    "Generated destination mip chain changed dimensions or mip cardinality for " + label + ".");
            }

            var mipPixels = new Color[texture.mipmapCount][];
            for (int mip = 0; mip < texture.mipmapCount; mip++)
                mipPixels[mip] = texture.GetPixels(mip);
            ValidateGeneratedMipChainPixels(
                texture.width,
                texture.height,
                mipPixels,
                byteQuantized,
                label);
        }

        internal static void ValidateGeneratedMipChainForTest(
            int width,
            int height,
            Color[][] mipPixels,
            bool byteQuantized)
        {
            ValidateGeneratedMipChainPixels(width, height, mipPixels, byteQuantized, "test seam");
        }

        private static void ValidateGeneratedMipChainPixels(
            int width,
            int height,
            Color[][] mipPixels,
            bool byteQuantized,
            string label)
        {
            int expectedMipCount = CalculateFullMipCount(width, height);
            if (width <= 0 || height <= 0 || mipPixels == null || mipPixels.Length != expectedMipCount)
            {
                throw new InvalidOperationException(
                    "Generated mip chain dimensions or cardinality are invalid for " + label + ".");
            }

            for (int mip = 0; mip < mipPixels.Length; mip++)
            {
                int levelWidth = Mathf.Max(1, width >> mip);
                int levelHeight = Mathf.Max(1, height >> mip);
                Color[] actual = mipPixels[mip];
                if (actual == null || actual.Length != levelWidth * levelHeight)
                {
                    throw new InvalidOperationException(
                        "Generated mip readback dimensions are invalid for " + label + " mip " + mip + ".");
                }
                for (int pixel = 0; pixel < actual.Length; pixel++)
                {
                    if (!IsFinite(actual[pixel]))
                    {
                        throw new InvalidOperationException(
                            "Generated mip contains a non-finite texel for " + label +
                            " mip " + mip + " texel " + pixel + ".");
                    }
                }

                if (mip == 0)
                    continue;
                Color[] expected = BoxDownsample2x2(
                    mipPixels[mip - 1],
                    Mathf.Max(1, width >> (mip - 1)),
                    Mathf.Max(1, height >> (mip - 1)),
                    out int expectedWidth,
                    out int expectedHeight);
                if (expectedWidth != levelWidth || expectedHeight != levelHeight || expected.Length != actual.Length)
                {
                    throw new InvalidOperationException(
                        "Independent box-downsample dimensions disagree for " + label + " mip " + mip + ".");
                }
                for (int pixel = 0; pixel < actual.Length; pixel++)
                {
                    for (int channel = 0; channel < 4; channel++)
                    {
                        float expectedChannel = expected[pixel][channel];
                        float actualChannel = actual[pixel][channel];
                        float tolerance = byteQuantized
                            ? GeneratedByteMipMaxAbsoluteError
                            : Mathf.Max(
                                GeneratedHalfMipMinimumAbsoluteError,
                                Mathf.Abs(expectedChannel) * GeneratedHalfMipMaxRelativeError);
                        float error = Mathf.Abs(expectedChannel - actualChannel);
                        if (!IsFinite(error) || error > tolerance)
                        {
                            throw new InvalidOperationException(
                                "Generated mip differs from independent 2x2 box downsampling for " + label +
                                " mip " + mip + " texel " + pixel + " channel " + channel +
                                ". expected=" + expectedChannel.ToString("R", CultureInfo.InvariantCulture) +
                                " actual=" + actualChannel.ToString("R", CultureInfo.InvariantCulture) +
                                " error=" + error.ToString("R", CultureInfo.InvariantCulture) +
                                " tolerance=" + tolerance.ToString("R", CultureInfo.InvariantCulture) + ".");
                        }
                    }
                }
            }
        }

        private static Color[] BoxDownsample2x2(
            Color[] source,
            int sourceWidth,
            int sourceHeight,
            out int destinationWidth,
            out int destinationHeight)
        {
            if (source == null || sourceWidth <= 0 || sourceHeight <= 0 ||
                source.Length != sourceWidth * sourceHeight)
            {
                throw new InvalidOperationException("2x2 box-downsample source dimensions are invalid.");
            }
            destinationWidth = Mathf.Max(1, sourceWidth >> 1);
            destinationHeight = Mathf.Max(1, sourceHeight >> 1);
            var result = new Color[destinationWidth * destinationHeight];
            for (int y = 0; y < destinationHeight; y++)
            {
                int y0 = Mathf.Min(sourceHeight - 1, y * 2);
                int y1 = Mathf.Min(sourceHeight - 1, y0 + 1);
                for (int x = 0; x < destinationWidth; x++)
                {
                    int x0 = Mathf.Min(sourceWidth - 1, x * 2);
                    int x1 = Mathf.Min(sourceWidth - 1, x0 + 1);
                    result[y * destinationWidth + x] = (
                        source[y0 * sourceWidth + x0] +
                        source[y0 * sourceWidth + x1] +
                        source[y1 * sourceWidth + x0] +
                        source[y1 * sourceWidth + x1]) * 0.25f;
                }
            }
            return result;
        }

        private static int CalculateFullMipCount(int width, int height)
        {
            if (width <= 0 || height <= 0)
                return 0;
            int result = 1;
            while (width > 1 || height > 1)
            {
                width = Mathf.Max(1, width >> 1);
                height = Mathf.Max(1, height >> 1);
                result++;
            }
            return result;
        }

        private static void ValidateExplicitMipCompressionParity(
            Texture2D reference,
            Texture2D compressed,
            bool direction,
            string label,
            bool lowEnergyResponse = false)
        {
            if (reference == null || compressed == null || reference.width != compressed.width ||
                reference.height != compressed.height || reference.mipmapCount != compressed.mipmapCount)
            {
                throw new InvalidOperationException(
                    "Explicit mip compression changed dimensions or mip cardinality for " + label + ".");
            }

            int channelCount = direction ? 4 : 3;
            for (int mip = 0; mip < reference.mipmapCount; mip++)
            {
                Color[] expected = reference.GetPixels(mip);
                Color[] actual = compressed.GetPixels(mip);
                if (expected.Length == 0 || expected.Length != actual.Length)
                    throw new InvalidOperationException("Explicit mip readback is incomplete for " + label + " mip " + mip + ".");

                double squaredError = 0d;
                double squaredReference = 0d;
                float maxAbsoluteError = 0f;
                for (int i = 0; i < expected.Length; i++)
                {
                    AccumulateMipCompressionChannel(
                        expected[i].r,
                        actual[i].r,
                        ref squaredError,
                        ref squaredReference,
                        ref maxAbsoluteError);
                    AccumulateMipCompressionChannel(
                        expected[i].g,
                        actual[i].g,
                        ref squaredError,
                        ref squaredReference,
                        ref maxAbsoluteError);
                    AccumulateMipCompressionChannel(
                        expected[i].b,
                        actual[i].b,
                        ref squaredError,
                        ref squaredReference,
                        ref maxAbsoluteError);
                    if (direction)
                    {
                        AccumulateMipCompressionChannel(
                            expected[i].a,
                            actual[i].a,
                            ref squaredError,
                            ref squaredReference,
                            ref maxAbsoluteError);
                    }
                }

                double absoluteRmse = Math.Sqrt(squaredError / (expected.Length * channelCount));
                double relativeRmse = Math.Sqrt(squaredError / Math.Max(1e-12d, squaredReference));
                bool failed = direction
                    ? absoluteRmse > DirectionMipCompressionMaxAbsoluteRmse ||
                      maxAbsoluteError > (lowEnergyResponse
                          ? ResponseDirectionMipCompressionMaxAbsoluteError
                          : DirectionMipCompressionMaxAbsoluteError)
                    : lowEnergyResponse
                        ? absoluteRmse > ResponseColorMipCompressionMaxAbsoluteRmse ||
                          (relativeRmse > ResponseColorMipCompressionMaxRelativeRmse &&
                           absoluteRmse > ResponseColorMipCompressionRelativeErrorFloor) ||
                          maxAbsoluteError > ResponseColorMipCompressionMaxAbsoluteError
                        : !IsBaseColorMipCompressionWithinBounds(
                            reference.width,
                            reference.height,
                            mip,
                            absoluteRmse,
                            relativeRmse,
                            maxAbsoluteError);
                if (failed)
                {
                    throw new MipCompressionQualityException(
                        "Generated mip compression exceeded its bound for " + label +
                        " mip " + mip + ". absoluteRmse=" +
                        absoluteRmse.ToString("R", CultureInfo.InvariantCulture) +
                        " relativeRmse=" + relativeRmse.ToString("R", CultureInfo.InvariantCulture) +
                        " maxAbsoluteError=" + maxAbsoluteError.ToString("R", CultureInfo.InvariantCulture) + ".");
                }
            }
        }

        internal static bool IsBaseColorMipCompressionWithinBoundsForTest(
            int width,
            int height,
            int mip,
            double absoluteRmse,
            double relativeRmse,
            float maxAbsoluteError)
        {
            return IsBaseColorMipCompressionWithinBounds(
                width,
                height,
                mip,
                absoluteRmse,
                relativeRmse,
                maxAbsoluteError);
        }

        private static bool IsBaseColorMipCompressionWithinBounds(
            int width,
            int height,
            int mip,
            double absoluteRmse,
            double relativeRmse,
            float maxAbsoluteError)
        {
            if (width <= 0 || height <= 0 || mip < 0 ||
                double.IsNaN(absoluteRmse) || double.IsInfinity(absoluteRmse) || absoluteRmse < 0d ||
                double.IsNaN(relativeRmse) || double.IsInfinity(relativeRmse) || relativeRmse < 0d ||
                !IsFinite(maxAbsoluteError) || maxAbsoluteError < 0f)
            {
                return false;
            }

            int levelWidth = Mathf.Max(1, width >> mip);
            int levelHeight = Mathf.Max(1, height >> mip);
            bool coarseMip = mip > 0 &&
                             Mathf.Max(levelWidth, levelHeight) <= CoarseBaseColorMipMaximumDimension;
            bool boundedLowEnergyMip = mip > 0 &&
                                       absoluteRmse <= LowEnergyBaseColorMipMaxAbsoluteRmse &&
                                       maxAbsoluteError <= LowEnergyBaseColorMipMaxAbsoluteError;
            bool boundedCoarseMip = coarseMip &&
                                    absoluteRmse <= CoarseBaseColorMipMaxAbsoluteRmse &&
                                    maxAbsoluteError <= CoarseBaseColorMipMaxAbsoluteError;
            bool relativeFailure = relativeRmse > ColorMipCompressionMaxRelativeRmse &&
                                   !boundedLowEnergyMip &&
                                   !boundedCoarseMip;
            return !relativeFailure && maxAbsoluteError <= ColorMipCompressionMaxAbsoluteError;
        }

        private static void AccumulateMipCompressionChannel(
            float expected,
            float actual,
            ref double squaredError,
            ref double squaredReference,
            ref float maxAbsoluteError)
        {
            if (!IsFinite(expected) || !IsFinite(actual))
                throw new InvalidOperationException("Explicit mip compression produced a non-finite channel.");
            float difference = actual - expected;
            squaredError += (double)difference * difference;
            squaredReference += (double)expected * expected;
            maxAbsoluteError = Mathf.Max(maxAbsoluteError, Mathf.Abs(difference));
        }

        private static List<ChartCopyOperation> ResolveAndValidateTriangleOperations(
            List<ChartCopyOperation> operations,
            bool direction,
            string label)
        {
            if (operations == null)
                throw new ArgumentNullException(nameof(operations));
            var result = new List<ChartCopyOperation>(operations.Count);
            for (int i = 0; i < operations.Count; i++)
            {
                ChartCopyOperation operation = operations[i];
                if (operation == null || operation.ProductionMesh == null ||
                    !IsFinite(operation.DestinationScaleOffset) ||
                    operation.DestinationScaleOffset.x <= 0f || operation.DestinationScaleOffset.y <= 0f)
                {
                    throw new InvalidOperationException(
                        "Triangle-raster operation has no mesh or valid P0 destination ST: '" +
                        (operation != null ? operation.Label : "null") + "'.");
                }
                if (operation.UsesImplicitZeroLightmapUv)
                    ValidateImplicitZeroLightmapOperation(operation.ProductionMesh, operation.Label);
                else
                    ValidateTriangleRasterMesh(operation.ProductionMesh, operation.Label);
                if (operation.HasSource)
                {
                    Texture2D source = direction ? operation.SourceDirection : operation.SourceColor;
                    if (source == null || !IsFinite(operation.SourceScaleOffset) ||
                        operation.SourceScaleOffset.x <= 0f || operation.SourceScaleOffset.y <= 0f)
                    {
                        throw new InvalidOperationException(
                            "Triangle-raster operation has an incomplete source texture/ST: '" + operation.Label + "'.");
                    }
                    ValidateSampleTexture(source, operation.Label + (direction ? " direction" : " color"), direction);
                }
                else if (operation.SourceColor != null || operation.SourceDirection != null)
                {
                    throw new InvalidOperationException(
                        "Neutral unmatched triangle-raster operation must not carry only one source texture: '" +
                        operation.Label + "'.");
                }
                result.Add(operation);
            }
            return result;
        }

        private static void ValidateImplicitZeroLightmapOperation(Mesh mesh, string label)
        {
            if (mesh == null || mesh.vertexCount <= 0)
                throw new InvalidOperationException("Implicit-zero lightmap UV mesh is empty for '" + label + "'.");
            Vector2[] uv2;
            try
            {
                uv2 = mesh.uv2;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Implicit-zero lightmap UV mesh cannot be inspected for '" + label + "'.", exception);
            }
            if ((uv2 != null && uv2.Length != 0) || mesh.HasVertexAttribute(VertexAttribute.TexCoord1))
            {
                throw new InvalidOperationException(
                    "Implicit-zero lightmap UV operation is inconsistent with the current mesh TEXCOORD1 contract for '" +
                    label + "'.");
            }
        }

        private static void PrepareRasterizationMeshes(
            List<ChartCopyOperation> operations,
            int targetWidth,
            int targetHeight)
        {
            if (operations == null || targetWidth <= 0 || targetHeight <= 0)
                throw new ArgumentException("Triangle-raster operation target dimensions are invalid.");
            for (int i = 0; i < operations.Count; i++)
            {
                ChartCopyOperation operation = operations[i];
                if (operation.UsesImplicitZeroLightmapUv)
                {
                    if (operation.TemporaryRasterMesh != null)
                        throw new InvalidOperationException("Implicit-zero footprint mesh was unexpectedly retained for '" + operation.Label + "'.");
                    operation.TemporaryRasterMesh = CreateImplicitZeroFootprintMesh(operation, targetWidth, targetHeight);
                }
                ValidateTriangleRasterMesh(operation.RasterMesh, operation.Label);
            }
        }

        private static void ReleaseTemporaryRasterizationMeshes(List<ChartCopyOperation> operations)
        {
            if (operations == null)
                return;
            for (int i = 0; i < operations.Count; i++)
            {
                ChartCopyOperation operation = operations[i];
                if (operation != null && operation.TemporaryRasterMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(operation.TemporaryRasterMesh);
                    operation.TemporaryRasterMesh = null;
                }
            }
        }

        private static Mesh CreateImplicitZeroFootprintMesh(
            ChartCopyOperation operation,
            int targetWidth,
            int targetHeight)
        {
            // With no TEXCOORD1 attribute Unity supplies (0,0), so every vertex
            // reads the sole lightmap point ST.zw. Reserve exactly the texel containing
            // that point; a full chart rectangle or UV0 inference would be incorrect.
            int pixelX = GetPointSampleTexel(operation.DestinationScaleOffset.z, targetWidth);
            int pixelY = GetPointSampleTexel(operation.DestinationScaleOffset.w, targetHeight);
            float minU = pixelX / (float)targetWidth;
            float maxU = (pixelX + 1) / (float)targetWidth;
            float minV = pixelY / (float)targetHeight;
            float maxV = (pixelY + 1) / (float)targetHeight;
            Vector4 st = operation.DestinationScaleOffset;
            Vector2[] syntheticUv2 =
            {
                new Vector2((minU - st.z) / st.x, (minV - st.w) / st.y),
                new Vector2((maxU - st.z) / st.x, (minV - st.w) / st.y),
                new Vector2((minU - st.z) / st.x, (maxV - st.w) / st.y),
                new Vector2((maxU - st.z) / st.x, (maxV - st.w) / st.y)
            };
            var footprint = new Mesh
            {
                name = "__DPBB_ImplicitZeroUvFootprint_" + operation.Label.Replace(' ', '_'),
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one },
                uv2 = syntheticUv2
            };
            footprint.SetIndices(new[] { 0, 1, 2, 2, 1, 3 }, MeshTopology.Triangles, 0, false);
            return footprint;
        }

        private static int GetPointSampleTexel(float normalizedCoordinate, int size)
        {
            if (!IsFinite(normalizedCoordinate) || size <= 0)
                throw new ArgumentOutOfRangeException(nameof(normalizedCoordinate));
            float clamped = Mathf.Clamp01(normalizedCoordinate);
            return Mathf.Clamp(Mathf.FloorToInt(clamped * size), 0, size - 1);
        }

        private static void ValidateTriangleRasterMesh(Mesh mesh, string label)
        {
            if (mesh == null || mesh.vertexCount <= 0 || mesh.subMeshCount <= 0)
                throw new InvalidOperationException("Triangle-raster mesh is empty for '" + label + "'.");
            Vector2[] uv2;
            try
            {
                uv2 = mesh.uv2;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Triangle-raster mesh UV2 cannot be read for '" + label + "'.", exception);
            }
            if (uv2 == null || uv2.Length != mesh.vertexCount)
            {
                throw new InvalidOperationException(
                    "Triangle-raster mesh has no full UV2 channel for '" + label + "'.");
            }
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles)
                {
                    throw new InvalidOperationException(
                        "Triangle-raster mesh has non-triangle submesh " + subMesh + " for '" + label + "'.");
                }
                uint indexCount = mesh.GetIndexCount(subMesh);
                if (indexCount < 3U || indexCount % 3U != 0U)
                {
                    throw new InvalidOperationException(
                        "Triangle-raster mesh has an invalid triangle index count in submesh " + subMesh +
                        " for '" + label + "'.");
                }
            }
        }

        private static RenderTexture CreateTriangleRasterTarget(
            int width,
            int height,
            RenderTextureFormat format,
            string label)
        {
            var target = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
            {
                name = "__DPBB_" + label.Replace(' ', '_'),
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!target.Create())
            {
                UnityEngine.Object.DestroyImmediate(target);
                throw new InvalidOperationException("Unable to allocate triangle-raster target '" + label + "'.");
            }
            return target;
        }

        private static void RenderOwnerExtrema(
            List<ChartCopyOperation> operations,
            RenderTexture ownerMin,
            RenderTexture ownerMax,
            Material material)
        {
            const float ownerEmpty = 65504f;
            Graphics.SetRenderTarget(ownerMin);
            GL.Clear(true, true, new Color(ownerEmpty, 0f, 0f, 0f));
            for (int i = 0; i < operations.Count; i++)
            {
                RenderTriangleOperation(operations[i], material, AtlasOwnerMinPass, i + 1, false);
            }
            Graphics.SetRenderTarget(ownerMax);
            GL.Clear(true, true, Color.black);
            for (int i = 0; i < operations.Count; i++)
            {
                RenderTriangleOperation(operations[i], material, AtlasOwnerMaxPass, i + 1, false);
            }
        }

        private static RasterizationStats ValidateTriangleOwnership(
            List<ChartCopyOperation> operations,
            RenderTexture ownerMin,
            RenderTexture ownerMax,
            string label)
        {
            Texture2D minReadback = null;
            Texture2D maxReadback = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                var result = new RasterizationStats
                {
                    OperationCount = operations.Count,
                    OwnedCoreMask = new bool[ownerMin.width * ownerMin.height],
                    ImplicitZeroCoreMask = new bool[ownerMin.width * ownerMin.height]
                };
                minReadback = new Texture2D(ownerMin.width, ownerMin.height, TextureFormat.RFloat, false, true);
                maxReadback = new Texture2D(ownerMax.width, ownerMax.height, TextureFormat.RFloat, false, true);
                RenderTexture.active = ownerMin;
                minReadback.ReadPixels(new Rect(0f, 0f, ownerMin.width, ownerMin.height), 0, 0, false);
                minReadback.Apply(false, false);
                RenderTexture.active = ownerMax;
                maxReadback.ReadPixels(new Rect(0f, 0f, ownerMax.width, ownerMax.height), 0, 0, false);
                maxReadback.Apply(false, false);
                Color[] minimum = minReadback.GetPixels();
                Color[] maximum = maxReadback.GetPixels();
                for (int i = 0; i < minimum.Length; i++)
                {
                    float minOwner = minimum[i].r;
                    float maxOwner = maximum[i].r;
                    bool emptyMin = minOwner >= 32752f;
                    bool emptyMax = maxOwner <= 0.5f;
                    if (emptyMin != emptyMax)
                    {
                        throw new InvalidOperationException(
                            "Triangle owner extrema disagree in " + label + " at texel " +
                            (i % ownerMin.width) + "," + (i / ownerMin.width) + ".");
                    }
                    if (emptyMin)
                        continue;
                    result.OwnedCoreMask[i] = true;
                    result.OwnedCoreTexelCount++;
                    int ownerIndex = Mathf.RoundToInt(maxOwner) - 1;
                    if (ownerIndex < 0 || ownerIndex >= operations.Count)
                    {
                        throw new InvalidOperationException(
                            "Triangle owner map has an invalid owner id in " + label + " at texel " +
                            (i % ownerMin.width) + "," + (i / ownerMin.width) + ".");
                    }
                    if (operations[ownerIndex].UsesImplicitZeroLightmapUv)
                    {
                        result.ImplicitZeroCoreMask[i] = true;
                        result.ImplicitZeroCoreTexelCount++;
                    }
                    if (Mathf.Abs(minOwner - maxOwner) <= 0.01f)
                        continue;
                    int lowIndex = Mathf.Clamp(Mathf.RoundToInt(minOwner) - 1, 0, operations.Count - 1);
                    int highIndex = Mathf.Clamp(Mathf.RoundToInt(maxOwner) - 1, 0, operations.Count - 1);
                    result.OwnerCollisionCount++;
                    throw new InvalidOperationException(
                        "UV2 triangle ownership collision in " + label + " at texel " +
                        (i % ownerMin.width) + "," + (i / ownerMin.width) + ": '" +
                        operations[lowIndex].Label + "' vs '" + operations[highIndex].Label + "'.");
                }
                result.SampledCoverageMask = DilateCoverageMask8Neighbour(
                    result.OwnedCoreMask,
                    ownerMin.width,
                    ownerMin.height,
                    ChartGutterPixels);
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                if (maxReadback != null)
                    UnityEngine.Object.DestroyImmediate(maxReadback);
                if (minReadback != null)
                    UnityEngine.Object.DestroyImmediate(minReadback);
            }
        }

        private static void ValidateCanonicalOrientationProof(
            List<ChartCopyOperation> operations,
            Texture2D canonicalSource,
            RenderTexture ownerMap,
            Material material,
            string label,
            RasterizationStats rasterization)
        {
            if (canonicalSource == null || rasterization == null)
                throw new ArgumentNullException(canonicalSource == null ? nameof(canonicalSource) : nameof(rasterization));
            int expectedTriangleSamples = rasterization.OwnedCoreTexelCount - rasterization.ImplicitZeroCoreTexelCount;
            if (expectedTriangleSamples < 0)
                throw new InvalidOperationException("Canonical orientation proof has an invalid implicit-zero ownership count.");
            if (expectedTriangleSamples == 0)
            {
                rasterization.CanonicalOrientationProofSampledTexelCount = 0;
                rasterization.CanonicalOrientationProofMaxAbsoluteChannelError = 0f;
                return;
            }

            RenderTexture proof = null;
            RenderTexture reference = null;
            Texture2D proofReadback = null;
            Texture2D referenceReadback = null;
            Texture2D ownerReadback = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                proof = CreateTriangleRasterTarget(
                    ownerMap.width,
                    ownerMap.height,
                    RenderTextureFormat.ARGBHalf,
                    "CanonicalOrientationProof_" + label);
                reference = CreateTriangleRasterTarget(
                    ownerMap.width,
                    ownerMap.height,
                    RenderTextureFormat.ARGBHalf,
                    "CanonicalOrientationReference_" + label);

                Graphics.SetRenderTarget(proof);
                GL.Clear(true, true, Color.black);
                material.SetTexture(AtlasSourceTextureId, canonicalSource);
                for (int i = 0; i < operations.Count; i++)
                    RenderCanonicalOrientationTriangle(operations[i], material);
                Graphics.Blit(canonicalSource, reference);

                proofReadback = ReadLinearArgbHalf(proof);
                referenceReadback = ReadLinearArgbHalf(reference);
                ownerReadback = ReadOwnerMap(ownerMap);
                Color[] proofPixels = proofReadback.GetPixels();
                Color[] referencePixels = referenceReadback.GetPixels();
                Color[] ownerPixels = ownerReadback.GetPixels();
                if (proofPixels.Length != referencePixels.Length || proofPixels.Length != ownerPixels.Length)
                    throw new InvalidOperationException("Canonical orientation proof readback dimensions disagree for " + label + ".");

                int sampled = 0;
                float maxError = 0f;
                int maxErrorIndex = -1;
                Color maxProof = Color.black;
                Color maxReference = Color.black;
                double sameAbsoluteSum = 0d;
                double sameSquaredSum = 0d;
                double flippedAbsoluteSum = 0d;
                double flippedSquaredSum = 0d;
                float flippedMaxError = 0f;
                for (int i = 0; i < proofPixels.Length; i++)
                {
                    if (ownerPixels[i].r <= 0.5f)
                        continue;
                    int ownerIndex = Mathf.RoundToInt(ownerPixels[i].r) - 1;
                    if (ownerIndex < 0 || ownerIndex >= operations.Count)
                        throw new InvalidOperationException("Canonical orientation proof found an invalid owner id for " + label + ".");
                    if (operations[ownerIndex].UsesImplicitZeroLightmapUv)
                        continue;
                    sampled++;
                    Color proofPixel = proofPixels[i];
                    Color referencePixel = referencePixels[i];
                    float error = MaxAbsoluteChannelDifference(proofPixel, referencePixel);
                    AccumulateColorError(proofPixel, referencePixel, ref sameAbsoluteSum, ref sameSquaredSum);
                    int x = i % ownerMap.width;
                    int y = i / ownerMap.width;
                    Color flippedReference = referencePixels[(ownerMap.height - 1 - y) * ownerMap.width + x];
                    float flippedError = MaxAbsoluteChannelDifference(proofPixel, flippedReference);
                    if (flippedError > flippedMaxError)
                        flippedMaxError = flippedError;
                    AccumulateColorError(proofPixel, flippedReference, ref flippedAbsoluteSum, ref flippedSquaredSum);
                    if (error > maxError)
                    {
                        maxError = error;
                        maxErrorIndex = i;
                        maxProof = proofPixel;
                        maxReference = referencePixel;
                    }
                }
                if (sampled != expectedTriangleSamples)
                {
                    throw new InvalidOperationException(
                        "Canonical orientation proof did not sample every owned UV2 texel for " + label +
                        ". expected=" + expectedTriangleSamples + " actual=" + sampled + ".");
                }
                rasterization.CanonicalOrientationProofSampledTexelCount = sampled;
                rasterization.CanonicalOrientationProofMaxAbsoluteChannelError = maxError;
                if (maxError > CanonicalOrientationProofMaxAbsoluteError)
                {
                    int maxX = maxErrorIndex % ownerMap.width;
                    int maxY = maxErrorIndex / ownerMap.width;
                    int channelCount = sampled * 4;
                    double sameMeanAbsolute = sameAbsoluteSum / channelCount;
                    double sameRmse = Math.Sqrt(sameSquaredSum / channelCount);
                    double flippedMeanAbsolute = flippedAbsoluteSum / channelCount;
                    double flippedRmse = Math.Sqrt(flippedSquaredSum / channelCount);
                    throw new InvalidOperationException(
                        "Canonical UV2 triangle orientation/identity proof exceeded its tolerance for " + label +
                        ". maxAbsError=" + maxError.ToString("R", CultureInfo.InvariantCulture) +
                        " tolerance=" + CanonicalOrientationProofMaxAbsoluteError.ToString("R", CultureInfo.InvariantCulture) +
                        " maxTexel=" + maxX + "," + maxY +
                        " proof=" + FormatColor(maxProof) +
                        " reference=" + FormatColor(maxReference) +
                        " same(meanAbs=" + sameMeanAbsolute.ToString("R", CultureInfo.InvariantCulture) +
                        ",rmse=" + sameRmse.ToString("R", CultureInfo.InvariantCulture) + ")" +
                        " yFlipped(meanAbs=" + flippedMeanAbsolute.ToString("R", CultureInfo.InvariantCulture) +
                        ",rmse=" + flippedRmse.ToString("R", CultureInfo.InvariantCulture) +
                        ",maxAbs=" + flippedMaxError.ToString("R", CultureInfo.InvariantCulture) + ").");
                }
            }
            finally
            {
                RenderTexture.active = previous;
                if (ownerReadback != null)
                    UnityEngine.Object.DestroyImmediate(ownerReadback);
                if (referenceReadback != null)
                    UnityEngine.Object.DestroyImmediate(referenceReadback);
                if (proofReadback != null)
                    UnityEngine.Object.DestroyImmediate(proofReadback);
                if (reference != null)
                    UnityEngine.Object.DestroyImmediate(reference);
                if (proof != null)
                    UnityEngine.Object.DestroyImmediate(proof);
            }
        }

        private static Texture2D ReadLinearArgbHalf(RenderTexture source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            var readable = new Texture2D(source.width, source.height, TextureFormat.RGBAHalf, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            RenderTexture.active = source;
            readable.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
            readable.Apply(false, false);
            return readable;
        }

        private static Texture2D ReadOwnerMap(RenderTexture source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            var readable = new Texture2D(source.width, source.height, TextureFormat.RFloat, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            RenderTexture.active = source;
            readable.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
            readable.Apply(false, false);
            return readable;
        }

        private static void RenderCanonicalOrientationTriangle(ChartCopyOperation operation, Material material)
        {
            material.SetVector(AtlasDestinationStId, operation.DestinationScaleOffset);
            material.SetVector(AtlasSourceStId, operation.DestinationScaleOffset);
            material.SetFloat(AtlasUseConstantSourceUvId, operation.UsesImplicitZeroLightmapUv ? 1f : 0f);
            material.SetFloat(AtlasOwnerId, 0f);
            if (!material.SetPass(AtlasCopyTrianglePass))
                throw new InvalidOperationException("Canonical orientation proof shader pass could not be set.");
            Mesh rasterMesh = operation.RasterMesh;
            for (int subMesh = 0; subMesh < rasterMesh.subMeshCount; subMesh++)
                Graphics.DrawMeshNow(rasterMesh, Matrix4x4.identity, subMesh);
        }

        private static float MaxAbsoluteChannelDifference(Color left, Color right)
        {
            return Mathf.Max(
                Mathf.Abs(left.r - right.r),
                Mathf.Abs(left.g - right.g),
                Mathf.Abs(left.b - right.b),
                Mathf.Abs(left.a - right.a));
        }

        private static void AccumulateColorError(
            Color left,
            Color right,
            ref double absoluteSum,
            ref double squaredSum)
        {
            AccumulateChannelError(left.r - right.r, ref absoluteSum, ref squaredSum);
            AccumulateChannelError(left.g - right.g, ref absoluteSum, ref squaredSum);
            AccumulateChannelError(left.b - right.b, ref absoluteSum, ref squaredSum);
            AccumulateChannelError(left.a - right.a, ref absoluteSum, ref squaredSum);
        }

        private static void AccumulateChannelError(float value, ref double absoluteSum, ref double squaredSum)
        {
            absoluteSum += Math.Abs(value);
            squaredSum += (double)value * value;
        }

        private static string FormatColor(Color value)
        {
            return "(" + value.r.ToString("R", CultureInfo.InvariantCulture) + "," +
                   value.g.ToString("R", CultureInfo.InvariantCulture) + "," +
                   value.b.ToString("R", CultureInfo.InvariantCulture) + "," +
                   value.a.ToString("R", CultureInfo.InvariantCulture) + ")";
        }

        private static void RenderTriangleValues(
            List<ChartCopyOperation> operations,
            RenderTexture destination,
            bool direction,
            Material material,
            int sourceMip,
            Dictionary<Texture2D, Texture2D> exactSourceMips,
            string label)
        {
            Graphics.SetRenderTarget(destination);
            for (int i = 0; i < operations.Count; i++)
            {
                ChartCopyOperation operation = operations[i];
                if (!operation.HasSource)
                    continue;
                Texture2D source = direction ? operation.SourceDirection : operation.SourceColor;
                Texture2D exactMip = GetExactSourceMipTexture(
                    source,
                    sourceMip,
                    exactSourceMips,
                    label + " operation '" + operation.Label + "'");
                material.SetTexture(AtlasSourceTextureId, exactMip);
                RenderTriangleOperation(operation, material, AtlasCopyTrianglePass, 0, true);
            }
        }

        private static void RenderTriangleOperation(
            ChartCopyOperation operation,
            Material material,
            int pass,
            int ownerId,
            bool sourceSampling)
        {
            material.SetVector(AtlasDestinationStId, operation.DestinationScaleOffset);
            if (sourceSampling)
                material.SetVector(AtlasSourceStId, operation.SourceScaleOffset);
            material.SetFloat(AtlasUseConstantSourceUvId, operation.UsesImplicitZeroLightmapUv ? 1f : 0f);
            material.SetFloat(AtlasOwnerId, ownerId);
            if (!material.SetPass(pass))
                throw new InvalidOperationException("Triangle-raster shader pass " + pass + " could not be set.");
            Mesh rasterMesh = operation.RasterMesh;
            for (int subMesh = 0; subMesh < rasterMesh.subMeshCount; subMesh++)
                Graphics.DrawMeshNow(rasterMesh, Matrix4x4.identity, subMesh);
        }

        private static void DilateOnlyIntoUnownedTexels(
            RenderTexture content,
            RenderTexture ownerMap,
            Material material,
            string label)
        {
            RenderTexture colorScratch = null;
            RenderTexture occupancyA = null;
            RenderTexture occupancyB = null;
            try
            {
                colorScratch = CreateTriangleRasterTarget(
                    content.width,
                    content.height,
                    RenderTextureFormat.ARGBHalf,
                    "DilationColor_" + label);
                occupancyA = CreateTriangleRasterTarget(
                    content.width,
                    content.height,
                    RenderTextureFormat.RFloat,
                    "DilationOccupancyA_" + label);
                occupancyB = CreateTriangleRasterTarget(
                    content.width,
                    content.height,
                    RenderTextureFormat.RFloat,
                    "DilationOccupancyB_" + label);
                Graphics.Blit(ownerMap, occupancyA);

                RenderTexture currentColor = content;
                RenderTexture nextColor = colorScratch;
                RenderTexture currentOccupancy = occupancyA;
                RenderTexture nextOccupancy = occupancyB;
                material.SetTexture(AtlasOwnerMapId, ownerMap);
                for (int step = 0; step < ChartGutterPixels; step++)
                {
                    material.SetTexture(AtlasOccupancyId, currentOccupancy);
                    Graphics.Blit(currentColor, nextColor, material, AtlasDilateColorPass);
                    material.SetTexture(AtlasOwnerMapId, ownerMap);
                    Graphics.Blit(currentOccupancy, nextOccupancy, material, AtlasDilateOccupancyPass);
                    SwapRenderTextures(ref currentColor, ref nextColor);
                    SwapRenderTextures(ref currentOccupancy, ref nextOccupancy);
                }
                if (currentColor != content)
                    Graphics.Blit(currentColor, content);
            }
            finally
            {
                if (occupancyB != null)
                    UnityEngine.Object.DestroyImmediate(occupancyB);
                if (occupancyA != null)
                    UnityEngine.Object.DestroyImmediate(occupancyA);
                if (colorScratch != null)
                    UnityEngine.Object.DestroyImmediate(colorScratch);
            }
        }

        private static void SwapRenderTextures(ref RenderTexture left, ref RenderTexture right)
        {
            RenderTexture temporary = left;
            left = right;
            right = temporary;
        }

        private static void ValidateMatchingRasterizationCoverage(
            RasterizationStats color,
            RasterizationStats direction,
            string label)
        {
            if (color == null || direction == null ||
                color.OperationCount != direction.OperationCount ||
                color.OwnedCoreTexelCount != direction.OwnedCoreTexelCount ||
                color.ImplicitZeroCoreTexelCount != direction.ImplicitZeroCoreTexelCount ||
                color.OwnerCollisionCount != 0 || direction.OwnerCollisionCount != 0)
            {
                throw new InvalidOperationException(
                    "Color/direction UV2 triangle ownership evidence differs or contains a collision for " + label + ".");
            }

            RasterizationStats[] colorMips = color.MipLevels ?? Array.Empty<RasterizationStats>();
            RasterizationStats[] directionMips = direction.MipLevels ?? Array.Empty<RasterizationStats>();
            if (colorMips.Length == 0 || colorMips.Length != directionMips.Length)
            {
                throw new InvalidOperationException(
                    "Color/direction mip coverage cardinality differs for " + label + ".");
            }
            for (int mip = 0; mip < colorMips.Length; mip++)
            {
                RasterizationStats colorLevel = colorMips[mip];
                RasterizationStats directionLevel = directionMips[mip];
                if (colorLevel == null || directionLevel == null ||
                    colorLevel.MipLevel != mip || directionLevel.MipLevel != mip ||
                    colorLevel.OperationCount != directionLevel.OperationCount ||
                    colorLevel.OwnedCoreTexelCount != directionLevel.OwnedCoreTexelCount ||
                    colorLevel.ImplicitZeroCoreTexelCount != directionLevel.ImplicitZeroCoreTexelCount ||
                    colorLevel.OwnerCollisionCount != 0 || directionLevel.OwnerCollisionCount != 0 ||
                    colorLevel.OwnedCoreMask == null || directionLevel.OwnedCoreMask == null ||
                    colorLevel.OwnedCoreMask.Length != directionLevel.OwnedCoreMask.Length ||
                    colorLevel.ImplicitZeroCoreMask == null || directionLevel.ImplicitZeroCoreMask == null ||
                    colorLevel.ImplicitZeroCoreMask.Length != directionLevel.ImplicitZeroCoreMask.Length ||
                    colorLevel.SampledCoverageMask == null || directionLevel.SampledCoverageMask == null ||
                    colorLevel.SampledCoverageMask.Length != directionLevel.SampledCoverageMask.Length)
                {
                    throw new InvalidOperationException(
                        "Color/direction coverage differs at generated mip " + mip + " for " + label + ".");
                }
                for (int texel = 0; texel < colorLevel.OwnedCoreMask.Length; texel++)
                {
                    if (colorLevel.OwnedCoreMask[texel] != directionLevel.OwnedCoreMask[texel] ||
                        colorLevel.ImplicitZeroCoreMask[texel] != directionLevel.ImplicitZeroCoreMask[texel] ||
                        colorLevel.SampledCoverageMask[texel] != directionLevel.SampledCoverageMask[texel])
                    {
                        throw new InvalidOperationException(
                            "Color/direction coverage masks differ at generated mip " + mip +
                            " texel " + texel + " for " + label + ".");
                    }
                }
            }
        }

        private sealed class BucketWork
        {
            public readonly int BucketIndex;
            public readonly int LocalLightmapIndex;
            public readonly Texture2D P0Color;
            public readonly Texture2D P0Direction;
            public readonly Texture2D P100Color;
            public readonly Texture2D P100Direction;
            public int Width => P0Color.width;
            public int Height => P0Color.height;

            public BucketWork(
                int bucketIndex,
                int localLightmapIndex,
                Texture2D p0Color,
                Texture2D p0Direction,
                Texture2D p100Color,
                Texture2D p100Direction)
            {
                BucketIndex = bucketIndex;
                LocalLightmapIndex = localLightmapIndex;
                P0Color = p0Color;
                P0Direction = p0Direction;
                P100Color = p100Color;
                P100Direction = p100Direction;
            }
        }

        private sealed class ChartCopyOperation
        {
            public readonly int DestinationBucketIndex;
            public readonly Vector4 DestinationScaleOffset;
            public readonly Texture2D SourceColor;
            public readonly Texture2D SourceDirection;
            public readonly Vector4 SourceScaleOffset;
            public readonly Mesh ProductionMesh;
            public readonly LightmapUvMode UvMode;
            public readonly string Label;
            public Mesh TemporaryRasterMesh;

            public bool HasSource => SourceColor != null && SourceDirection != null;
            public bool UsesImplicitZeroLightmapUv => UvMode == LightmapUvMode.ImplicitZero;
            public Mesh RasterMesh => TemporaryRasterMesh != null ? TemporaryRasterMesh : ProductionMesh;

            public ChartCopyOperation(
                int destinationBucketIndex,
                Vector4 destinationScaleOffset,
                Texture2D sourceColor,
                Texture2D sourceDirection,
                Vector4 sourceScaleOffset,
                Mesh productionMesh,
                LightmapUvMode lightmapUvMode,
                string label)
            {
                DestinationBucketIndex = destinationBucketIndex;
                DestinationScaleOffset = destinationScaleOffset;
                SourceColor = sourceColor;
                SourceDirection = sourceDirection;
                SourceScaleOffset = sourceScaleOffset;
                ProductionMesh = productionMesh;
                UvMode = lightmapUvMode;
                Label = label;
            }
        }

        private sealed class EndpointNativeResponseDeltaAtlasSet
        {
            public readonly Texture2D[] ColorDeltas;
            public readonly Texture2D[] DirectionalMomentDeltas;
            public readonly string[] ColorDeltaRelativePaths;
            public readonly string[] DirectionalMomentDeltaRelativePaths;
            public readonly RasterizationStats[] BaselineColorRasterization;
            public readonly RasterizationStats[] BaselineDirectionRasterization;
            public readonly RasterizationStats[] FullColorRasterization;
            public readonly RasterizationStats[] FullDirectionRasterization;
            public readonly DeltaQuantizationStats[] ColorDeltaQuantization;
            public readonly DeltaQuantizationStats[] DirectionalMomentDeltaQuantization;

            public EndpointNativeResponseDeltaAtlasSet(int count)
            {
                ColorDeltas = new Texture2D[count];
                DirectionalMomentDeltas = new Texture2D[count];
                ColorDeltaRelativePaths = new string[count];
                DirectionalMomentDeltaRelativePaths = new string[count];
                BaselineColorRasterization = new RasterizationStats[count];
                BaselineDirectionRasterization = new RasterizationStats[count];
                FullColorRasterization = new RasterizationStats[count];
                FullDirectionRasterization = new RasterizationStats[count];
                ColorDeltaQuantization = new DeltaQuantizationStats[count];
                DirectionalMomentDeltaQuantization = new DeltaQuantizationStats[count];
            }
        }

        private sealed class CompactResponseAtlasSet
        {
            public readonly Texture2D[] ColorPositive;
            public readonly Texture2D[] ColorNegative;
            public readonly Texture2D[] MomentPositive;
            public readonly Texture2D[] MomentNegative;
            public readonly string[] ColorPositiveRelativePaths;
            public readonly string[] ColorNegativeRelativePaths;
            public readonly string[] MomentPositiveRelativePaths;
            public readonly string[] MomentNegativeRelativePaths;
            public readonly Vector4[][] ColorPositiveScales;
            public readonly Vector4[][] ColorNegativeScales;
            public readonly Vector4[][] MomentPositiveScales;
            public readonly Vector4[][] MomentNegativeScales;
            public readonly CompactDeltaReconstructionStats[][] ColorReconstruction;
            public readonly CompactDeltaReconstructionStats[][] MomentReconstruction;
            public readonly RasterizationStats[] BaselineColorRasterization;
            public readonly RasterizationStats[] BaselineDirectionRasterization;
            public readonly RasterizationStats[] FullColorRasterization;
            public readonly RasterizationStats[] FullDirectionRasterization;

            public CompactResponseAtlasSet(int count)
            {
                ColorPositive = new Texture2D[count];
                ColorNegative = new Texture2D[count];
                MomentPositive = new Texture2D[count];
                MomentNegative = new Texture2D[count];
                ColorPositiveRelativePaths = new string[count];
                ColorNegativeRelativePaths = new string[count];
                MomentPositiveRelativePaths = new string[count];
                MomentNegativeRelativePaths = new string[count];
                ColorPositiveScales = new Vector4[count][];
                ColorNegativeScales = new Vector4[count][];
                MomentPositiveScales = new Vector4[count][];
                MomentNegativeScales = new Vector4[count][];
                ColorReconstruction = new CompactDeltaReconstructionStats[count][];
                MomentReconstruction = new CompactDeltaReconstructionStats[count][];
                BaselineColorRasterization = new RasterizationStats[count];
                BaselineDirectionRasterization = new RasterizationStats[count];
                FullColorRasterization = new RasterizationStats[count];
                FullDirectionRasterization = new RasterizationStats[count];
            }

            public void Assign(int index, CompactMagnitudePayload payload, string payloadRoot)
            {
                if (payload == null)
                    throw new ArgumentNullException(nameof(payload));
                ColorPositive[index] = payload.ColorPositive;
                ColorNegative[index] = payload.ColorNegative;
                MomentPositive[index] = payload.MomentPositive;
                MomentNegative[index] = payload.MomentNegative;
                ColorPositiveRelativePaths[index] = MakeRelativeAssetPath(payloadRoot, payload.ColorPositivePath);
                ColorNegativeRelativePaths[index] = MakeRelativeAssetPath(payloadRoot, payload.ColorNegativePath);
                MomentPositiveRelativePaths[index] = MakeRelativeAssetPath(payloadRoot, payload.MomentPositivePath);
                MomentNegativeRelativePaths[index] = MakeRelativeAssetPath(payloadRoot, payload.MomentNegativePath);
                ColorPositiveScales[index] = CloneVector4Array(payload.ColorPositiveScales);
                ColorNegativeScales[index] = CloneVector4Array(payload.ColorNegativeScales);
                MomentPositiveScales[index] = CloneVector4Array(payload.MomentPositiveScales);
                MomentNegativeScales[index] = CloneVector4Array(payload.MomentNegativeScales);
                ColorReconstruction[index] = payload.ColorReconstruction;
                MomentReconstruction[index] = payload.MomentReconstruction;
            }
        }

        private sealed class CompactMagnitudePayload
        {
            public Texture2D ColorPositive;
            public Texture2D ColorNegative;
            public Texture2D MomentPositive;
            public Texture2D MomentNegative;
            public string ColorPositivePath;
            public string ColorNegativePath;
            public string MomentPositivePath;
            public string MomentNegativePath;
            public Vector4[] ColorPositiveScales;
            public Vector4[] ColorNegativeScales;
            public Vector4[] MomentPositiveScales;
            public Vector4[] MomentNegativeScales;
            public CompactDeltaReconstructionStats[] ColorReconstruction;
            public CompactDeltaReconstructionStats[] MomentReconstruction;
        }

        private sealed class CompactReconstructionQualityException : InvalidOperationException
        {
            public CompactReconstructionQualityException(string message)
                : base(message)
            {
            }
        }

        private sealed class MipCompressionQualityException : InvalidOperationException
        {
            public MipCompressionQualityException(string message)
                : base(message)
            {
            }
        }

        [Serializable]
        private sealed class CompactDeltaReconstructionStats
        {
            public int mipLevel;
            public int ownedTexelCount;
            public int channelSampleCount;
            public float absoluteRmse;
            public float relativeRmse;
            public float absoluteMax;
            public int wrongSignCount;
        }

        [Serializable]
        private sealed class DeltaQuantizationStats
        {
            public int ownedMip0TexelCount;
            public int channelSampleCount;
            public float absoluteRmse;
            public float absoluteP95;
            public float absoluteP99;
            public float absoluteMax;
        }

        private sealed class RepackedAtlasSet
        {
            public readonly Texture2D[] Colors;
            public readonly Texture2D[] Directions;
            public readonly string[] ColorRelativePaths;
            public readonly string[] DirectionRelativePaths;
            public readonly RasterizationStats[] ColorRasterization;
            public readonly RasterizationStats[] DirectionRasterization;

            public RepackedAtlasSet(int count)
            {
                Colors = new Texture2D[count];
                Directions = new Texture2D[count];
                ColorRelativePaths = new string[count];
                DirectionRelativePaths = new string[count];
                ColorRasterization = new RasterizationStats[count];
                DirectionRasterization = new RasterizationStats[count];
            }
        }

        private sealed class RasterizationStats
        {
            public int MipLevel;
            public int OperationCount;
            public int OwnedCoreTexelCount;
            public int ImplicitZeroCoreTexelCount;
            public int OwnerCollisionCount;
            public int CanonicalOrientationProofSampledTexelCount;
            public float CanonicalOrientationProofMaxAbsoluteChannelError;
            public RasterizationStats[] MipLevels;
            public bool[] OwnedCoreMask;
            public bool[] ImplicitZeroCoreMask;
            public bool[] SampledCoverageMask;
        }

        // This is intentionally a byte-for-byte semantic mirror of the public state-hash
        // construction used by DungeonPortalReceiverBounceBaker.  The capture tool keeps
        // its implementation private, so the generated-payload verifier recomputes it
        // here rather than treating the serialized stateHash as an unverified assertion.
        private static string ComputeCaptureStateHash(
            DungeonPortalReceiverResponseCapture.CaptureState state)
        {
            var builder = new StringBuilder(8192);
            AppendString(builder, state.stateName);
            AppendString(builder, state.capturedUtcIso8601);
            AppendString(builder, state.stateFolderPath);
            AppendDouble(builder, state.elapsedSeconds);
            builder.Append((int)state.lightmapsMode).Append('|');
            AppendInjector(builder, state.injector);
            AppendString(builder, state.probeLocalPositionSignature);
            AppendString(builder, state.rendererLayoutSignature);
            AppendString(builder, state.fullRendererParitySignature);
            builder.Append(state.fullRendererCount).Append('|');
            builder.Append(state.fullRendererComponentCount).Append('|');
            builder.Append(state.materialPropertyBlocksVerifiedEmpty ? '1' : '0').Append('|');
            AppendString(builder, state.lightingSettingsCloneDependencyHashBeforeBake);
            AppendString(builder, state.lightingSettingsCloneDependencyHashAfterBake);
            builder.Append(state.doorLightProbeCount).Append('|');
            AppendString(builder, state.doorLightProbeLocalPositionSignature);

            DungeonPortalReceiverResponseCapture.CaptureLightmap[] lightmaps = state.lightmaps ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            for (int i = 0; i < lightmaps.Length; i++)
                AppendCaptureLightmap(builder, lightmaps[i]);

            DungeonPortalReceiverResponseCapture.CaptureRenderer[] renderers = state.renderers ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureRenderer>();
            for (int i = 0; i < renderers.Length; i++)
                AppendCaptureRenderer(builder, renderers[i]);

            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] fullInventory =
                state.fullRendererInventory ??
                Array.Empty<DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry>();
            for (int i = 0; i < fullInventory.Length; i++)
                AppendFullRendererInventoryEntry(builder, fullInventory[i]);

            DungeonPortalReceiverResponseCapture.ProbeSample[] probes = state.probes ??
                Array.Empty<DungeonPortalReceiverResponseCapture.ProbeSample>();
            for (int i = 0; i < probes.Length; i++)
                AppendProbe(builder, probes[i]);

            DungeonPortalReceiverResponseCapture.FixedCameraCapture[] cameras = state.fixedCameraCaptures ??
                Array.Empty<DungeonPortalReceiverResponseCapture.FixedCameraCapture>();
            for (int i = 0; i < cameras.Length; i++)
                AppendFixedCamera(builder, cameras[i]);

            return ComputeSha256(builder.ToString());
        }

        private static void AppendInjector(
            StringBuilder builder,
            DungeonPortalReceiverResponseCapture.InjectorProvenance injector)
        {
            builder.Append(injector.enabled ? '1' : '0').Append('|');
            builder.Append((int)injector.type).Append('|');
            builder.Append((int)injector.lightmapBakeType).Append('|');
            AppendColor(builder, injector.color);
            AppendFloat(builder, injector.intensity);
            AppendFloat(builder, injector.bounceIntensity);
            AppendVector3(builder, injector.localPosition);
            AppendVector3(builder, NormalizeEulerAngles(injector.localEulerAngles));
            AppendString(builder, injector.placementInterpretation);
            AppendFloat(builder, injector.range);
            AppendFloat(builder, injector.spotAngle);
            AppendFloat(builder, injector.innerSpotAngle);
            builder.Append((int)injector.shadows).Append('|');
            AppendFloat(builder, injector.shadowStrength);
            builder.Append(injector.cullingMask).Append('|');
            builder.Append(injector.renderingLayerMask).Append('|');
            builder.Append(injector.shadowRenderingLayerMask).Append('|');
        }

        private static void AppendCaptureLightmap(
            StringBuilder builder,
            DungeonPortalReceiverResponseCapture.CaptureLightmap lightmap)
        {
            builder.Append(lightmap.sourceLightmapIndex).Append('|');
            AppendString(builder, lightmap.colorAssetPath);
            AppendString(builder, lightmap.colorDependencyHash);
            builder.Append(lightmap.colorWidth).Append('|').Append(lightmap.colorHeight).Append('|');
            AppendString(builder, lightmap.colorFormat);
            AppendString(builder, lightmap.directionAssetPath);
            AppendString(builder, lightmap.directionDependencyHash);
            builder.Append(lightmap.directionWidth).Append('|').Append(lightmap.directionHeight).Append('|');
            AppendString(builder, lightmap.directionFormat);
            builder.Append(lightmap.hasShadowMask ? '1' : '0').Append('|');
            AppendString(builder, lightmap.shadowMaskAssetPath);
            AppendString(builder, lightmap.shadowMaskDependencyHash);
            builder.Append(lightmap.shadowMaskWidth).Append('|').Append(lightmap.shadowMaskHeight).Append('|');
            AppendString(builder, lightmap.shadowMaskFormat);
        }

        private static void AppendCaptureRenderer(
            StringBuilder builder,
            DungeonPortalReceiverResponseCapture.CaptureRenderer renderer)
        {
            AppendString(builder, renderer.relativePath);
            builder.Append(renderer.rendererBucketIndex).Append('|');
            builder.Append(renderer.componentOrdinal).Append('|');
            AppendString(builder, renderer.meshAssetGuid);
            builder.Append(renderer.meshLocalId).Append('|');
            AppendString(builder, renderer.meshUv2Hash);
            builder.Append(renderer.lightmapIndex).Append('|');
            AppendVector4(builder, renderer.lightmapScaleOffset);
        }

        private static void AppendFullRendererInventoryEntry(
            StringBuilder builder,
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry entry)
        {
            AppendString(builder, entry.scope);
            AppendString(builder, entry.relativePath);
            AppendString(builder, entry.rendererType);
            builder.Append(entry.componentOrdinal).Append('|');
            AppendString(builder, entry.canonicalBucket);
            AppendString(builder, entry.parityMetadataHash);
        }

        private static void AppendProbe(
            StringBuilder builder,
            DungeonPortalReceiverResponseCapture.ProbeSample probe)
        {
            builder.Append(probe.probeIndex).Append('|');
            AppendVector3(builder, probe.localPosition);
            AppendVector3(builder, probe.worldPosition);
            AppendVector3(builder, probe.coefficient0);
            AppendVector3(builder, probe.coefficient1);
            AppendVector3(builder, probe.coefficient2);
            AppendVector3(builder, probe.coefficient3);
            AppendVector3(builder, probe.coefficient4);
            AppendVector3(builder, probe.coefficient5);
            AppendVector3(builder, probe.coefficient6);
            AppendVector3(builder, probe.coefficient7);
            AppendVector3(builder, probe.coefficient8);
            AppendVector4(builder, probe.occlusion);
        }

        private static void AppendFixedCamera(
            StringBuilder builder,
            DungeonPortalReceiverResponseCapture.FixedCameraCapture camera)
        {
            AppendString(builder, camera.cameraId);
            AppendString(builder, camera.cameraPath);
            AppendVector3(builder, camera.doorwayLocalPosition);
            AppendVector3(builder, NormalizeEulerAngles(camera.doorwayLocalEulerAngles));
            AppendFloat(builder, camera.fieldOfView);
            AppendFloat(builder, camera.nearClipPlane);
            AppendFloat(builder, camera.farClipPlane);
            builder.Append(camera.orthographic ? '1' : '0').Append('|');
            AppendFloat(builder, camera.orthographicSize);
            AppendFloat(builder, camera.aspect);
            builder.Append((int)camera.clearFlags).Append('|');
            AppendColor(builder, camera.backgroundColor);
            builder.Append(camera.cullingMask).Append('|');
            builder.Append((int)camera.renderingPath).Append('|');
            builder.Append(camera.postProcessingEnabled ? '1' : '0').Append('|');
            builder.Append((int)camera.antialiasing).Append('|');
            builder.Append(camera.dithering ? '1' : '0').Append('|');
            builder.Append(camera.volumeLayerMask).Append('|');
            builder.Append(camera.allowMsaa ? '1' : '0').Append('|');
            builder.Append(camera.allowDynamicResolution ? '1' : '0').Append('|');
            builder.Append(camera.useOcclusionCulling ? '1' : '0').Append('|');
            builder.Append(camera.urpRendererIndex).Append('|');
            AppendString(builder, camera.renderPipelineAssetPath);
            AppendString(builder, camera.renderPipelineDependencyHash);
            builder.Append(camera.hdr ? '1' : '0').Append('|');
            builder.Append(camera.linearPreTonemap ? '1' : '0').Append('|');
            AppendString(builder, camera.hdrColorAssetPath);
            AppendString(builder, camera.hdrColorDependencyHash);
            builder.Append(camera.width).Append('|').Append(camera.height).Append('|');
            AppendString(builder, camera.textureFormat);
        }

        private static Color[] ReadLinearPixels(Texture2D source)
        {
            if (source == null || source.width <= 0 || source.height <= 0)
                throw new InvalidOperationException("Cannot read an empty endpoint cookie texture.");
            if (GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat) ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                throw new InvalidOperationException("Endpoint cookie cannot be read as a linear HDR GPU texture.");
            }

            RenderTexture previousTarget = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture target = null;
            Texture2D readable = null;
            try
            {
                GL.sRGBWrite = false;
                target = new RenderTexture(
                    source.width,
                    source.height,
                    0,
                    RenderTextureFormat.ARGBHalf,
                    RenderTextureReadWrite.Linear)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                if (!target.Create())
                    throw new InvalidOperationException("Unable to allocate a temporary linear endpoint-cookie readback target.");
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                readable = new Texture2D(source.width, source.height, TextureFormat.RGBAHalf, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                readable.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
                readable.Apply(false, false);
                return readable.GetPixels();
            }
            finally
            {
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousSrgbWrite;
                if (readable != null)
                    UnityEngine.Object.DestroyImmediate(readable);
                if (target != null)
                    UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void AddArtifactFingerprint(
            List<ArtifactFingerprint> records,
            string payloadRoot,
            string assetPath,
            string kind,
            UnityEngine.Object knownAsset)
        {
            if (records == null)
                throw new ArgumentNullException(nameof(records));
            string relativePath = MakeRelativeAssetPath(payloadRoot, assetPath);
            if (records.Any(item => item != null && string.Equals(item.relativePath, relativePath, StringComparison.Ordinal)))
                throw new InvalidOperationException("Generated artifact was fingerprinted twice: '" + relativePath + "'.");
            UnityEngine.Object asset = knownAsset != null
                ? knownAsset
                : AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null)
                throw new InvalidOperationException("Cannot fingerprint a missing generated artifact: '" + assetPath + "'.");
            Texture2D texture = asset as Texture2D;
            records.Add(new ArtifactFingerprint
            {
                relativePath = relativePath,
                dependencyHash = GetDependencyHash(assetPath),
                kind = kind ?? string.Empty,
                width = texture != null ? texture.width : 0,
                height = texture != null ? texture.height : 0,
                mipmapCount = texture != null ? texture.mipmapCount : 0,
                textureFormat = texture != null ? texture.format.ToString() : string.Empty,
                linear = texture != null && !GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat)
            });
        }

        private static string MakeRelativeAssetPath(string payloadRoot, string assetPath)
        {
            string normalizedRoot = NormalizeAssetPath(payloadRoot).TrimEnd('/');
            string normalizedAsset = NormalizeAssetPath(assetPath);
            if (!normalizedAsset.StartsWith(normalizedRoot + "/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Asset is not inside the generated payload root. root='" + normalizedRoot + "' asset='" +
                    normalizedAsset + "'.");
            }
            return normalizedAsset.Substring(normalizedRoot.Length + 1);
        }

        private static string CombinePayloadPath(string payloadRoot, string relativePath)
        {
            string root = NormalizeAssetPath(payloadRoot).TrimEnd('/');
            if (!root.StartsWith(Root + "/", StringComparison.Ordinal) &&
                !string.Equals(root, Root, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Payload root is outside the isolated PoC root: '" + root + "'.");
            }
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidOperationException("Generated manifest has an empty relative artifact path.");
            string[] segments = relativePath.Replace('\\', '/').Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(segments[i]) || segments[i] == "." || segments[i] == "..")
                    throw new InvalidOperationException("Generated manifest has a non-canonical relative artifact path: '" + relativePath + "'.");
            }
            return root + "/" + string.Join("/", segments);
        }

        private static string AssetPathToPhysicalPath(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            if (!normalized.StartsWith(Root + "/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "This authoring tool may write only inside its isolated PoC root: '" + normalized + "'.");
            }
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new InvalidOperationException("Unable to resolve the Unity project root.");
            string fullProjectRoot = Path.GetFullPath(projectRoot);
            string candidate = Path.GetFullPath(Path.Combine(
                fullProjectRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            string rootWithSeparator = fullProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                       Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Resolved generated output escapes the Unity project root.");
            return candidate;
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Asset path is empty.");
            string normalized = path.Replace('\\', '/').Trim();
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) ||
                normalized.IndexOf("..", StringComparison.Ordinal) >= 0 ||
                normalized.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException("Expected a canonical Assets-relative path: '" + path + "'.");
            }
            return normalized;
        }

        private static string GetDependencyHash(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            if (AssetDatabase.LoadMainAssetAtPath(normalized) == null)
                throw new InvalidOperationException("Cannot hash a missing asset: '" + normalized + "'.");
            string value = AssetDatabase.GetAssetDependencyHash(normalized).ToString();
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Asset dependency hash is unavailable: '" + normalized + "'.");
            return value;
        }

        private static string ComputeFloatArrayHash(float[] values)
        {
            if (values == null)
                return ComputeSha256("null");
            var builder = new StringBuilder(values.Length * 18 + 16);
            AppendInt(builder, values.Length);
            for (int i = 0; i < values.Length; i++)
                AppendFloat(builder, values[i]);
            return ComputeSha256(builder.ToString());
        }

        private static float[] ToArray(Vector4 value)
        {
            return new[] { value.x, value.y, value.z, value.w };
        }

        private static void AppendString(StringBuilder builder, string value)
        {
            string safe = value ?? string.Empty;
            builder.Append(safe.Length).Append(':').Append(safe).Append('|');
        }

        private static void AppendInt(StringBuilder builder, int value)
        {
            builder.Append(value).Append('|');
        }

        private static void AppendDouble(StringBuilder builder, double value)
        {
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        private static void AppendFloat(StringBuilder builder, float value)
        {
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        private static void AppendVector3(StringBuilder builder, Vector3 value)
        {
            AppendFloat(builder, value.x);
            AppendFloat(builder, value.y);
            AppendFloat(builder, value.z);
        }

        private static void AppendVector4(StringBuilder builder, Vector4 value)
        {
            AppendFloat(builder, value.x);
            AppendFloat(builder, value.y);
            AppendFloat(builder, value.z);
            AppendFloat(builder, value.w);
        }

        private static void AppendColor(StringBuilder builder, Color value)
        {
            AppendFloat(builder, value.r);
            AppendFloat(builder, value.g);
            AppendFloat(builder, value.b);
            AppendFloat(builder, value.a);
        }

        private static string ComputeSha256(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            using (SHA256 hasher = SHA256.Create())
            {
                byte[] hash = hasher.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static Vector3 NormalizeEulerAngles(Vector3 angles)
        {
            return new Vector3(
                Mathf.Repeat(angles.x, 360f),
                Mathf.Repeat(angles.y, 360f),
                Mathf.Repeat(angles.z, 360f));
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude <= 0.00001f * 0.00001f;
        }

        private static bool Approximately(Vector4 left, Vector4 right)
        {
            return Mathf.Abs(left.x - right.x) <= 0.00001f &&
                   Mathf.Abs(left.y - right.y) <= 0.00001f &&
                   Mathf.Abs(left.z - right.z) <= 0.00001f &&
                   Mathf.Abs(left.w - right.w) <= 0.00001f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Color value)
        {
            return IsFinite(value.r) && IsFinite(value.g) && IsFinite(value.b) && IsFinite(value.a);
        }

        private static bool IsFinite(Vector4 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrWhiteSpace(assetFolder) ||
                (!string.Equals(assetFolder, "Assets", StringComparison.Ordinal) &&
                 !assetFolder.StartsWith("Assets/", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Refusing to create a non-Assets folder: '" + assetFolder + "'.");
            }
            if (!string.Equals(assetFolder, "Assets", StringComparison.Ordinal) &&
                !assetFolder.StartsWith(Root, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to write outside the isolated baked-basis PoC root: '" + assetFolder + "'.");
            }
            if (AssetDatabase.IsValidFolder(assetFolder))
                return;

            string[] segments = assetFolder.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string guid = AssetDatabase.CreateFolder(current, segments[i]);
                    if (string.IsNullOrWhiteSpace(guid) || !AssetDatabase.IsValidFolder(next))
                        throw new InvalidOperationException("Unable to create owned generated folder: '" + next + "'.");
                }
                current = next;
            }
        }

        private static string CreateStagingFolder()
        {
            return CreateStagingFolder(GeneratedRoot);
        }

        private static string CreateStagingFolder(string generatedRoot)
        {
            ValidateOwnedGeneratedRoot(generatedRoot);
            string token = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) + "_" +
                           Guid.NewGuid().ToString("N").Substring(0, 8);
            string path = generatedRoot + "/__Staging_" + token;
            EnsureFolder(path);
            return path;
        }

        private static void AssertNoStaleStagingFolders()
        {
            AssertNoStaleStagingFolders(GeneratedRoot);
        }

        private static void AssertNoStaleStagingFolders(string generatedRoot)
        {
            ValidateOwnedGeneratedRoot(generatedRoot);
            if (!AssetDatabase.IsValidFolder(generatedRoot))
                return;
            string[] children = AssetDatabase.GetSubFolders(generatedRoot);
            for (int i = 0; i < children.Length; i++)
            {
                string name = Path.GetFileName(children[i].Replace('/', Path.DirectorySeparatorChar));
                if (name.StartsWith("__Staging_", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "A previous generated staging payload was retained for inspection: '" + children[i] +
                        "'. This tool will not overwrite or delete it automatically.");
                }
            }
        }

        private static void ValidateOwnedGeneratedRoot(string generatedRoot)
        {
            if (!string.Equals(generatedRoot, GeneratedRoot, StringComparison.Ordinal) &&
                !string.Equals(generatedRoot, EndpointNativeGeneratedRoot, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Refusing generated-payload operation outside an explicitly owned root: '" +
                    generatedRoot + "'.");
            }
        }

        private static string ArchiveEndpointNativeFailedStaging(string stagingRoot)
        {
            string prefix = EndpointNativeGeneratedRoot + "/__Staging_";
            if (string.IsNullOrWhiteSpace(stagingRoot) ||
                !stagingRoot.StartsWith(prefix, StringComparison.Ordinal) ||
                !AssetDatabase.IsValidFolder(stagingRoot))
            {
                throw new InvalidOperationException(
                    "Refusing to archive an invalid endpoint-native staging folder: '" + stagingRoot + "'.");
            }
            string suffix = stagingRoot.Substring(prefix.Length);
            string failedRoot = EndpointNativeGeneratedRoot + "/__Failed_" + suffix;
            if (AssetDatabase.IsValidFolder(failedRoot))
            {
                failedRoot = EndpointNativeGeneratedRoot + "/__Failed_" + suffix + "_" +
                             Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            string error = AssetDatabase.MoveAsset(stagingRoot, failedRoot);
            if (!string.IsNullOrWhiteSpace(error))
                throw new InvalidOperationException("Unable to retain failed endpoint-native staging: " + error);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return failedRoot;
        }

        private static void WriteManifest(string payloadRoot, DungeonPortalBakedBasisGeneratedManifest manifest)
        {
            if (manifest == null || !string.Equals(manifest.schema, DungeonPortalBakedBasisGeneratedManifest.CurrentSchema, StringComparison.Ordinal))
                throw new InvalidOperationException("Generated manifest is null or has an invalid schema.");
            string assetPath = payloadRoot + "/" + ManifestFileName;
            if (File.Exists(AssetPathToPhysicalPath(assetPath)))
                throw new InvalidOperationException("Refusing to overwrite an existing staging manifest: '" + assetPath + "'.");
            string json = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(AssetPathToPhysicalPath(assetPath), json, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void WriteEndpointNativeManifest(
            string payloadRoot,
            EndpointNativeManifest manifest)
        {
            if (manifest == null ||
                !string.Equals(manifest.schema, EndpointNativeManifestSchema, StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.payloadRootName,
                    EndpointNativePayloadRootName,
                    StringComparison.Ordinal) ||
                (manifest.runtimeConsumerReady &&
                 (!string.Equals(
                      manifest.runtimeSchema,
                      DungeonPortalBakedRoomBasisData.CurrentSchema,
                      StringComparison.Ordinal) ||
                   !string.Equals(
                       manifest.receiverPosePolicy,
                       EndpointNativeReceiverPosePolicy,
                       StringComparison.Ordinal) ||
                   !string.Equals(
                       manifest.compactResponseEncoding,
                       EndpointNativeCompactResponseEncoding,
                       StringComparison.Ordinal))))
            {
                throw new InvalidOperationException(
                    "Endpoint-native manifest is null, has an invalid schema, or has an incomplete ready-consumer contract.");
            }
            string assetPath = payloadRoot + "/" + EndpointNativeManifestFileName;
            if (File.Exists(AssetPathToPhysicalPath(assetPath)))
            {
                throw new InvalidOperationException(
                    "Refusing to overwrite an existing endpoint-native staging manifest: '" + assetPath + "'.");
            }
            string json = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(AssetPathToPhysicalPath(assetPath), json, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static EndpointNativeManifest LoadEndpointNativeManifest(string payloadRoot)
        {
            string manifestPath = payloadRoot + "/" + EndpointNativeManifestFileName;
            string physicalManifest = AssetPathToPhysicalPath(manifestPath);
            if (!File.Exists(physicalManifest))
                throw new InvalidOperationException("Endpoint-native payload manifest is missing: '" + manifestPath + "'.");
            EndpointNativeManifest manifest = JsonUtility.FromJson<EndpointNativeManifest>(
                File.ReadAllText(physicalManifest));
            if (manifest == null)
                throw new InvalidOperationException("Endpoint-native payload manifest could not be deserialized.");
            return manifest;
        }

        private static bool TryVerifyEndpointNativePayload(
            string payloadRoot,
            bool expectedRuntimeConsumerReady,
            out string failure)
        {
            return TryVerifyEndpointNativeCompactPayload(
                payloadRoot,
                expectedRuntimeConsumerReady,
                out failure);
        }

        private static bool TryVerifyEndpointNativeCompactPayload(
            string payloadRoot,
            bool expectedRuntimeConsumerReady,
            out string failure)
        {
            try
            {
                if (!AssetDatabase.IsValidFolder(payloadRoot))
                    throw new InvalidOperationException("Endpoint-native payload folder is missing: '" + payloadRoot + "'.");
                EndpointNativeManifest manifest = LoadEndpointNativeManifest(payloadRoot);
                if (manifest == null ||
                    !string.Equals(manifest.schema, EndpointNativeManifestSchema, StringComparison.Ordinal) ||
                    !string.Equals(
                        manifest.payloadRootName,
                        EndpointNativePayloadRootName,
                        StringComparison.Ordinal) ||
                    !string.Equals(manifest.toolVersion, ToolVersion, StringComparison.Ordinal) ||
                    manifest.runtimeConsumerReady != expectedRuntimeConsumerReady ||
                    manifest.strictPersistedCaptureIntegrityClaimed ||
                    !manifest.productionInputsReadOnly ||
                     !string.Equals(manifest.basePolicy, EndpointNativeManifestBasePolicy, StringComparison.Ordinal) ||
                     !string.Equals(
                         manifest.responseMipPolicy,
                         EndpointNativeManifestResponseMipPolicy,
                         StringComparison.Ordinal) ||
                      !string.Equals(manifest.compactResponseEncoding,
                          EndpointNativeCompactResponseEncoding, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Endpoint-native compact manifest is incomplete or not the v2 normalized response contract.");
                }
                if (expectedRuntimeConsumerReady &&
                    (!string.Equals(manifest.toolVersion, ToolVersion, StringComparison.Ordinal) ||
                     string.IsNullOrWhiteSpace(manifest.publishedUtcIso8601) &&
                     string.IsNullOrWhiteSpace(manifest.generatedUtcIso8601) ||
                     !string.Equals(
                         manifest.runtimeSchema,
                         DungeonPortalBakedRoomBasisData.CurrentSchema,
                         StringComparison.Ordinal) ||
                     !string.Equals(
                         manifest.receiverPosePolicy,
                         EndpointNativeReceiverPosePolicy,
                         StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        "Endpoint-native ready manifest does not identify the DPBB-2 angle-pose publication contract.");
                }
                if (!string.Equals(manifest.chartCopyShaderPath, ChartCopyShaderPath, StringComparison.Ordinal) ||
                    !string.Equals(
                        manifest.chartCopyShaderDependencyHash,
                        GetDependencyHash(ChartCopyShaderPath),
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Endpoint-native chart-copy shader fingerprint no longer matches.");
                }

                InputFingerprint[] inputs = manifest.inputs ?? Array.Empty<InputFingerprint>();
                if (inputs.Length == 0)
                    throw new InvalidOperationException("Endpoint-native input fingerprints are missing.");
                for (int i = 0; i < inputs.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(inputs[i].assetPath) ||
                        string.IsNullOrWhiteSpace(inputs[i].dependencyHash) ||
                        AssetDatabase.LoadMainAssetAtPath(inputs[i].assetPath) == null ||
                        !string.Equals(
                            inputs[i].dependencyHash,
                            GetDependencyHash(inputs[i].assetPath),
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Endpoint-native input fingerprint changed at index " + i + ".");
                    }
                }

                AngleCaptureIntegrityRecord[] captures = manifest.angleCaptureIntegrity ??
                    Array.Empty<AngleCaptureIntegrityRecord>();
                if (captures.Length != EndpointNativeRoomSpecs.Length)
                    throw new InvalidOperationException("Endpoint-native angle-capture integrity records are incomplete.");
                for (int i = 0; i < captures.Length; i++)
                    VerifyAngleCaptureIntegrityRecord(captures[i]);

                ArtifactFingerprint[] artifacts = manifest.artifacts ?? Array.Empty<ArtifactFingerprint>();
                if (artifacts.Length == 0)
                    throw new InvalidOperationException("Endpoint-native response artifact fingerprints are missing.");
                VerifyEndpointNativeCompactArtifactContract(manifest, artifacts);
                for (int i = 0; i < artifacts.Length; i++)
                    VerifyArtifactFingerprint(payloadRoot, artifacts[i]);

                EndpointNativeRoomRecord[] rooms = manifest.rooms ?? Array.Empty<EndpointNativeRoomRecord>();
                if (rooms.Length != EndpointNativeRoomSpecs.Length)
                {
                    throw new InvalidOperationException(
                        "Endpoint-native manifest does not contain exactly the two isolated room records.");
                }
                for (int i = 0; i < rooms.Length; i++)
                    VerifyEndpointNativeCompactRoomRecord(
                        payloadRoot,
                        rooms[i],
                        expectedRuntimeConsumerReady);

                failure = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        private static void VerifyAngleCaptureIntegrityRecord(AngleCaptureIntegrityRecord record)
        {
            if (record == null || !record.structuralValidationPassed ||
                record.captureSchemaVersion != DungeonPortalBakedBasisDoorAngleCapture.CurrentSchemaVersion ||
                string.IsNullOrWhiteSpace(record.receiverRoomId) ||
                !string.Equals(record.angleCaptureAssetPath, GetAngleCaptureAssetPath(record.receiverRoomId),
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(record.angleCaptureDependencyHash) ||
                !string.Equals(record.angleCaptureDependencyHash, GetDependencyHash(record.angleCaptureAssetPath),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Endpoint-native angle-capture integrity fingerprint is invalid.");
            }
            DungeonPortalBakedBasisDoorAngleCapture capture =
                LoadRequired<DungeonPortalBakedBasisDoorAngleCapture>(record.angleCaptureAssetPath);
            if (!capture.TryValidate(out string failure) ||
                !string.Equals(capture.ReceiverRoomId, record.receiverRoomId, StringComparison.Ordinal) ||
                !string.Equals(capture.StableDoorwayId, StableDoorwayId, StringComparison.Ordinal) ||
                !string.Equals(capture.Provenance.canonicalWorkspaceDependencyHash,
                    record.canonicalWorkspaceDependencyHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Endpoint-native angle capture no longer validates: " + failure);
            }
            AngleCapturePoseIntegrityRecord[] poses = record.poses ?? Array.Empty<AngleCapturePoseIntegrityRecord>();
            DungeonPortalBakedBasisDoorAngleCapture.AngleState[] baselines = capture.MatchedBaselineAngleStates;
            DungeonPortalBakedBasisDoorAngleCapture.AngleState[] full = capture.FullAngleStates;
            if (poses.Length != RequiredAnglePoseCount || baselines.Length != RequiredAnglePoseCount ||
                full.Length != RequiredAnglePoseCount)
            {
                throw new InvalidOperationException("Endpoint-native angle-capture integrity pose cardinality drifted.");
            }
            for (int i = 0; i < RequiredAnglePoseCount; i++)
            {
                AngleCapturePoseIntegrityRecord pose = poses[i];
                if (pose == null || !string.Equals(pose.poseId, PoseIdForIndex(i), StringComparison.Ordinal) ||
                    !Mathf.Approximately(pose.openFraction, full[i].openFraction) ||
                    !Mathf.Approximately(pose.angleDegrees, full[i].angleDegrees) ||
                    !string.Equals(pose.baselineStateId, baselines[i].stateId, StringComparison.Ordinal) ||
                    !string.Equals(pose.baselineStateHash, baselines[i].stateHash, StringComparison.Ordinal) ||
                    !string.Equals(pose.fullStateId, full[i].stateId, StringComparison.Ordinal) ||
                    !string.Equals(pose.fullStateHash, full[i].stateHash, StringComparison.Ordinal) ||
                    !string.Equals(pose.probeStencilPolicy, full[i].probeStencilPolicy, StringComparison.Ordinal) ||
                    !string.Equals(pose.probeLocalPositionSignature, full[i].probeLocalPositionSignature,
                        StringComparison.Ordinal) ||
                    !string.Equals(pose.firstNineBaselineShSignature, ComputeFirstNineProbeShSignature(baselines[i]),
                        StringComparison.Ordinal) ||
                    !string.Equals(pose.firstNineFullShSignature, ComputeFirstNineProbeShSignature(full[i]),
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Endpoint-native angle-capture integrity pose drifted at " +
                                                        PoseIdForIndex(i) + ".");
                }
            }
        }

        private static void VerifyEndpointNativeCompactArtifactContract(
            EndpointNativeManifest manifest,
            ArtifactFingerprint[] artifacts)
        {
            var responsePaths = new HashSet<string>(StringComparer.Ordinal);
            var transitionPaths = new HashSet<string>(StringComparer.Ordinal);
            var basisPaths = new HashSet<string>(StringComparer.Ordinal);
            EndpointNativeRoomRecord[] rooms = manifest.rooms ?? Array.Empty<EndpointNativeRoomRecord>();
            for (int room = 0; room < rooms.Length; room++)
            {
                EndpointNativeRoomRecord roomRecord = rooms[room];
                if (roomRecord == null || string.IsNullOrWhiteSpace(roomRecord.basisAssetRelativePath) ||
                    !basisPaths.Add(roomRecord.basisAssetRelativePath))
                {
                    throw new InvalidOperationException("Endpoint-native compact basis artifact declaration is invalid.");
                }
                EndpointNativeEndpointRecord[] endpoints = roomRecord.endpoints ??
                    Array.Empty<EndpointNativeEndpointRecord>();
                for (int endpoint = 0; endpoint < endpoints.Length; endpoint++)
                {
                    EndpointNativeEndpointRecord endpointRecord = endpoints[endpoint];
                    if (endpointRecord == null || (endpointRecord.atlases ?? Array.Empty<EndpointNativeAtlasRecord>()).Length != 0)
                    {
                        throw new InvalidOperationException("Endpoint-native compact verifier rejects legacy flat response atlases.");
                    }
                    for (int pose = 0; pose < RequiredAnglePoseCount; pose++)
                    {
                        EndpointNativePoseRecord poseRecord = GetEndpointNativePoseRecord(endpointRecord, pose);
                        EndpointNativeAtlasRecord[] atlases = poseRecord.atlases ?? Array.Empty<EndpointNativeAtlasRecord>();
                        for (int atlas = 0; atlas < atlases.Length; atlas++)
                        {
                            EndpointNativeAtlasRecord value = atlases[atlas];
                            if (value == null || !responsePaths.Add(value.colorPositiveRelativePath) ||
                                !responsePaths.Add(value.colorNegativeRelativePath) ||
                                !responsePaths.Add(value.directionalMomentPositiveRelativePath) ||
                                !responsePaths.Add(value.directionalMomentNegativeRelativePath))
                            {
                                throw new InvalidOperationException("Endpoint-native compact response paths are missing or duplicate.");
                            }
                            bool colorTransitionAdded = transitionPaths.Add(value.oppositeBaseColorRelativePath);
                            bool directionTransitionAdded = transitionPaths.Add(value.oppositeBaseDirectionRelativePath);
                            if ((pose == 0 && (!colorTransitionAdded || !directionTransitionAdded)) ||
                                (pose != 0 && (colorTransitionAdded || directionTransitionAdded)))
                            {
                                throw new InvalidOperationException(
                                    "Endpoint-native base-transition paths must be unique at D025 and exactly reused by later poses.");
                            }
                        }
                    }
                }
            }
            if (responsePaths.Count == 0 || transitionPaths.Count == 0 ||
                basisPaths.Count != EndpointNativeRoomSpecs.Length)
                throw new InvalidOperationException("Endpoint-native compact artifact declaration has an empty dynamic set.");
            artifacts = artifacts ?? Array.Empty<ArtifactFingerprint>();
            var artifactPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (ArtifactFingerprint artifact in artifacts)
            {
                if (artifact == null || string.IsNullOrWhiteSpace(artifact.relativePath) || !artifactPaths.Add(artifact.relativePath))
                    throw new InvalidOperationException("Endpoint-native compact artifact fingerprints contain a duplicate path.");
            }
            var expected = new HashSet<string>(responsePaths, StringComparer.Ordinal);
            expected.UnionWith(transitionPaths);
            expected.UnionWith(basisPaths);
            if (!artifactPaths.SetEquals(expected))
            {
                throw new InvalidOperationException(
                    "Endpoint-native compact artifact fingerprints are not exactly the dynamic response, transition, and basis set.");
            }
        }

        private static void VerifyEndpointNativeCompactRoomRecord(
            string payloadRoot,
            EndpointNativeRoomRecord record,
            bool runtimeConsumerReady)
        {
            RoomSpec spec = EndpointNativeRoomSpecs.FirstOrDefault(value => record != null &&
                string.Equals(value.RoomId, record.roomId, StringComparison.Ordinal));
            if (spec == null || !string.Equals(record.productionPrefabPath, spec.ProductionPrefabPath, StringComparison.Ordinal) ||
                !string.Equals(record.p0BakeDataPath, spec.P0BakeDataPath, StringComparison.Ordinal) ||
                !string.Equals(record.p100BakeDataPath, spec.P100BakeDataPath, StringComparison.Ordinal) ||
                !string.Equals(record.angleCaptureAssetPath, GetAngleCaptureAssetPath(spec.RoomId), StringComparison.Ordinal) ||
                !string.Equals(record.angleCaptureDependencyHash, GetDependencyHash(record.angleCaptureAssetPath),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Endpoint-native compact room record uses an unexpected dependency.");
            }
            ResolvedRoom room = ResolveEndpointNativeRoom(spec);
            if (!string.Equals(record.canonicalRendererMappingSignature,
                    ComputeCanonicalMappingSignature(room.CanonicalRenderers), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Endpoint-native compact renderer mapping signature drifted for '" + record.roomId + "'.");
            }
            EndpointNativeRendererRecord[] mapping = record.rendererMappings ?? Array.Empty<EndpointNativeRendererRecord>();
            if (mapping.Length != room.CanonicalRenderers.Length)
                throw new InvalidOperationException("Endpoint-native compact renderer mapping count drifted.");
            for (int i = 0; i < mapping.Length; i++)
            {
                CanonicalRendererWork expected = room.CanonicalRenderers[i];
                EndpointNativeRendererRecord actual = mapping[i];
                if (actual == null || !string.Equals(actual.canonicalKey, expected.CanonicalKey, StringComparison.Ordinal) ||
                    actual.p0LocalLightmapIndex != expected.P0.lightmapIndex ||
                    actual.p100LocalLightmapIndex != expected.P100.lightmapIndex ||
                    !MatchesVector4(actual.p0ScaleOffset, expected.P0.lightmapScaleOffset) ||
                    !MatchesVector4(actual.p100ScaleOffset, expected.P100.lightmapScaleOffset))
                {
                    throw new InvalidOperationException("Endpoint-native compact renderer mapping drifted at " + i + ".");
                }
            }
            EndpointNativeEndpointRecord[] endpoints = record.endpoints ?? Array.Empty<EndpointNativeEndpointRecord>();
            if (endpoints.Length != 2)
                throw new InvalidOperationException("Endpoint-native compact room requires exactly P0/P100 endpoints.");
            VerifyEndpointNativeCompactEndpointRecord(payloadRoot, room, endpoints[0], EndpointLayout.Power0);
            VerifyEndpointNativeCompactEndpointRecord(payloadRoot, room, endpoints[1], EndpointLayout.Power100);
            if (runtimeConsumerReady)
            {
                if (string.IsNullOrWhiteSpace(record.basisAssetRelativePath) ||
                    !string.Equals(record.basisDependencyHash,
                        GetDependencyHash(CombinePayloadPath(payloadRoot, record.basisAssetRelativePath)), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Endpoint-native compact room basis fingerprint is invalid.");
                }
                VerifyEndpointNativeAngleBasisDefinition(room, record, payloadRoot,
                    LoadRequired<DungeonPortalBakedRoomBasisData>(CombinePayloadPath(payloadRoot, record.basisAssetRelativePath)));
            }
        }

        private static void VerifyEndpointNativeCompactEndpointRecord(
            string payloadRoot,
            ResolvedRoom room,
            EndpointNativeEndpointRecord endpoint,
            EndpointLayout layout)
        {
            string endpointId = layout == EndpointLayout.Power100 ? "P100" : "P0";
            if (endpoint == null || !endpoint.exactBaseReferences ||
                !string.Equals(endpoint.endpointId, endpointId, StringComparison.Ordinal) ||
                !string.Equals(endpoint.baseTransitionPolicy,
                    EndpointNativeEndpointBaseTransitionPolicy, StringComparison.Ordinal) ||
                !string.Equals(endpoint.responseMipPolicy,
                    EndpointNativeEndpointResponseMipPolicy, StringComparison.Ordinal) ||
                (endpoint.atlases ?? Array.Empty<EndpointNativeAtlasRecord>()).Length != 0)
            {
                throw new InvalidOperationException("Endpoint-native compact " + endpointId + " record is incomplete or legacy.");
            }
            Texture2D[] colors = layout == EndpointLayout.Power100 ? room.P100.lightmapColors : room.P0.lightmapColors;
            Texture2D[] directions = layout == EndpointLayout.Power100 ? room.P100.lightmapDirections : room.P0.lightmapDirections;
            DungeonPortalBakedBasisDoorAngleCapture.AngleState[] baselines =
                room.AngleCapture.MatchedBaselineAngleStates;
            DungeonPortalBakedBasisDoorAngleCapture.AngleState[] full = room.AngleCapture.FullAngleStates;
            for (int poseIndex = 0; poseIndex < RequiredAnglePoseCount; poseIndex++)
            {
                EndpointNativePoseRecord pose = GetEndpointNativePoseRecord(endpoint, poseIndex);
                if (!Mathf.Approximately(pose.openFraction, full[poseIndex].openFraction) ||
                    !Mathf.Approximately(pose.angleDegrees, full[poseIndex].angleDegrees) ||
                    !string.Equals(pose.baselineStateId, baselines[poseIndex].stateId, StringComparison.Ordinal) ||
                    !string.Equals(pose.baselineStateHash, baselines[poseIndex].stateHash, StringComparison.Ordinal) ||
                    !string.Equals(pose.fullStateId, full[poseIndex].stateId, StringComparison.Ordinal) ||
                    !string.Equals(pose.fullStateHash, full[poseIndex].stateHash, StringComparison.Ordinal) ||
                    !string.Equals(pose.probeStencilPolicy, full[poseIndex].probeStencilPolicy, StringComparison.Ordinal) ||
                    !string.Equals(pose.probeLocalPositionSignature, full[poseIndex].probeLocalPositionSignature,
                        StringComparison.Ordinal) ||
                    !string.Equals(pose.firstNineBaselineShSignature,
                        ComputeFirstNineProbeShSignature(baselines[poseIndex]), StringComparison.Ordinal) ||
                    !string.Equals(pose.firstNineFullShSignature,
                        ComputeFirstNineProbeShSignature(full[poseIndex]), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Endpoint-native compact pose provenance drifted for " +
                                                        endpointId + " " + PoseIdForIndex(poseIndex) + ".");
                }
                EndpointNativeAtlasRecord[] atlases = pose.atlases ?? Array.Empty<EndpointNativeAtlasRecord>();
                if (atlases.Length != colors.Length || colors.Length != directions.Length)
                    throw new InvalidOperationException("Endpoint-native compact atlas cardinality changed for " + endpointId + ".");
                for (int atlas = 0; atlas < atlases.Length; atlas++)
                {
                    VerifyEndpointNativeCompactAtlasRecord(
                        payloadRoot, room, endpointId, pose, atlases[atlas], atlas, colors[atlas], directions[atlas]);
                }
            }
        }

        private static void VerifyEndpointNativeCompactAtlasRecord(
            string payloadRoot,
            ResolvedRoom room,
            string endpointId,
            EndpointNativePoseRecord pose,
            EndpointNativeAtlasRecord atlas,
            int expectedIndex,
            Texture2D colorReference,
            Texture2D directionReference)
        {
            int expectedOperations = room.CanonicalRenderers.Count(renderer =>
                (string.Equals(endpointId, "P100", StringComparison.Ordinal)
                    ? renderer.P100.lightmapIndex : renderer.P0.lightmapIndex) == expectedIndex);
            if (atlas == null || atlas.localLightmapIndex != expectedIndex ||
                !string.Equals(atlas.baseColorAssetPath, AssetDatabase.GetAssetPath(colorReference), StringComparison.Ordinal) ||
                !string.Equals(atlas.baseColorDependencyHash,
                    GetDependencyHash(AssetDatabase.GetAssetPath(colorReference)), StringComparison.Ordinal) ||
                !string.Equals(atlas.baseDirectionAssetPath, AssetDatabase.GetAssetPath(directionReference), StringComparison.Ordinal) ||
                !string.Equals(atlas.baseDirectionDependencyHash,
                    GetDependencyHash(AssetDatabase.GetAssetPath(directionReference)), StringComparison.Ordinal) ||
                atlas.width != colorReference.width || atlas.height != colorReference.height ||
                atlas.baseColorMipmapCount != colorReference.mipmapCount ||
                atlas.baseDirectionMipmapCount != directionReference.mipmapCount ||
                atlas.oppositeBaseMipmapCount != colorReference.mipmapCount ||
                atlas.oppositeBaseOperationCount != expectedOperations ||
                atlas.oppositeBaseOperationCount <= 0 ||
                atlas.oppositeBaseOwnedCoreTexelCount <= 0 ||
                atlas.oppositeBaseImplicitZeroCoreTexelCount < 0 ||
                atlas.oppositeBaseImplicitZeroCoreTexelCount > atlas.oppositeBaseOwnedCoreTexelCount ||
                atlas.oppositeBaseOwnerCollisionCount != 0 ||
                atlas.oppositeBaseCanonicalOrientationProofSampledTexelCount !=
                    atlas.oppositeBaseOwnedCoreTexelCount - atlas.oppositeBaseImplicitZeroCoreTexelCount ||
                !IsFinite(atlas.oppositeBaseCanonicalOrientationProofMaxAbsoluteChannelError) ||
                atlas.oppositeBaseCanonicalOrientationProofMaxAbsoluteChannelError < 0f ||
                atlas.oppositeBaseCanonicalOrientationProofMaxAbsoluteChannelError >
                    CanonicalOrientationProofMaxAbsoluteError ||
                atlas.responseMipmapCount != colorReference.mipmapCount ||
                atlas.operationCount != expectedOperations || atlas.operationCount <= 0 ||
                atlas.baselineOwnedCoreTexelCount <= 0 || atlas.fullOwnedCoreTexelCount <= 0 ||
                atlas.baselineOwnedCoreTexelCount != atlas.fullOwnedCoreTexelCount ||
                atlas.baselineImplicitZeroCoreTexelCount < 0 ||
                atlas.baselineImplicitZeroCoreTexelCount > atlas.baselineOwnedCoreTexelCount ||
                atlas.fullImplicitZeroCoreTexelCount < 0 ||
                atlas.fullImplicitZeroCoreTexelCount > atlas.fullOwnedCoreTexelCount ||
                atlas.baselineImplicitZeroCoreTexelCount != atlas.fullImplicitZeroCoreTexelCount ||
                atlas.baselineOwnerCollisionCount != 0 || atlas.fullOwnerCollisionCount != 0 ||
                !string.Equals(atlas.responseEncoding,
                    "NormalizedPositiveNegativeDeltaCanonicalPositiveZeroSelectiveCompactPairFallbacks",
                    StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(atlas.colorDeltaRelativePath) ||
                !string.IsNullOrEmpty(atlas.directionalMomentDeltaRelativePath) ||
                !AreValidCompactScales(atlas.colorPositiveScales, colorReference.mipmapCount) ||
                !AreValidCompactScales(atlas.colorNegativeScales, colorReference.mipmapCount) ||
                !AreValidCompactScales(atlas.directionalMomentPositiveScales, directionReference.mipmapCount) ||
                !AreValidCompactScales(atlas.directionalMomentNegativeScales, directionReference.mipmapCount) ||
                !AreValidCompactReconstruction(atlas.colorReconstructionByMip, colorReference.mipmapCount, false) ||
                !AreValidCompactReconstruction(atlas.directionalMomentReconstructionByMip,
                    directionReference.mipmapCount, true))
            {
                throw new InvalidOperationException("Endpoint-native compact atlas contract failed for " +
                                                    endpointId + " " + pose.poseId + " LM" + expectedIndex + ".");
            }
            string root = room.Spec.RoomId + "/Endpoints/" + endpointId + "/ReceiverBasis/Poses/" + pose.poseId +
                          "/NormalizedSignedDelta/LM" + expectedIndex.ToString("00", CultureInfo.InvariantCulture);
            TextureFormat colorStorageFormat = ParseCompactColorStorageFormat(atlas.colorStorageFormat,
                endpointId + " " + pose.poseId + " LM" + expectedIndex);
            TextureFormat momentStorageFormat = ParseCompactMomentStorageFormat(
                atlas.directionalMomentStorageFormat,
                endpointId + " " + pose.poseId + " LM" + expectedIndex);
            VerifyEndpointNativeCompactTexture(payloadRoot, atlas.colorPositiveRelativePath,
                root + "_ColorPositive.asset", colorReference, colorStorageFormat, "positive color");
            VerifyEndpointNativeCompactTexture(payloadRoot, atlas.colorNegativeRelativePath,
                root + "_ColorNegative.asset", colorReference, colorStorageFormat, "negative color");
            VerifyEndpointNativeCompactTexture(payloadRoot, atlas.directionalMomentPositiveRelativePath,
                root + "_DirectionalMomentPositive.asset", directionReference, momentStorageFormat,
                "positive directional moment");
            VerifyEndpointNativeCompactTexture(payloadRoot, atlas.directionalMomentNegativeRelativePath,
                root + "_DirectionalMomentNegative.asset", directionReference, momentStorageFormat,
                "negative directional moment");
            string transitionRoot = room.Spec.RoomId + "/Endpoints/" + endpointId + "/BaseTransition/OppositeEndpoint/LM" +
                                    expectedIndex.ToString("00", CultureInfo.InvariantCulture);
            TextureFormat oppositeBaseColorStorageFormat = ParseBaseTransitionColorStorageFormat(
                atlas.oppositeBaseColorStorageFormat,
                endpointId + " " + pose.poseId + " LM" + expectedIndex);
            VerifyEndpointNativeBaseTransitionTexture(payloadRoot, atlas.oppositeBaseColorRelativePath,
                transitionRoot + "_Color.asset", colorReference, oppositeBaseColorStorageFormat,
                endpointId + " opposite base color");
            VerifyEndpointNativeBaseTransitionTexture(payloadRoot, atlas.oppositeBaseDirectionRelativePath,
                transitionRoot + "_Direction.asset", directionReference, TextureFormat.RGBA32, endpointId + " opposite base direction");
        }

        private static TextureFormat ParseCompactColorStorageFormat(string serializedFormat, string label)
        {
            if (!Enum.TryParse(serializedFormat, out TextureFormat format) ||
                (format != TextureFormat.BC6H && format != TextureFormat.RGBAHalf))
            {
                throw new InvalidOperationException(
                    "Endpoint-native compact color pair has an unsupported storage format for " +
                    label + ": '" + serializedFormat + "'.");
            }
            return format;
        }

        private static TextureFormat ParseCompactMomentStorageFormat(string serializedFormat, string label)
        {
            if (!Enum.TryParse(serializedFormat, out TextureFormat format) ||
                (format != TextureFormat.BC7 && format != TextureFormat.RGBAHalf))
            {
                throw new InvalidOperationException(
                    "Endpoint-native compact directional-moment pair has an unsupported storage format for " +
                    label + ": '" + serializedFormat + "'.");
            }
            return format;
        }

        private static TextureFormat ParseBaseTransitionColorStorageFormat(
            string serializedFormat,
            string label)
        {
            if (!Enum.TryParse(serializedFormat, out TextureFormat format) ||
                (format != TextureFormat.BC6H && format != TextureFormat.RGBAHalf))
            {
                throw new InvalidOperationException(
                    "Endpoint-native opposite-base color has an unsupported storage format for " +
                    label + ": '" + serializedFormat + "'.");
            }
            return format;
        }

        private static void VerifyEndpointNativeCompactTexture(
            string payloadRoot,
            string relativePath,
            string expectedRelativePath,
            Texture2D reference,
            TextureFormat expectedFormat,
            string label)
        {
            if (!string.Equals(relativePath, expectedRelativePath, StringComparison.Ordinal))
                throw new InvalidOperationException("Endpoint-native compact texture path is non-deterministic for " + label + ".");
            Texture2D texture = LoadRequired<Texture2D>(CombinePayloadPath(payloadRoot, relativePath));
            ValidateSampleTexture(texture, label, expectedFormat == TextureFormat.BC7);
            if (texture.format != expectedFormat || texture.width != reference.width || texture.height != reference.height ||
                texture.mipmapCount != reference.mipmapCount || texture.filterMode != reference.filterMode ||
                texture.wrapModeU != reference.wrapModeU || texture.wrapModeV != reference.wrapModeV ||
                texture.wrapModeW != reference.wrapModeW || texture.anisoLevel != reference.anisoLevel ||
                !Mathf.Approximately(texture.mipMapBias, reference.mipMapBias))
            {
                throw new InvalidOperationException("Endpoint-native compact texture violates native sampling contract for " + label + ".");
            }
        }

        private static bool AreValidCompactScales(Vector4[] scales, int mipCount)
        {
            if (scales == null || scales.Length != mipCount || mipCount <= 1 ||
                mipCount > DungeonPortalBakedRoomBasisData.NormalizedDeltaMaximumMipCount)
                return false;
            for (int mip = 0; mip < scales.Length; mip++)
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    if (!IsFinite(scales[mip][channel]) ||
                        scales[mip][channel] < DungeonPortalBakedRoomBasisData.NormalizedDeltaZeroChannelScale)
                        return false;
                }
            }
            return true;
        }

        private static bool AreValidCompactReconstruction(
            CompactDeltaReconstructionStats[] stats,
            int mipCount,
            bool directionalMoment)
        {
            if (stats == null || stats.Length != mipCount)
                return false;
            for (int mip = 0; mip < stats.Length; mip++)
            {
                CompactDeltaReconstructionStats value = stats[mip];
                if (value == null || value.mipLevel != mip || value.ownedTexelCount <= 0 ||
                    value.channelSampleCount != value.ownedTexelCount * (directionalMoment ? 4 : 3) ||
                    value.wrongSignCount != 0 || !IsFinite(value.absoluteRmse) || !IsFinite(value.relativeRmse) ||
                    !IsFinite(value.absoluteMax) || value.absoluteRmse < 0f || value.relativeRmse < 0f ||
                    value.absoluteMax < 0f)
                    return false;
            }
            return true;
        }

        private static void VerifyEndpointNativeDeltaArtifactContract(
            EndpointNativeManifest manifest,
            ArtifactFingerprint[] artifacts)
        {
            var expectedDeltaPaths = new HashSet<string>(StringComparer.Ordinal);
            var expectedBaseTransitionPaths = new HashSet<string>(StringComparer.Ordinal);
            EndpointNativeRoomRecord[] rooms = manifest.rooms ??
                Array.Empty<EndpointNativeRoomRecord>();
            for (int roomIndex = 0; roomIndex < rooms.Length; roomIndex++)
            {
                EndpointNativeEndpointRecord[] endpoints = rooms[roomIndex]?.endpoints ??
                    Array.Empty<EndpointNativeEndpointRecord>();
                for (int endpointIndex = 0; endpointIndex < endpoints.Length; endpointIndex++)
                {
                    EndpointNativeAtlasRecord[] atlases = endpoints[endpointIndex]?.atlases ??
                        Array.Empty<EndpointNativeAtlasRecord>();
                    for (int atlasIndex = 0; atlasIndex < atlases.Length; atlasIndex++)
                    {
                        EndpointNativeAtlasRecord atlas = atlases[atlasIndex];
                        if (atlas == null ||
                            !expectedDeltaPaths.Add(atlas.colorDeltaRelativePath) ||
                            !expectedDeltaPaths.Add(atlas.directionalMomentDeltaRelativePath) ||
                            !expectedBaseTransitionPaths.Add(atlas.oppositeBaseColorRelativePath) ||
                            !expectedBaseTransitionPaths.Add(atlas.oppositeBaseDirectionRelativePath))
                        {
                            throw new InvalidOperationException(
                                "Endpoint-native records contain a missing/duplicate response or " +
                                "base-transition texture path.");
                        }
                    }
                }
            }
            if (expectedDeltaPaths.Count == 0)
            {
                throw new InvalidOperationException(
                    "Endpoint-native response manifest contains " + expectedDeltaPaths.Count +
                    " delta paths; at least one declared response path is required.");
            }
            if (expectedBaseTransitionPaths.Count == 0)
            {
                throw new InvalidOperationException(
                    "Endpoint-native manifest contains " + expectedBaseTransitionPaths.Count +
                    " base-transition texture paths; at least one declared transition path is required.");
            }

            artifacts = artifacts ?? Array.Empty<ArtifactFingerprint>();
            var artifactPaths = new HashSet<string>(StringComparer.Ordinal);
            var textureArtifactPaths = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < artifacts.Length; i++)
            {
                ArtifactFingerprint artifact = artifacts[i];
                if (artifact == null || string.IsNullOrWhiteSpace(artifact.relativePath) ||
                    !artifactPaths.Add(artifact.relativePath))
                {
                    throw new InvalidOperationException(
                        "Endpoint-native artifact fingerprints contain a missing or duplicate path.");
                }
                if (artifact.width > 0 || artifact.height > 0)
                    textureArtifactPaths.Add(artifact.relativePath);
            }
            var expectedTexturePaths = new HashSet<string>(expectedDeltaPaths, StringComparer.Ordinal);
            expectedTexturePaths.UnionWith(expectedBaseTransitionPaths);
            if (!textureArtifactPaths.SetEquals(expectedTexturePaths))
            {
                throw new InvalidOperationException(
                    "Endpoint-native texture fingerprints are not exactly the declared response " +
                    "and dual-layout base-transition artifacts.");
            }

            int expectedArtifactCount = expectedDeltaPaths.Count +
                                        expectedBaseTransitionPaths.Count +
                                        (manifest.runtimeConsumerReady ? EndpointNativeRoomSpecs.Length : 0);
            if (artifacts.Length != expectedArtifactCount)
            {
                throw new InvalidOperationException(
                    "Endpoint-native artifact fingerprint count is " + artifacts.Length +
                    "; expected " + expectedArtifactCount + ".");
            }
        }

        private static void VerifyEndpointNativePublicationCandidate(
            string publicationStagingRoot,
            EndpointNativeManifest expectedManifest)
        {
            EndpointNativeManifest candidate = LoadEndpointNativeManifest(publicationStagingRoot);
            if (candidate == null || !candidate.runtimeConsumerReady ||
                !string.Equals(candidate.schema, EndpointNativeManifestSchema, StringComparison.Ordinal) ||
                !string.Equals(
                    candidate.payloadRootName,
                    EndpointNativePayloadRootName,
                    StringComparison.Ordinal) ||
                !string.Equals(candidate.toolVersion, ToolVersion, StringComparison.Ordinal) ||
                !string.Equals(
                    candidate.runtimeSchema,
                    DungeonPortalBakedRoomBasisData.CurrentSchema,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    candidate.receiverPosePolicy,
                    EndpointNativeReceiverPosePolicy,
                    StringComparison.Ordinal) ||
                candidate.rooms == null || expectedManifest == null ||
                candidate.rooms.Length != expectedManifest.rooms.Length)
            {
                throw new InvalidOperationException(
                    "Endpoint-native publication staging manifest lost its DPBB-2 ready contract.");
            }

            VerifyEndpointNativeDeltaArtifactContract(candidate, candidate.artifacts);
            for (int i = 0; i < candidate.rooms.Length; i++)
            {
                EndpointNativeRoomRecord record = candidate.rooms[i];
                RoomSpec spec = EndpointNativeRoomSpecs.FirstOrDefault(
                    value => record != null &&
                             string.Equals(value.RoomId, record.roomId, StringComparison.Ordinal));
                if (spec == null)
                    throw new InvalidOperationException("Publication staging contains an unexpected room record.");
                ResolvedRoom room = ResolveEndpointNativeRoom(spec);
                VerifyEndpointNativeRoomBasisRecord(
                    publicationStagingRoot,
                    EndpointNativeGeneratedRoot,
                    room,
                    record);

                ArtifactFingerprint fingerprint = (candidate.artifacts ??
                    Array.Empty<ArtifactFingerprint>()).SingleOrDefault(
                    value => value != null &&
                             string.Equals(
                                 value.relativePath,
                                 record.basisAssetRelativePath,
                                 StringComparison.Ordinal));
                if (fingerprint == null ||
                    !string.Equals(
                        fingerprint.kind,
                        "EndpointNativeDPBB2RoomBasisData",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Publication staging is missing the room-basis artifact fingerprint for '" +
                        record.roomId + "'.");
                }
                VerifyArtifactFingerprint(publicationStagingRoot, fingerprint);
            }
        }

        private static void VerifyEndpointNativeRoomBasisRecord(
            string basisAssetRoot,
            string responsePayloadRoot,
            ResolvedRoom source,
            EndpointNativeRoomRecord record)
        {
            string expectedRelativePath =
                record.roomId + "/" + record.roomId + "_RoomBasis.asset";
            if (!string.Equals(
                    record.basisAssetRelativePath,
                    expectedRelativePath,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(record.basisDependencyHash))
            {
                throw new InvalidOperationException(
                    "Endpoint-native room-basis path/hash contract is incomplete for '" +
                    record.roomId + "'.");
            }
            string basisPath = CombinePayloadPath(basisAssetRoot, record.basisAssetRelativePath);
            DungeonPortalBakedRoomBasisData basis =
                LoadRequired<DungeonPortalBakedRoomBasisData>(basisPath);
            if (!string.Equals(
                    record.basisDependencyHash,
                    GetDependencyHash(basisPath),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Endpoint-native room-basis dependency hash changed for '" + record.roomId + "'.");
            }
            VerifyEndpointNativeBasisDefinition(source, record, responsePayloadRoot, basis);
        }

        private static void VerifyEndpointNativeRoomRecord(
            string payloadRoot,
            EndpointNativeRoomRecord record,
            bool runtimeConsumerReady)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.roomId))
                throw new InvalidOperationException("Endpoint-native room record is incomplete.");
            RoomSpec spec = EndpointNativeRoomSpecs.FirstOrDefault(
                value => string.Equals(value.RoomId, record.roomId, StringComparison.Ordinal));
            if (spec == null ||
                !string.Equals(record.productionPrefabPath, spec.ProductionPrefabPath, StringComparison.Ordinal) ||
                !string.Equals(record.p0BakeDataPath, spec.P0BakeDataPath, StringComparison.Ordinal) ||
                !string.Equals(record.p100BakeDataPath, spec.P100BakeDataPath, StringComparison.Ordinal) ||
                !string.Equals(record.receiverCapturePath, spec.ReceiverCapturePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Endpoint-native room record refers to an unexpected input for '" + record.roomId + "'.");
            }

            ResolvedRoom source = ResolveEndpointNativeRoom(spec);
            if (!string.Equals(
                    record.canonicalRendererMappingSignature,
                    ComputeCanonicalMappingSignature(source.CanonicalRenderers),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Endpoint-native renderer mapping signature changed for '" + record.roomId + "'.");
            }
            EndpointNativeRendererRecord[] rendererRecords = record.rendererMappings ??
                Array.Empty<EndpointNativeRendererRecord>();
            if (rendererRecords.Length != source.CanonicalRenderers.Length)
                throw new InvalidOperationException("Endpoint-native renderer binding count changed for '" + record.roomId + "'.");
            for (int i = 0; i < rendererRecords.Length; i++)
            {
                EndpointNativeRendererRecord actual = rendererRecords[i];
                CanonicalRendererWork expected = source.CanonicalRenderers[i];
                if (actual == null ||
                    !string.Equals(actual.canonicalKey, expected.CanonicalKey, StringComparison.Ordinal) ||
                    actual.p0LocalLightmapIndex != expected.P0.lightmapIndex ||
                    actual.p100LocalLightmapIndex != expected.P100.lightmapIndex ||
                    !MatchesVector4(actual.p0ScaleOffset, expected.P0.lightmapScaleOffset) ||
                    !MatchesVector4(actual.p100ScaleOffset, expected.P100.lightmapScaleOffset))
                {
                    throw new InvalidOperationException(
                        "Endpoint-native renderer binding changed at index " + i + " for '" + record.roomId + "'.");
                }
            }

            EndpointNativeEndpointRecord[] endpoints = record.endpoints ??
                Array.Empty<EndpointNativeEndpointRecord>();
            if (endpoints.Length != 2)
                throw new InvalidOperationException("Endpoint-native room must contain exactly P0 and P100 response layouts.");
            VerifyEndpointNativeEndpointRecord(payloadRoot, source, endpoints[0], EndpointLayout.Power0);
            VerifyEndpointNativeEndpointRecord(payloadRoot, source, endpoints[1], EndpointLayout.Power100);

            if (runtimeConsumerReady)
                VerifyEndpointNativeRoomBasisRecord(payloadRoot, payloadRoot, source, record);
            else if (!string.IsNullOrWhiteSpace(record.basisAssetRelativePath) ||
                     !string.IsNullOrWhiteSpace(record.basisDependencyHash))
            {
                throw new InvalidOperationException(
                    "A pre-publication endpoint-native room record unexpectedly names a runtime basis asset.");
            }
        }

        private static void VerifyEndpointNativeEndpointRecord(
            string payloadRoot,
            ResolvedRoom room,
            EndpointNativeEndpointRecord record,
            EndpointLayout layout)
        {
            string endpointId = layout == EndpointLayout.Power100 ? "P100" : "P0";
            if (record == null || !record.exactBaseReferences ||
                !string.Equals(record.endpointId, endpointId, StringComparison.Ordinal) ||
                !string.Equals(
                    record.baseTransitionPolicy,
                    "CapturedEndpointLayoutWithOppositeEndpointTriangleRepacked",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    record.responseMipPolicy,
                    "DestinationNativeMip0ThenFullLinearChainPrecomputedSignedDelta",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Endpoint-native " + endpointId + " record is incomplete or overclaims its response mips.");
            }

            Texture2D[] colors = layout == EndpointLayout.Power100
                ? room.P100.lightmapColors ?? Array.Empty<Texture2D>()
                : room.P0.lightmapColors ?? Array.Empty<Texture2D>();
            Texture2D[] directions = layout == EndpointLayout.Power100
                ? room.P100.lightmapDirections ?? Array.Empty<Texture2D>()
                : room.P0.lightmapDirections ?? Array.Empty<Texture2D>();
            EndpointNativeAtlasRecord[] atlases = record.atlases ??
                Array.Empty<EndpointNativeAtlasRecord>();
            if (atlases.Length != colors.Length || colors.Length != directions.Length)
                throw new InvalidOperationException("Endpoint-native " + endpointId + " atlas count changed.");

            for (int i = 0; i < atlases.Length; i++)
            {
                EndpointNativeAtlasRecord atlas = atlases[i];
                Texture2D baseColor = colors[i];
                Texture2D baseDirection = directions[i];
                string colorPath = AssetDatabase.GetAssetPath(baseColor);
                string directionPath = AssetDatabase.GetAssetPath(baseDirection);
                int expectedOperations = room.CanonicalRenderers.Count(renderer =>
                    (layout == EndpointLayout.Power100
                        ? renderer.P100.lightmapIndex
                        : renderer.P0.lightmapIndex) == i);
                if (atlas == null || atlas.localLightmapIndex != i ||
                    !string.Equals(atlas.baseColorAssetPath, colorPath, StringComparison.Ordinal) ||
                    !string.Equals(atlas.baseColorDependencyHash, GetDependencyHash(colorPath), StringComparison.Ordinal) ||
                    !string.Equals(atlas.baseDirectionAssetPath, directionPath, StringComparison.Ordinal) ||
                    !string.Equals(atlas.baseDirectionDependencyHash, GetDependencyHash(directionPath), StringComparison.Ordinal) ||
                    atlas.width != baseColor.width || atlas.height != baseColor.height ||
                    atlas.baseColorMipmapCount != baseColor.mipmapCount ||
                    atlas.baseDirectionMipmapCount != baseDirection.mipmapCount ||
                    atlas.oppositeBaseMipmapCount != baseColor.mipmapCount ||
                    atlas.oppositeBaseOperationCount != expectedOperations ||
                    atlas.oppositeBaseOperationCount <= 0 ||
                    atlas.oppositeBaseOwnedCoreTexelCount <= 0 ||
                    atlas.oppositeBaseImplicitZeroCoreTexelCount < 0 ||
                    atlas.oppositeBaseImplicitZeroCoreTexelCount >
                        atlas.oppositeBaseOwnedCoreTexelCount ||
                    atlas.oppositeBaseOwnerCollisionCount != 0 ||
                    atlas.oppositeBaseCanonicalOrientationProofSampledTexelCount !=
                        atlas.oppositeBaseOwnedCoreTexelCount -
                        atlas.oppositeBaseImplicitZeroCoreTexelCount ||
                    !IsFinite(atlas.oppositeBaseCanonicalOrientationProofMaxAbsoluteChannelError) ||
                    atlas.oppositeBaseCanonicalOrientationProofMaxAbsoluteChannelError < 0f ||
                    atlas.oppositeBaseCanonicalOrientationProofMaxAbsoluteChannelError >
                        CanonicalOrientationProofMaxAbsoluteError ||
                    atlas.responseMipmapCount != baseColor.mipmapCount ||
                    !string.Equals(atlas.responseEncoding, "PrecomputedDeltaRGBAHalf", StringComparison.Ordinal) ||
                    atlas.operationCount != expectedOperations || atlas.operationCount <= 0 ||
                    atlas.baselineOwnedCoreTexelCount <= 0 || atlas.fullOwnedCoreTexelCount <= 0 ||
                    atlas.baselineOwnerCollisionCount != 0 || atlas.fullOwnerCollisionCount != 0 ||
                    atlas.baselineOwnedCoreTexelCount != atlas.fullOwnedCoreTexelCount ||
                    atlas.baselineImplicitZeroCoreTexelCount != atlas.fullImplicitZeroCoreTexelCount ||
                    !IsValidDeltaQuantizationRecord(
                        atlas.colorDeltaOwnedMip0Quantization,
                        atlas.baselineOwnedCoreTexelCount,
                        3) ||
                    !IsValidDeltaQuantizationRecord(
                        atlas.directionalMomentDeltaOwnedMip0Quantization,
                        atlas.baselineOwnedCoreTexelCount,
                        4))
                {
                    throw new InvalidOperationException(
                        "Endpoint-native " + endpointId + " atlas contract failed at bucket " + i + ".");
                }

                string prefix = room.Spec.RoomId + "/Endpoints/" + endpointId +
                                "/ReceiverBasis/PrecomputedDelta/";
                string transitionPrefix = room.Spec.RoomId + "/Endpoints/" + endpointId +
                                          "/BaseTransition/OppositeEndpoint/";
                string stem = "LM" + i.ToString("00", CultureInfo.InvariantCulture);
                VerifyEndpointNativeBaseTransitionTexture(
                    payloadRoot,
                    atlas.oppositeBaseColorRelativePath,
                    transitionPrefix + stem + "_Color.asset",
                    baseColor,
                    TextureFormat.BC6H,
                    endpointId + " opposite-endpoint base color");
                VerifyEndpointNativeBaseTransitionTexture(
                    payloadRoot,
                    atlas.oppositeBaseDirectionRelativePath,
                    transitionPrefix + stem + "_Direction.asset",
                    baseDirection,
                    TextureFormat.RGBA32,
                    endpointId + " opposite-endpoint base direction");
                VerifyEndpointNativeResponseTexture(
                    payloadRoot,
                    atlas.colorDeltaRelativePath,
                    prefix + stem + "_ColorDelta.asset",
                    baseColor,
                    endpointId + " signed color delta");
                VerifyEndpointNativeResponseTexture(
                    payloadRoot,
                    atlas.directionalMomentDeltaRelativePath,
                    prefix + stem + "_DirectionalMomentDelta.asset",
                    baseDirection,
                    endpointId + " signed directional-moment delta");
            }
        }

        private static void VerifyEndpointNativeBaseTransitionTexture(
            string payloadRoot,
            string relativePath,
            string expectedRelativePath,
            Texture2D endpointReference,
            TextureFormat expectedFormat,
            string label)
        {
            if (!string.Equals(relativePath, expectedRelativePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Endpoint-native base-transition path is not deterministic for " + label + ".");
            }
            Texture2D texture = LoadRequired<Texture2D>(CombinePayloadPath(payloadRoot, relativePath));
            ValidateSampleTexture(texture, label,
                expectedFormat == TextureFormat.BC7 || expectedFormat == TextureFormat.RGBA32);
            if (texture.format != expectedFormat ||
                texture.width != endpointReference.width || texture.height != endpointReference.height ||
                texture.mipmapCount != endpointReference.mipmapCount ||
                texture.filterMode != endpointReference.filterMode ||
                texture.wrapModeU != endpointReference.wrapModeU ||
                texture.wrapModeV != endpointReference.wrapModeV ||
                texture.wrapModeW != endpointReference.wrapModeW ||
                texture.anisoLevel != endpointReference.anisoLevel ||
                !Mathf.Approximately(texture.mipMapBias, endpointReference.mipMapBias))
            {
                throw new InvalidOperationException(
                    "Endpoint-native base-transition texture does not match its captured " +
                    "endpoint layout/sampling contract for " + label + ".");
            }
        }

        private static void VerifyEndpointNativeResponseTexture(
            string payloadRoot,
            string relativePath,
            string expectedRelativePath,
            Texture2D endpointReference,
            string label)
        {
            if (!string.Equals(relativePath, expectedRelativePath, StringComparison.Ordinal))
                throw new InvalidOperationException("Endpoint-native response path is not deterministic for " + label + ".");
            Texture2D texture = LoadRequired<Texture2D>(CombinePayloadPath(payloadRoot, relativePath));
            ValidateSampleTexture(texture, label, true);
            if (texture.format != TextureFormat.RGBAHalf ||
                texture.width != endpointReference.width || texture.height != endpointReference.height ||
                texture.mipmapCount != endpointReference.mipmapCount ||
                texture.filterMode != endpointReference.filterMode ||
                texture.wrapModeU != endpointReference.wrapModeU ||
                texture.wrapModeV != endpointReference.wrapModeV)
            {
                throw new InvalidOperationException(
                    "Endpoint-native response texture does not match its endpoint sampling contract for " + label + ".");
            }
        }

        private static bool IsValidDeltaQuantizationRecord(
            DeltaQuantizationStats record,
            int expectedOwnedTexels,
            int channels)
        {
            return record != null && expectedOwnedTexels > 0 &&
                   record.ownedMip0TexelCount == expectedOwnedTexels &&
                   record.channelSampleCount == expectedOwnedTexels * channels &&
                   IsFinite(record.absoluteRmse) && record.absoluteRmse >= 0f &&
                   IsFinite(record.absoluteP95) && record.absoluteP95 >= 0f &&
                   IsFinite(record.absoluteP99) && record.absoluteP99 >= record.absoluteP95 &&
                   IsFinite(record.absoluteMax) && record.absoluteMax >= record.absoluteP99;
        }

        private static bool MatchesVector4(float[] serialized, Vector4 expected)
        {
            return serialized != null && serialized.Length == 4 &&
                   Mathf.Abs(serialized[0] - expected.x) <= 1e-6f &&
                   Mathf.Abs(serialized[1] - expected.y) <= 1e-6f &&
                   Mathf.Abs(serialized[2] - expected.z) <= 1e-6f &&
                   Mathf.Abs(serialized[3] - expected.w) <= 1e-6f;
        }

        private static bool TryVerifyPayload(string payloadRoot, out string failure)
        {
            try
            {
                if (!AssetDatabase.IsValidFolder(payloadRoot))
                    throw new InvalidOperationException("Generated payload folder is missing: '" + payloadRoot + "'.");
                string manifestPath = payloadRoot + "/" + ManifestFileName;
                string physicalManifest = AssetPathToPhysicalPath(manifestPath);
                if (!File.Exists(physicalManifest))
                    throw new InvalidOperationException("Generated payload manifest is missing: '" + manifestPath + "'.");
                DungeonPortalBakedBasisGeneratedManifest manifest = JsonUtility.FromJson<DungeonPortalBakedBasisGeneratedManifest>(
                    File.ReadAllText(physicalManifest));
                if (manifest == null || !string.Equals(manifest.schema, DungeonPortalBakedBasisGeneratedManifest.CurrentSchema, StringComparison.Ordinal) ||
                    manifest.strictPersistedCaptureIntegrityClaimed)
                {
                    throw new InvalidOperationException("Generated payload manifest is incomplete or makes an invalid strict-integrity claim.");
                }
                if (string.IsNullOrWhiteSpace(manifest.chartCopyShaderPath) ||
                    !string.Equals(manifest.chartCopyShaderDependencyHash, GetDependencyHash(manifest.chartCopyShaderPath), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The chart-copy shader dependency hash no longer matches the generated payload.");
                }

                InputFingerprint[] inputs = manifest.inputs ?? Array.Empty<InputFingerprint>();
                for (int i = 0; i < inputs.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(inputs[i].assetPath) ||
                        string.IsNullOrWhiteSpace(inputs[i].dependencyHash) ||
                        AssetDatabase.LoadMainAssetAtPath(inputs[i].assetPath) == null ||
                        !string.Equals(inputs[i].dependencyHash, GetDependencyHash(inputs[i].assetPath), StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Generated input fingerprint changed at index " + i + ".");
                    }
                }

                CaptureIntegrityRecord[] captures = manifest.captureIntegrity ?? Array.Empty<CaptureIntegrityRecord>();
                for (int i = 0; i < captures.Length; i++)
                    VerifyCaptureIntegrityRecord(captures[i]);

                ArtifactFingerprint[] artifacts = manifest.artifacts ?? Array.Empty<ArtifactFingerprint>();
                for (int i = 0; i < artifacts.Length; i++)
                    VerifyArtifactFingerprint(payloadRoot, artifacts[i]);

                RoomRecord[] roomRecords = manifest.rooms ?? Array.Empty<RoomRecord>();
                if (roomRecords.Length != LegacyRoomSpecs.Length)
                    throw new InvalidOperationException("Generated manifest does not include exactly the two canonical room payloads.");
                for (int i = 0; i < roomRecords.Length; i++)
                    VerifyRoomRecord(payloadRoot, roomRecords[i]);

                failure = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        private static void VerifyCaptureIntegrityRecord(CaptureIntegrityRecord record)
        {
            if (record == null || !record.structuralValidationPassed ||
                record.strictValidationStatus == null || record.strictValidationStatus.IndexOf("NOT_CLAIMED", StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("Generated capture-integrity record is incomplete or overclaims strict validation.");
            }
            DungeonPortalReceiverResponseCapture capture =
                LoadRequired<DungeonPortalReceiverResponseCapture>(record.captureAssetPath);
            if (!capture.TryValidate(out string structuralFailure))
                throw new InvalidOperationException("Raw capture structural validation no longer passes: " + structuralFailure);
            DungeonPortalReceiverResponseCapture.CaptureState baseline = capture.Baseline;
            DungeonPortalReceiverResponseCapture.CaptureState full = capture.Full;
            if (!string.Equals(record.baselineStateHash, baseline.stateHash, StringComparison.Ordinal) ||
                !string.Equals(record.fullStateHash, full.stateHash, StringComparison.Ordinal) ||
                !string.Equals(ComputeCaptureStateHash(baseline), baseline.stateHash, StringComparison.Ordinal) ||
                !string.Equals(ComputeCaptureStateHash(full), full.stateHash, StringComparison.Ordinal) ||
                !VerifyPublicStateTextureDependencies(baseline) || !VerifyPublicStateTextureDependencies(full))
            {
                throw new InvalidOperationException("Raw capture state hash or public texture dependency no longer matches its evidence.");
            }

            EvaluateCaptureInputWaiver(
                capture.Provenance,
                record.receiverRoomId,
                out bool allNonWaivedMatch,
                out bool waiverApplied,
                out string waiverPath,
                out string savedHash,
                out string currentHash);
            if (record.allNonWaivedProductionHashesMatch != allNonWaivedMatch ||
                record.rotationBakeToolHashOnlyWaiverApplied != waiverApplied ||
                !string.Equals(record.waiverAssetPath ?? string.Empty, waiverPath ?? string.Empty, StringComparison.Ordinal) ||
                !string.Equals(record.savedDependencyHash ?? string.Empty, savedHash ?? string.Empty, StringComparison.Ordinal) ||
                !string.Equals(record.currentDependencyHash ?? string.Empty, currentHash ?? string.Empty, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Raw capture provenance waiver no longer matches the generated record.");
            }
        }

        private static bool VerifyPublicStateTextureDependencies(DungeonPortalReceiverResponseCapture.CaptureState state)
        {
            DungeonPortalReceiverResponseCapture.CaptureLightmap[] maps = state.lightmaps ??
                Array.Empty<DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            for (int i = 0; i < maps.Length; i++)
            {
                if (!DependencyMatches(maps[i].colorAssetPath, maps[i].colorDependencyHash) ||
                    !DependencyMatches(maps[i].directionAssetPath, maps[i].directionDependencyHash) ||
                    (maps[i].hasShadowMask && !DependencyMatches(maps[i].shadowMaskAssetPath, maps[i].shadowMaskDependencyHash)))
                {
                    return false;
                }
            }
            DungeonPortalReceiverResponseCapture.FixedCameraCapture[] cameras = state.fixedCameraCaptures ??
                Array.Empty<DungeonPortalReceiverResponseCapture.FixedCameraCapture>();
            for (int i = 0; i < cameras.Length; i++)
            {
                if (!DependencyMatches(cameras[i].hdrColorAssetPath, cameras[i].hdrColorDependencyHash))
                    return false;
            }
            return true;
        }

        private static bool DependencyMatches(string assetPath, string expectedHash)
        {
            return !string.IsNullOrWhiteSpace(assetPath) && !string.IsNullOrWhiteSpace(expectedHash) &&
                   AssetDatabase.LoadMainAssetAtPath(assetPath) != null &&
                   string.Equals(GetDependencyHash(assetPath), expectedHash, StringComparison.Ordinal);
        }

        private static void VerifyArtifactFingerprint(string payloadRoot, ArtifactFingerprint artifact)
        {
            if (artifact == null || string.IsNullOrWhiteSpace(artifact.relativePath))
                throw new InvalidOperationException("Generated artifact record is incomplete.");
            string path = CombinePayloadPath(payloadRoot, artifact.relativePath);
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null || !string.Equals(artifact.dependencyHash, GetDependencyHash(path), StringComparison.Ordinal))
                throw new InvalidOperationException("Generated artifact is missing or has a changed dependency hash: '" + path + "'.");
            if (artifact.width > 0 || artifact.height > 0)
            {
                Texture2D texture = asset as Texture2D;
                if (texture == null || texture.width != artifact.width || texture.height != artifact.height ||
                    texture.mipmapCount != artifact.mipmapCount || artifact.mipmapCount <= 1 ||
                    !string.Equals(texture.format.ToString(), artifact.textureFormat, StringComparison.Ordinal) ||
                    GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat) || !artifact.linear)
                {
                    throw new InvalidOperationException(
                        "Generated texture artifact no longer satisfies its linear format/dimension/full-mip contract: '" +
                        path + "'.");
                }
            }
        }

        private static void VerifyRoomRecord(string payloadRoot, RoomRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.roomId) ||
                string.IsNullOrWhiteSpace(record.basisAssetRelativePath) ||
                string.IsNullOrWhiteSpace(record.chartOwnershipPolicy) ||
                record.chartOwnershipPolicy.IndexOf("UV2_TRIANGLE_RASTER", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Generated room record is incomplete.");
            RoomSpec spec = LegacyRoomSpecs.FirstOrDefault(value => string.Equals(value.RoomId, record.roomId, StringComparison.Ordinal));
            if (spec == null)
                throw new InvalidOperationException("Generated payload includes an unexpected room id: '" + record.roomId + "'.");
            string expectedBasisRelativePath = record.roomId + "/" + record.roomId + "_RoomBasis.asset";
            if (!string.Equals(record.basisAssetRelativePath, expectedBasisRelativePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Generated room-basis path does not satisfy the canonical direct Generated/<RoomId>/ contract for '" +
                    record.roomId + "'.");
            }
            DungeonPortalBakedRoomBasisData basis = LoadRequired<DungeonPortalBakedRoomBasisData>(
                CombinePayloadPath(payloadRoot, record.basisAssetRelativePath));
            if (!basis.TryValidateDefinition(out string basisFailure) ||
                !string.Equals(basis.RoomId, record.roomId, StringComparison.Ordinal) ||
                basis.CanonicalRenderers.Length != record.canonicalRendererCount)
            {
                throw new InvalidOperationException("Generated room-basis definition failed verification for '" + record.roomId + "': " + basisFailure);
            }
            ResolvedRoom source = ResolveRoom(spec);
            if (!string.Equals(record.canonicalRendererMappingSignature,
                    ComputeCanonicalMappingSignature(source.CanonicalRenderers), StringComparison.Ordinal) ||
                source.CanonicalRenderers.Count(item => item.BaselineCapture.HasValue) != record.captureRendererCount ||
                source.CanonicalRenderers.Count(item => !item.BaselineCapture.HasValue) != record.unmatchedProductionRendererCount)
            {
                throw new InvalidOperationException("Generated room mapping record no longer matches the read-only source inputs for '" + record.roomId + "'.");
            }
            for (int i = 0; i < source.CanonicalRenderers.Length; i++)
            {
                CanonicalRendererWork renderer = source.CanonicalRenderers[i];
                if (renderer.LightmapUvMode == LightmapUvMode.ImplicitZero)
                    ValidateImplicitZeroLightmapOperation(renderer.ProductionMesh, renderer.CanonicalKey);
                else
                    ValidateTriangleRasterMesh(renderer.ProductionMesh, renderer.CanonicalKey);
            }
            VerifyRendererMappingRecords(record, source);
            VerifyAtlasRecords(payloadRoot, record, source, basis);
        }

        private static void VerifyRendererMappingRecords(RoomRecord record, ResolvedRoom source)
        {
            RendererMappingRecord[] mappings = record.rendererMappings ?? Array.Empty<RendererMappingRecord>();
            if (mappings.Length != source.CanonicalRenderers.Length)
                throw new InvalidOperationException("Generated renderer-mapping record count changed for '" + record.roomId + "'.");
            for (int i = 0; i < mappings.Length; i++)
            {
                RendererMappingRecord mapping = mappings[i];
                CanonicalRendererWork expected = source.CanonicalRenderers[i];
                if (mapping == null ||
                    !string.Equals(mapping.canonicalKey, expected.CanonicalKey, StringComparison.Ordinal) ||
                    !string.Equals(mapping.productionMeshAssetGuid, expected.ProductionMeshAssetGuid, StringComparison.Ordinal) ||
                    mapping.productionMeshLocalId != expected.ProductionMeshLocalId ||
                    !string.Equals(mapping.productionMeshUv2Hash, expected.ProductionMeshUv2Hash, StringComparison.Ordinal) ||
                    !string.Equals(mapping.lightmapUvMode, expected.LightmapUvMode.ToString(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Generated renderer-mapping UV2 identity/mode changed at index " + i + " for '" + record.roomId + "'.");
                }
            }
        }

        private static void VerifyAtlasRecords(
            string payloadRoot,
            RoomRecord record,
            ResolvedRoom source,
            DungeonPortalBakedRoomBasisData basis)
        {
            AtlasRecord[] records = record.atlases ?? Array.Empty<AtlasRecord>();
            BucketWork[] buckets = BuildBucketWork(source);
            if (records.Length != buckets.Length || basis.CanonicalAtlases.Length != buckets.Length)
            {
                throw new InvalidOperationException(
                    "Generated atlas-record cardinality does not match the canonical P0 bucket layout for '" +
                    record.roomId + "'.");
            }
            var visited = new bool[buckets.Length];
            for (int i = 0; i < records.Length; i++)
            {
                AtlasRecord atlas = records[i];
                if (atlas == null || atlas.canonicalBucketIndex < 0 || atlas.canonicalBucketIndex >= buckets.Length ||
                    visited[atlas.canonicalBucketIndex])
                {
                    throw new InvalidOperationException("Generated atlas record has an invalid or duplicate bucket index.");
                }
                visited[atlas.canonicalBucketIndex] = true;
                BucketWork bucket = buckets[atlas.canonicalBucketIndex];
                DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket basisBucket =
                    basis.CanonicalAtlases[atlas.canonicalBucketIndex];
                if (atlas.canonicalLocalLightmapIndex != bucket.LocalLightmapIndex ||
                    atlas.width != bucket.Width || atlas.height != bucket.Height ||
                    basisBucket == null || basisBucket.MipCount != bucket.P0Color.mipmapCount ||
                    !string.Equals(atlas.p0ColorSourceAssetPath, AssetDatabase.GetAssetPath(bucket.P0Color), StringComparison.Ordinal) ||
                    !string.Equals(atlas.p0DirectionSourceAssetPath, AssetDatabase.GetAssetPath(bucket.P0Direction), StringComparison.Ordinal) ||
                    atlas.p0ColorMipmapCount != bucket.P0Color.mipmapCount ||
                    atlas.p0DirectionMipmapCount != bucket.P0Direction.mipmapCount)
                {
                    throw new InvalidOperationException(
                        "Generated atlas record no longer matches the canonical P0 layout or mip chain for '" +
                        record.roomId + "' bucket " + atlas.canonicalBucketIndex + ".");
                }

                VerifyCanonicalGeneratedTexture(
                    payloadRoot,
                    atlas.p100ColorCanonicalRelativePath,
                    atlas.width,
                    atlas.height,
                    atlas.p100ColorCanonicalMipmapCount,
                    TextureFormat.BC6H,
                    "P100 color");
                VerifyCanonicalGeneratedTexture(
                    payloadRoot,
                    atlas.p100DirectionCanonicalRelativePath,
                    atlas.width,
                    atlas.height,
                    atlas.p100DirectionCanonicalMipmapCount,
                    TextureFormat.BC7,
                    "P100 direction");
                VerifyCanonicalGeneratedTexture(
                    payloadRoot,
                    atlas.responseBaselineColorRelativePath,
                    atlas.width,
                    atlas.height,
                    atlas.responseBaselineColorMipmapCount,
                    TextureFormat.BC6H,
                    "receiver baseline color");
                VerifyCanonicalGeneratedTexture(
                    payloadRoot,
                    atlas.responseBaselineDirectionRelativePath,
                    atlas.width,
                    atlas.height,
                    atlas.responseBaselineDirectionMipmapCount,
                    TextureFormat.BC7,
                    "receiver baseline direction");
                VerifyCanonicalGeneratedTexture(
                    payloadRoot,
                    atlas.responseFullColorRelativePath,
                    atlas.width,
                    atlas.height,
                    atlas.responseFullColorMipmapCount,
                    TextureFormat.BC6H,
                    "receiver full color");
                VerifyCanonicalGeneratedTexture(
                    payloadRoot,
                    atlas.responseFullDirectionRelativePath,
                    atlas.width,
                    atlas.height,
                    atlas.responseFullDirectionMipmapCount,
                    TextureFormat.BC7,
                    "receiver full direction");

                if (atlas.p100ColorCanonicalMipmapCount != bucket.P0Color.mipmapCount ||
                    atlas.p100DirectionCanonicalMipmapCount != bucket.P0Direction.mipmapCount ||
                    atlas.responseBaselineColorMipmapCount != bucket.P0Color.mipmapCount ||
                    atlas.responseBaselineDirectionMipmapCount != bucket.P0Direction.mipmapCount ||
                    atlas.responseFullColorMipmapCount != bucket.P0Color.mipmapCount ||
                    atlas.responseFullDirectionMipmapCount != bucket.P0Direction.mipmapCount)
                {
                    throw new InvalidOperationException(
                        "Generated atlas does not preserve the full canonical P0 mip count for '" + record.roomId +
                        "' bucket " + atlas.canonicalBucketIndex + ".");
                }

                int expectedOperationCount = source.CanonicalRenderers.Count(
                    item => item.P0.lightmapIndex == atlas.canonicalBucketIndex);
                int expectedImplicitZeroOperationCount = source.CanonicalRenderers.Count(
                    item => item.P0.lightmapIndex == atlas.canonicalBucketIndex &&
                            item.LightmapUvMode == LightmapUvMode.ImplicitZero);
                VerifyRasterizationEvidence(
                    atlas,
                    expectedOperationCount,
                    expectedImplicitZeroOperationCount,
                    record.roomId);
            }
        }

        private static void VerifyRasterizationEvidence(
            AtlasRecord atlas,
            int expectedOperationCount,
            int expectedImplicitZeroOperationCount,
            string roomId)
        {
            if (atlas.p100OperationCount != expectedOperationCount ||
                atlas.responseBaselineOperationCount != expectedOperationCount ||
                atlas.responseFullOperationCount != expectedOperationCount ||
                atlas.p100OwnedCoreTexelCount < 0 ||
                atlas.responseBaselineOwnedCoreTexelCount < 0 ||
                atlas.responseFullOwnedCoreTexelCount < 0 ||
                atlas.p100ImplicitZeroCoreTexelCount < 0 ||
                atlas.responseBaselineImplicitZeroCoreTexelCount < 0 ||
                atlas.responseFullImplicitZeroCoreTexelCount < 0 ||
                atlas.p100OwnerCollisionCount != 0 ||
                atlas.responseBaselineOwnerCollisionCount != 0 ||
                atlas.responseFullOwnerCollisionCount != 0 ||
                atlas.p100OwnedCoreTexelCount != atlas.responseBaselineOwnedCoreTexelCount ||
                atlas.p100OwnedCoreTexelCount != atlas.responseFullOwnedCoreTexelCount ||
                atlas.p100ImplicitZeroCoreTexelCount != expectedImplicitZeroOperationCount ||
                atlas.responseBaselineImplicitZeroCoreTexelCount != expectedImplicitZeroOperationCount ||
                atlas.responseFullImplicitZeroCoreTexelCount != expectedImplicitZeroOperationCount ||
                atlas.canonicalOrientationProofSampledTexelCount !=
                    atlas.p100OwnedCoreTexelCount - atlas.p100ImplicitZeroCoreTexelCount ||
                !IsFinite(atlas.canonicalOrientationProofMaxAbsoluteChannelError) ||
                atlas.canonicalOrientationProofMaxAbsoluteChannelError < 0f ||
                atlas.canonicalOrientationProofMaxAbsoluteChannelError > CanonicalOrientationProofMaxAbsoluteError)
            {
                throw new InvalidOperationException(
                    "Generated UV2 triangle ownership/orientation evidence is invalid for '" + roomId +
                    "' bucket " + atlas.canonicalBucketIndex + ".");
            }
            if (expectedOperationCount > 0 && atlas.p100OwnedCoreTexelCount == 0)
            {
                throw new InvalidOperationException(
                    "Generated UV2 triangle ownership evidence has no covered texels for non-empty '" + roomId +
                    "' bucket " + atlas.canonicalBucketIndex + ".");
            }
        }

        private static void VerifyCanonicalGeneratedTexture(
            string payloadRoot,
            string relativePath,
            int width,
            int height,
            int expectedMipmapCount,
            TextureFormat expectedFormat,
            string label)
        {
            Texture2D texture = LoadRequired<Texture2D>(CombinePayloadPath(payloadRoot, relativePath));
            if (texture.width != width || texture.height != height || texture.mipmapCount != expectedMipmapCount ||
                texture.mipmapCount <= 1 || texture.format != expectedFormat ||
                GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat))
            {
                throw new InvalidOperationException(
                    "Generated " + label + " atlas violates its linear dimensions/format/full-mip contract: '" +
                    relativePath + "'.");
            }
        }

        [Serializable]
        private sealed class EndpointNativeManifest
        {
            public string schema = EndpointNativeManifestSchema;
            public string toolVersion = string.Empty;
            public string generatedUtcIso8601 = string.Empty;
            public string publishedUtcIso8601 = string.Empty;
            public string payloadRootName = string.Empty;
            public string chartCopyShaderPath = string.Empty;
            public string chartCopyShaderDependencyHash = string.Empty;
            public bool productionInputsReadOnly;
            public bool strictPersistedCaptureIntegrityClaimed;
            public string basePolicy = string.Empty;
            public string responseMipPolicy = string.Empty;
            public string runtimeSchema = string.Empty;
            public string receiverPosePolicy = string.Empty;
            public string compactResponseEncoding = string.Empty;
            public bool runtimeConsumerReady;
            public InputFingerprint[] inputs = Array.Empty<InputFingerprint>();
            public CaptureIntegrityRecord[] captureIntegrity = Array.Empty<CaptureIntegrityRecord>();
            public AngleCaptureIntegrityRecord[] angleCaptureIntegrity =
                Array.Empty<AngleCaptureIntegrityRecord>();
            public ArtifactFingerprint[] artifacts = Array.Empty<ArtifactFingerprint>();
            public EndpointNativeRoomRecord[] rooms = Array.Empty<EndpointNativeRoomRecord>();
        }

        [Serializable]
        private sealed class AngleCaptureIntegrityRecord
        {
            public string receiverRoomId = string.Empty;
            public string angleCaptureAssetPath = string.Empty;
            public string angleCaptureDependencyHash = string.Empty;
            public bool structuralValidationPassed;
            public string structuralValidationError = string.Empty;
            public int captureSchemaVersion;
            public string canonicalWorkspaceDependencyHash = string.Empty;
            public AngleCapturePoseIntegrityRecord[] poses = Array.Empty<AngleCapturePoseIntegrityRecord>();
        }

        [Serializable]
        private sealed class AngleCapturePoseIntegrityRecord
        {
            public string poseId = string.Empty;
            public float openFraction;
            public float angleDegrees;
            public string baselineStateId = string.Empty;
            public string baselineStateHash = string.Empty;
            public string fullStateId = string.Empty;
            public string fullStateHash = string.Empty;
            public string probeStencilPolicy = string.Empty;
            public string probeLocalPositionSignature = string.Empty;
            public string firstNineBaselineShSignature = string.Empty;
            public string firstNineFullShSignature = string.Empty;
        }

        [Serializable]
        private sealed class EndpointNativeRoomRecord
        {
            public string roomId = string.Empty;
            public string productionPrefabPath = string.Empty;
            public string p0BakeDataPath = string.Empty;
            public string p100BakeDataPath = string.Empty;
            public string receiverCapturePath = string.Empty;
            public string angleCaptureAssetPath = string.Empty;
            public string angleCaptureDependencyHash = string.Empty;
            public string canonicalRendererMappingSignature = string.Empty;
            public string basisAssetRelativePath = string.Empty;
            public string basisDependencyHash = string.Empty;
            public EndpointNativeEndpointRecord[] endpoints = Array.Empty<EndpointNativeEndpointRecord>();
            public EndpointNativeRendererRecord[] rendererMappings = Array.Empty<EndpointNativeRendererRecord>();
        }

        [Serializable]
        private sealed class EndpointNativeEndpointRecord
        {
            public string endpointId = string.Empty;
            public bool exactBaseReferences;
            public string baseTransitionPolicy = string.Empty;
            public string responseMipPolicy = string.Empty;
            public EndpointNativePoseRecord[] poses = Array.Empty<EndpointNativePoseRecord>();
            // Retained only to deserialize/reject pre-angle v1 manifests. V2 authoring
            // always leaves this empty and stores all response atlases under poses.
            public EndpointNativeAtlasRecord[] atlases = Array.Empty<EndpointNativeAtlasRecord>();
        }

        [Serializable]
        private sealed class EndpointNativePoseRecord
        {
            public string poseId = string.Empty;
            public float openFraction;
            public float angleDegrees;
            public string baselineStateId = string.Empty;
            public string baselineStateHash = string.Empty;
            public string fullStateId = string.Empty;
            public string fullStateHash = string.Empty;
            public string probeStencilPolicy = string.Empty;
            public string probeLocalPositionSignature = string.Empty;
            public string firstNineBaselineShSignature = string.Empty;
            public string firstNineFullShSignature = string.Empty;
            public EndpointNativeAtlasRecord[] atlases = Array.Empty<EndpointNativeAtlasRecord>();
        }

        [Serializable]
        private sealed class EndpointNativeAtlasRecord
        {
            public int localLightmapIndex;
            public string baseColorAssetPath = string.Empty;
            public string baseColorDependencyHash = string.Empty;
            public string baseDirectionAssetPath = string.Empty;
            public string baseDirectionDependencyHash = string.Empty;
            public int width;
            public int height;
            public int baseColorMipmapCount;
            public int baseDirectionMipmapCount;
            public string oppositeBaseColorRelativePath = string.Empty;
            public string oppositeBaseDirectionRelativePath = string.Empty;
            public string oppositeBaseColorStorageFormat = string.Empty;
            public int oppositeBaseMipmapCount;
            public int oppositeBaseOperationCount;
            public int oppositeBaseOwnedCoreTexelCount;
            public int oppositeBaseImplicitZeroCoreTexelCount;
            public int oppositeBaseOwnerCollisionCount;
            public int oppositeBaseCanonicalOrientationProofSampledTexelCount;
            public float oppositeBaseCanonicalOrientationProofMaxAbsoluteChannelError;
            public string responseEncoding = string.Empty;
            public string colorStorageFormat = string.Empty;
            public string directionalMomentStorageFormat = string.Empty;
            public string colorDeltaRelativePath = string.Empty;
            public string directionalMomentDeltaRelativePath = string.Empty;
            public string colorPositiveRelativePath = string.Empty;
            public string colorNegativeRelativePath = string.Empty;
            public string directionalMomentPositiveRelativePath = string.Empty;
            public string directionalMomentNegativeRelativePath = string.Empty;
            public Vector4[] colorPositiveScales = Array.Empty<Vector4>();
            public Vector4[] colorNegativeScales = Array.Empty<Vector4>();
            public Vector4[] directionalMomentPositiveScales = Array.Empty<Vector4>();
            public Vector4[] directionalMomentNegativeScales = Array.Empty<Vector4>();
            public CompactDeltaReconstructionStats[] colorReconstructionByMip =
                Array.Empty<CompactDeltaReconstructionStats>();
            public CompactDeltaReconstructionStats[] directionalMomentReconstructionByMip =
                Array.Empty<CompactDeltaReconstructionStats>();
            public int responseMipmapCount;
            public int operationCount;
            public int baselineOwnedCoreTexelCount;
            public int baselineImplicitZeroCoreTexelCount;
            public int baselineOwnerCollisionCount;
            public int fullOwnedCoreTexelCount;
            public int fullImplicitZeroCoreTexelCount;
            public int fullOwnerCollisionCount;
            public DeltaQuantizationStats colorDeltaOwnedMip0Quantization;
            public DeltaQuantizationStats directionalMomentDeltaOwnedMip0Quantization;
        }

        [Serializable]
        private sealed class EndpointNativeRendererRecord
        {
            public string canonicalKey = string.Empty;
            public int p0LocalLightmapIndex;
            public float[] p0ScaleOffset = Array.Empty<float>();
            public int p100LocalLightmapIndex;
            public float[] p100ScaleOffset = Array.Empty<float>();
        }

        private sealed class EndpointNativeBasisPublicationTransaction
        {
            private readonly PublicationMoveRecord[] promoted;
            private readonly PublicationMoveRecord[] backups;
            private bool settled;

            private EndpointNativeBasisPublicationTransaction(
                PublicationMoveRecord[] promotedRecords,
                PublicationMoveRecord[] backupRecords)
            {
                promoted = promotedRecords;
                backups = backupRecords;
            }

            public static EndpointNativeBasisPublicationTransaction Promote(
                string stagingRoot,
                string manifestFileName)
            {
                ValidateOwnedGeneratedRoot(EndpointNativeGeneratedRoot);
                if (!AssetDatabase.IsValidFolder(stagingRoot) ||
                    !stagingRoot.StartsWith(
                        EndpointNativeGeneratedRoot + "/__Staging_",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Refusing to publish endpoint-native bases from a non-owned staging root: '" +
                        stagingRoot + "'.");
                }

                string token = DateTime.UtcNow.ToString(
                                   "yyyyMMddTHHmmssfffZ",
                                   CultureInfo.InvariantCulture) + "_" +
                               Guid.NewGuid().ToString("N").Substring(0, 8);
                string backupRoot = EndpointNativeGeneratedRoot +
                                    "/__BasisPublicationBackup_" + token;
                EnsureFolder(backupRoot);
                var backupRecords = new List<PublicationMoveRecord>();
                var promotedRecords = new List<PublicationMoveRecord>();
                try
                {
                    for (int i = 0; i < EndpointNativeRoomSpecs.Length; i++)
                    {
                        string roomId = EndpointNativeRoomSpecs[i].RoomId;
                        string finalPath = EndpointNativeGeneratedRoot + "/" + roomId + "/" +
                                           roomId + "_RoomBasis.asset";
                        if (!PublicationAssetExists(finalPath))
                            continue;
                        string backupPath = backupRoot + "/" + roomId + "_RoomBasis.asset";
                        MovePublicationAssetOrThrow(
                            finalPath,
                            backupPath,
                            "backup the existing endpoint-native room basis");
                        backupRecords.Add(new PublicationMoveRecord(finalPath, backupPath));
                    }

                    string finalManifest = EndpointNativeGeneratedRoot + "/" + manifestFileName;
                    if (!PublicationAssetExists(finalManifest))
                        throw new InvalidOperationException("Endpoint-native publication has no prior manifest to back up.");
                    string backupManifest = backupRoot + "/" + manifestFileName;
                    MovePublicationAssetOrThrow(
                        finalManifest,
                        backupManifest,
                        "backup the pre-publication endpoint-native manifest");
                    backupRecords.Add(new PublicationMoveRecord(finalManifest, backupManifest));

                    for (int i = 0; i < EndpointNativeRoomSpecs.Length; i++)
                    {
                        string roomId = EndpointNativeRoomSpecs[i].RoomId;
                        string sourcePath = stagingRoot + "/" + roomId + "/" +
                                            roomId + "_RoomBasis.asset";
                        string finalPath = EndpointNativeGeneratedRoot + "/" + roomId + "/" +
                                           roomId + "_RoomBasis.asset";
                        if (!PublicationAssetExists(sourcePath))
                            throw new InvalidOperationException("Publication staging is missing '" + sourcePath + "'.");
                        MovePublicationAssetOrThrow(
                            sourcePath,
                            finalPath,
                            "publish an endpoint-native room basis");
                        promotedRecords.Add(new PublicationMoveRecord(finalPath, sourcePath));
                    }

                    string stagedManifest = stagingRoot + "/" + manifestFileName;
                    if (!PublicationAssetExists(stagedManifest))
                        throw new InvalidOperationException("Publication staging is missing its manifest.");
                    MovePublicationAssetOrThrow(
                        stagedManifest,
                        finalManifest,
                        "publish the DPBB-2 endpoint-native manifest");
                    promotedRecords.Add(new PublicationMoveRecord(finalManifest, stagedManifest));

                    string archivedStaging = EndpointNativeGeneratedRoot +
                                             "/__PromotedBasisPublication_" + token;
                    MovePublicationAssetOrThrow(
                        stagingRoot,
                        archivedStaging,
                        "archive the empty endpoint-native basis-publication staging root");
                }
                catch
                {
                    RestoreFailedPublication(promotedRecords, backupRecords, token);
                    throw;
                }

                return new EndpointNativeBasisPublicationTransaction(
                    promotedRecords.ToArray(),
                    backupRecords.ToArray());
            }

            public void Commit()
            {
                settled = true;
            }

            public void RollBack()
            {
                if (settled)
                    return;
                RestoreFailedPublication(
                    new List<PublicationMoveRecord>(promoted),
                    new List<PublicationMoveRecord>(backups),
                    DateTime.UtcNow.ToString(
                        "yyyyMMddTHHmmssfffZ",
                        CultureInfo.InvariantCulture) + "_" +
                    Guid.NewGuid().ToString("N").Substring(0, 8));
                settled = true;
            }

            private static void RestoreFailedPublication(
                List<PublicationMoveRecord> promotedRecords,
                List<PublicationMoveRecord> backupRecords,
                string token)
            {
                string failedRoot = EndpointNativeGeneratedRoot +
                                    "/__FailedBasisPublication_" + token;
                EnsureFolder(failedRoot);
                for (int i = promotedRecords.Count - 1; i >= 0; i--)
                {
                    PublicationMoveRecord record = promotedRecords[i];
                    if (!PublicationAssetExists(record.FinalPath))
                        continue;
                    string failedPath = failedRoot + "/" + Path.GetFileName(record.FinalPath);
                    MovePublicationAssetOrThrow(
                        record.FinalPath,
                        failedPath,
                        "retain a failed endpoint-native publication artifact");
                }
                for (int i = backupRecords.Count - 1; i >= 0; i--)
                {
                    PublicationMoveRecord record = backupRecords[i];
                    if (!PublicationAssetExists(record.BackupPath))
                        continue;
                    MovePublicationAssetOrThrow(
                        record.BackupPath,
                        record.FinalPath,
                        "restore the prior endpoint-native publication artifact");
                }
            }

            private static bool PublicationAssetExists(string assetPath)
            {
                return AssetDatabase.LoadMainAssetAtPath(assetPath) != null ||
                       AssetDatabase.IsValidFolder(assetPath);
            }

            private static void MovePublicationAssetOrThrow(
                string source,
                string destination,
                string action)
            {
                if (!source.StartsWith(EndpointNativeGeneratedRoot + "/", StringComparison.Ordinal) ||
                    !destination.StartsWith(
                        EndpointNativeGeneratedRoot + "/",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Refusing endpoint-native publication move outside its owned root.");
                }
                string error = AssetDatabase.MoveAsset(source, destination);
                if (!string.IsNullOrWhiteSpace(error))
                    throw new InvalidOperationException("Unable to " + action + ": " + error);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            private readonly struct PublicationMoveRecord
            {
                public readonly string FinalPath;
                public readonly string BackupPath;

                public PublicationMoveRecord(string finalPath, string backupPath)
                {
                    FinalPath = finalPath;
                    BackupPath = backupPath;
                }
            }
        }

        private sealed class PromotionTransaction
        {
            private readonly MoveRecord[] promoted;
            private readonly MoveRecord[] backups;
            private readonly string ownedRoot;
            private bool settled;

            private PromotionTransaction(
                MoveRecord[] promotedRecords,
                MoveRecord[] backupRecords,
                string generatedRoot)
            {
                promoted = promotedRecords;
                backups = backupRecords;
                ownedRoot = generatedRoot;
            }

            public static PromotionTransaction Promote(string stagingPayload)
            {
                return Promote(stagingPayload, GeneratedRoot, ManifestFileName);
            }

            public static PromotionTransaction Promote(
                string stagingPayload,
                string generatedRoot,
                string manifestFileName)
            {
                ValidateOwnedGeneratedRoot(generatedRoot);
                if (!AssetDatabase.IsValidFolder(stagingPayload) ||
                    !stagingPayload.StartsWith(generatedRoot + "/__Staging_", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Refusing to promote a non-owned staging payload: '" + stagingPayload + "'.");
                }
                if (string.IsNullOrWhiteSpace(manifestFileName) ||
                    manifestFileName.IndexOf('/') >= 0 || manifestFileName.IndexOf('\\') >= 0)
                {
                    throw new InvalidOperationException("Generated manifest leaf name is invalid.");
                }
                string token = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) +
                               "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string backupRoot = generatedRoot + "/__Backup_" + token;
                EnsureFolder(backupRoot);
                RoomSpec[] promotionSpecs = string.Equals(
                    generatedRoot,
                    EndpointNativeGeneratedRoot,
                    StringComparison.Ordinal)
                    ? EndpointNativeRoomSpecs
                    : LegacyRoomSpecs;
                if (promotionSpecs.Length != 2)
                    throw new InvalidOperationException("Generated promotion requires exactly two room specs.");
                string[] leafNames =
                {
                    promotionSpecs[0].RoomId,
                    promotionSpecs[1].RoomId,
                    manifestFileName
                };
                var backupRecords = new List<MoveRecord>();
                var promotedRecords = new List<MoveRecord>();
                try
                {
                    for (int i = 0; i < leafNames.Length; i++)
                    {
                        string finalPath = generatedRoot + "/" + leafNames[i];
                        if (!AssetExists(finalPath))
                            continue;
                        string backupPath = backupRoot + "/" + leafNames[i];
                        MoveOwnedAssetOrThrow(
                            finalPath,
                            backupPath,
                            "backup existing generated payload leaf",
                            generatedRoot);
                        backupRecords.Add(new MoveRecord(finalPath, backupPath));
                    }
                    for (int i = 0; i < leafNames.Length; i++)
                    {
                        string sourcePath = stagingPayload + "/" + leafNames[i];
                        string finalPath = generatedRoot + "/" + leafNames[i];
                        if (!AssetExists(sourcePath))
                            throw new InvalidOperationException("Staging payload is missing required leaf: '" + sourcePath + "'.");
                        MoveOwnedAssetOrThrow(
                            sourcePath,
                            finalPath,
                            "promote staged generated payload leaf",
                            generatedRoot);
                        promotedRecords.Add(new MoveRecord(finalPath, sourcePath));
                    }
                    // The staging directory is now empty. Rename rather than delete it so every
                    // agent-created filesystem mutation remains inspectable/recoverable.
                    string archivedStaging = generatedRoot + "/__PromotedStaging_" + token;
                    MoveOwnedAssetOrThrow(
                        stagingPayload,
                        archivedStaging,
                        "archive empty promoted staging root",
                        generatedRoot);
                }
                catch
                {
                    RestoreFailedPromotion(promotedRecords, backupRecords, token, generatedRoot);
                    throw;
                }
                return new PromotionTransaction(
                    promotedRecords.ToArray(),
                    backupRecords.ToArray(),
                    generatedRoot);
            }

            public void Commit()
            {
                settled = true;
            }

            public void RollBack()
            {
                if (settled)
                    return;
                RestoreFailedPromotion(
                    new List<MoveRecord>(promoted),
                    new List<MoveRecord>(backups),
                    DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) + "_" +
                    Guid.NewGuid().ToString("N").Substring(0, 8),
                    ownedRoot);
                settled = true;
            }

            private static void RestoreFailedPromotion(
                List<MoveRecord> promotedRecords,
                List<MoveRecord> backupRecords,
                string token,
                string generatedRoot)
            {
                ValidateOwnedGeneratedRoot(generatedRoot);
                string failedRoot = generatedRoot + "/__Failed_" + token;
                EnsureFolder(failedRoot);
                for (int i = promotedRecords.Count - 1; i >= 0; i--)
                {
                    MoveRecord record = promotedRecords[i];
                    if (!AssetExists(record.FinalPath))
                        continue;
                    MoveOwnedAssetOrThrow(
                        record.FinalPath,
                        failedRoot + "/" + Path.GetFileName(record.FinalPath),
                        "retain failed promoted payload leaf",
                        generatedRoot);
                }
                for (int i = backupRecords.Count - 1; i >= 0; i--)
                {
                    MoveRecord record = backupRecords[i];
                    if (AssetExists(record.BackupPath))
                    {
                        MoveOwnedAssetOrThrow(
                            record.BackupPath,
                            record.FinalPath,
                            "restore prior generated payload leaf",
                            generatedRoot);
                    }
                }
            }

            private static bool AssetExists(string assetPath)
            {
                return AssetDatabase.LoadMainAssetAtPath(assetPath) != null || AssetDatabase.IsValidFolder(assetPath);
            }

            private static void MoveOwnedAssetOrThrow(
                string source,
                string destination,
                string action,
                string generatedRoot)
            {
                ValidateOwnedGeneratedRoot(generatedRoot);
                if (!source.StartsWith(generatedRoot + "/", StringComparison.Ordinal) ||
                    !destination.StartsWith(generatedRoot + "/", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Refusing generated-folder move outside the owned root.");
                }
                string error = AssetDatabase.MoveAsset(source, destination);
                if (!string.IsNullOrWhiteSpace(error))
                    throw new InvalidOperationException("Unable to " + action + ": " + error);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            private readonly struct MoveRecord
            {
                public readonly string FinalPath;
                public readonly string BackupPath;

                public MoveRecord(string finalPath, string backupPath)
                {
                    FinalPath = finalPath;
                    BackupPath = backupPath;
                }
            }
        }

        private sealed class RoomSpec
        {
            public readonly string RoomId;
            public readonly string ProductionPrefabPath;
            public readonly string P0BakeDataPath;
            public readonly string P100BakeDataPath;
            public readonly string ReceiverCapturePath;
            public readonly string EndpointProfilePath;
            public readonly int ExpectedProductionRendererCount;
            public readonly int ExpectedCaptureRendererCount;

            public RoomSpec(
                string roomId,
                string prefabPath,
                string p0BakeDataPath,
                string p100BakeDataPath,
                string receiverCapturePath,
                string endpointProfilePath,
                int expectedProductionRendererCount,
                int expectedCaptureRendererCount)
            {
                RoomId = roomId;
                ProductionPrefabPath = prefabPath;
                P0BakeDataPath = p0BakeDataPath;
                P100BakeDataPath = p100BakeDataPath;
                ReceiverCapturePath = receiverCapturePath;
                EndpointProfilePath = endpointProfilePath;
                ExpectedProductionRendererCount = expectedProductionRendererCount;
                ExpectedCaptureRendererCount = expectedCaptureRendererCount;
            }
        }

        private sealed class ResolvedRoom
        {
            public readonly RoomSpec Spec;
            public readonly GameObject Prefab;
            public readonly DungeonTileBakeData P0;
            public readonly DungeonTileBakeData P100;
            public readonly DungeonPortalReceiverResponseCapture CaptureAsset;
            public readonly DungeonPortalEndpointProfile Endpoint;
            public readonly DungeonPortalReceiverResponseCapture.CaptureState Baseline;
            public readonly DungeonPortalReceiverResponseCapture.CaptureState Full;
            public readonly CanonicalRendererWork[] CanonicalRenderers;
            public readonly DungeonPortalBakedBasisDoorAngleCapture AngleCapture;
            public readonly string AngleCaptureAssetPath;

            public ResolvedRoom(
                RoomSpec spec,
                GameObject prefab,
                DungeonTileBakeData p0,
                DungeonTileBakeData p100,
                DungeonPortalReceiverResponseCapture captureAsset,
                DungeonPortalEndpointProfile endpoint,
                DungeonPortalReceiverResponseCapture.CaptureState baseline,
                DungeonPortalReceiverResponseCapture.CaptureState full,
                CanonicalRendererWork[] canonicalRenderers,
                DungeonPortalBakedBasisDoorAngleCapture angleCapture = null,
                string angleCaptureAssetPath = "")
            {
                Spec = spec;
                Prefab = prefab;
                P0 = p0;
                P100 = p100;
                CaptureAsset = captureAsset;
                Endpoint = endpoint;
                Baseline = baseline;
                Full = full;
                CanonicalRenderers = canonicalRenderers;
                AngleCapture = angleCapture;
                AngleCaptureAssetPath = angleCaptureAssetPath ?? string.Empty;
            }
        }

        private sealed class CanonicalRendererWork
        {
            public string CanonicalKey;
            public string ProductionRelativePath;
            public int Occurrence;
            public DungeonTileBakeData.RendererBakeEntry P0;
            public DungeonTileBakeData.RendererBakeEntry P100;
            public string ProductionStablePath;
            public int ProductionComponentOrdinal;
            public Mesh ProductionMesh;
            public string ProductionMeshAssetGuid;
            public long ProductionMeshLocalId;
            public string ProductionMeshUv2Hash;
            public LightmapUvMode LightmapUvMode;
            public DungeonPortalReceiverResponseCapture.CaptureRenderer? BaselineCapture;
            public DungeonPortalReceiverResponseCapture.CaptureRenderer? FullCapture;
        }
    }
}
