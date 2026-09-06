using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DungeonPortalBakedBasisPoC
{
    /// <summary>
    /// Executes composition entirely on the GPU and copies the final RTs into unique,
    /// Texture2D-backed LightmapData slots. Direction maps are reconstructed from a
    /// luminance-weighted directional moment rather than blended as encoded RGB values.
    /// </summary>
    internal sealed class DungeonPortalBakedBasisGpuComposer : IDisposable
    {
        internal readonly struct Contribution
        {
            public readonly DungeonPortalBakedRoomBasisData.ResponseAtlas Atlas;
            public readonly Color RgbCoefficient;
            public readonly float ScalarWeight;

            public Contribution(
                DungeonPortalBakedRoomBasisData.ResponseAtlas atlas,
                Color rgbCoefficient,
                float scalarWeight)
            {
                Atlas = atlas;
                RgbCoefficient = rgbCoefficient;
                ScalarWeight = scalarWeight;
            }
        }

        private const int BaseColorPass = 0;
        private const int AddColorPass = 1;
        private const int FinalizeColorPass = 2;
        private const int BaseMomentPass = 3;
        private const int AddMomentPass = 4;
        private const int EncodeDirectionPass = 5;
        private const int RequiredPassCount = 6;

        private static readonly int BaseColor0Id = Shader.PropertyToID("_BaseColor0");
        private static readonly int BaseColor1Id = Shader.PropertyToID("_BaseColor1");
        private static readonly int BaseDirection0Id = Shader.PropertyToID("_BaseDirection0");
        private static readonly int BaseDirection1Id = Shader.PropertyToID("_BaseDirection1");
        private static readonly int BasePowerId = Shader.PropertyToID("_BasePower");
        private static readonly int SourceMipId = Shader.PropertyToID("_SourceMip");
        private static readonly int AccumulatorId = Shader.PropertyToID("_Accumulator");
        private static readonly int ComposedColorId = Shader.PropertyToID("_ComposedColor");
        private static readonly int OffColorId = Shader.PropertyToID("_OffColor");
        private static readonly int OnColorId = Shader.PropertyToID("_OnColor");
        private static readonly int OffDirectionId = Shader.PropertyToID("_OffDirection");
        private static readonly int OnDirectionId = Shader.PropertyToID("_OnDirection");
        private static readonly int ColorDeltaId = Shader.PropertyToID("_ColorDelta");
        private static readonly int DirectionalMomentDeltaId =
            Shader.PropertyToID("_DirectionalMomentDelta");
        private static readonly int ColorDeltaPositiveId =
            Shader.PropertyToID("_ColorDeltaPositive");
        private static readonly int ColorDeltaNegativeId =
            Shader.PropertyToID("_ColorDeltaNegative");
        private static readonly int DirectionalMomentDeltaPositiveId =
            Shader.PropertyToID("_DirectionalMomentDeltaPositive");
        private static readonly int DirectionalMomentDeltaNegativeId =
            Shader.PropertyToID("_DirectionalMomentDeltaNegative");
        private static readonly int ColorDeltaPositiveScalesId =
            Shader.PropertyToID("_ColorDeltaPositiveScales");
        private static readonly int ColorDeltaNegativeScalesId =
            Shader.PropertyToID("_ColorDeltaNegativeScales");
        private static readonly int DirectionalMomentDeltaPositiveScalesId =
            Shader.PropertyToID("_DirectionalMomentDeltaPositiveScales");
        private static readonly int DirectionalMomentDeltaNegativeScalesId =
            Shader.PropertyToID("_DirectionalMomentDeltaNegativeScales");
        private static readonly int ResponseEncodingId = Shader.PropertyToID("_ResponseEncoding");
        private static readonly int RgbCoefficientId = Shader.PropertyToID("_RgbCoefficient");
        private static readonly int ScalarWeightId = Shader.PropertyToID("_ScalarWeight");

        private readonly Material material;
        private RenderTexture colorA;
        private RenderTexture colorB;
        private RenderTexture momentA;
        private RenderTexture momentB;
        private RenderTexture directionOutput;
        private int targetWidth;
        private int targetHeight;
        private readonly Vector4[] colorPositiveScaleBuffer =
            new Vector4[DungeonPortalBakedRoomBasisData.NormalizedDeltaMaximumMipCount];
        private readonly Vector4[] colorNegativeScaleBuffer =
            new Vector4[DungeonPortalBakedRoomBasisData.NormalizedDeltaMaximumMipCount];
        private readonly Vector4[] momentPositiveScaleBuffer =
            new Vector4[DungeonPortalBakedRoomBasisData.NormalizedDeltaMaximumMipCount];
        private readonly Vector4[] momentNegativeScaleBuffer =
            new Vector4[DungeonPortalBakedRoomBasisData.NormalizedDeltaMaximumMipCount];

        private DungeonPortalBakedBasisGpuComposer(Material compositionMaterial)
        {
            material = compositionMaterial;
        }

        internal static bool TryCreate(
            Shader shader,
            out DungeonPortalBakedBasisGpuComposer composer,
            out string failure)
        {
            composer = null;
            if (shader == null || !shader.isSupported)
            {
                failure = "The explicit baked-basis composition shader is missing or unsupported.";
                return false;
            }

            if (shader.passCount < RequiredPassCount)
            {
                failure = $"Composition shader '{shader.name}' has {shader.passCount} passes; " +
                          $"DPBB-1 requires at least {RequiredPassCount}.";
                return false;
            }

            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
            {
                failure = "This device does not support the required RGBAHalf texture/RT format.";
                return false;
            }

            CopyTextureSupport copySupport = SystemInfo.copyTextureSupport;
            if ((copySupport & CopyTextureSupport.Basic) == 0 ||
                (copySupport & CopyTextureSupport.DifferentTypes) == 0)
            {
                failure = $"This device cannot copy a RenderTexture into the required Texture2D " +
                          $"lightmap slot. Copy support: {copySupport}.";
                return false;
            }

            var runtimeMaterial = new Material(shader)
            {
                name = "__DungeonPortalBakedBasisCompositionMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
            composer = new DungeonPortalBakedBasisGpuComposer(runtimeMaterial);
            failure = null;
            return true;
        }

        internal static Texture2D CreateRuntimeLightmapTexture(
            Texture2D samplingPrototype,
            string name)
        {
            if (samplingPrototype == null)
                throw new ArgumentNullException(nameof(samplingPrototype));

            var texture = new Texture2D(
                samplingPrototype.width,
                samplingPrototype.height,
                TextureFormat.RGBAHalf,
                samplingPrototype.mipmapCount,
                true,
                true)
            {
                name = name,
                filterMode = samplingPrototype.filterMode,
                wrapModeU = samplingPrototype.wrapModeU,
                wrapModeV = samplingPrototype.wrapModeV,
                wrapModeW = samplingPrototype.wrapModeW,
                anisoLevel = samplingPrototype.anisoLevel,
                mipMapBias = samplingPrototype.mipMapBias,
                hideFlags = HideFlags.HideAndDontSave
            };
            // Allocate every native mip before Graphics.CopyTexture targets it. This is the
            // only CPU upload; no Apply call is permitted after GPU composition begins.
            texture.Apply(false, false);
            return texture;
        }

        internal bool TryCompose(
            DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket,
            float basePower01,
            IReadOnlyList<Contribution> contributions,
            Texture2D outputColor,
            Texture2D outputDirection,
            out string failure)
        {
            if (bucket == null || outputColor == null || outputDirection == null ||
                contributions == null)
            {
                failure = "GPU composition received a missing bucket, output, or contribution list.";
                return false;
            }

            if (!TryValidateOutput(
                    outputColor,
                    bucket,
                    bucket.Power0Color,
                    "color",
                    out failure) ||
                !TryValidateOutput(
                    outputDirection,
                    bucket,
                    bucket.Power0Direction,
                    "direction",
                    out failure))
            {
                return false;
            }

            // Validate every contribution before writing any output mip. The room data asset
            // has already validated texture layouts; this protects direct/runtime callers from
            // receiving a partially updated mip chain due to an invalid weight or encoding.
            for (int i = 0; i < contributions.Count; i++)
            {
                if (!TrySetContribution(contributions[i], bucket, out failure))
                    return false;
            }

            bool previousSrgbWrite = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                SetBaseTextures(bucket, Mathf.Clamp01(basePower01));

                for (int mip = 0; mip < bucket.MipCount; mip++)
                {
                    int mipWidth = Mathf.Max(1, bucket.Width >> mip);
                    int mipHeight = Mathf.Max(1, bucket.Height >> mip);
                    if (!EnsureTargets(mipWidth, mipHeight, out failure))
                        return false;

                    material.SetFloat(SourceMipId, mip);
                    Draw(colorA, BaseColorPass);
                    Draw(momentA, BaseMomentPass);

                    RenderTexture currentColor = colorA;
                    RenderTexture nextColor = colorB;
                    RenderTexture currentMoment = momentA;
                    RenderTexture nextMoment = momentB;

                    for (int i = 0; i < contributions.Count; i++)
                    {
                        Contribution contribution = contributions[i];
                        if (!TrySetContribution(contribution, bucket, out failure))
                            return false;

                        material.SetTexture(AccumulatorId, currentColor);
                        Draw(nextColor, AddColorPass);
                        Swap(ref currentColor, ref nextColor);

                        material.SetTexture(AccumulatorId, currentMoment);
                        Draw(nextMoment, AddMomentPass);
                        Swap(ref currentMoment, ref nextMoment);
                    }

                    // Signed response deltas remain signed through the whole sum. Clamp color
                    // once after all doors so the result does not depend on contribution order.
                    material.SetTexture(AccumulatorId, currentColor);
                    Draw(nextColor, FinalizeColorPass);
                    Swap(ref currentColor, ref nextColor);

                    material.SetTexture(AccumulatorId, currentMoment);
                    material.SetTexture(ComposedColorId, currentColor);
                    Draw(directionOutput, EncodeDirectionPass);

                    if (currentColor.graphicsFormat != outputColor.graphicsFormat ||
                        directionOutput.graphicsFormat != outputDirection.graphicsFormat)
                    {
                        failure = "Runtime RT and Texture2D graphics formats differ; GPU copy " +
                                  "was refused instead of invoking a platform conversion.";
                        return false;
                    }

                    Graphics.CopyTexture(currentColor, 0, 0, outputColor, 0, mip);
                    Graphics.CopyTexture(directionOutput, 0, 0, outputDirection, 0, mip);
                }

                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = $"GPU lightmap composition failed: {exception.GetType().Name}: " +
                          exception.Message;
                return false;
            }
            finally
            {
                GL.sRGBWrite = previousSrgbWrite;
            }
        }

        public void Dispose()
        {
            DestroyTarget(ref directionOutput);
            DestroyTarget(ref momentB);
            DestroyTarget(ref momentA);
            DestroyTarget(ref colorB);
            DestroyTarget(ref colorA);
            targetWidth = 0;
            targetHeight = 0;
            DestroyObject(material);
        }

        private void SetBaseTextures(
            DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket,
            float basePower01)
        {
            material.SetTexture(BaseColor0Id, bucket.Power0Color);
            material.SetTexture(BaseColor1Id, bucket.Power100Color);
            material.SetTexture(BaseDirection0Id, bucket.Power0Direction);
            material.SetTexture(BaseDirection1Id, bucket.Power100Direction);
            material.SetFloat(BasePowerId, basePower01);
        }

        private bool TrySetContribution(
            Contribution contribution,
            DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket,
            out string failure)
        {
            DungeonPortalBakedRoomBasisData.ResponseAtlas atlas = contribution.Atlas;
            if (atlas == null || !IsFiniteNonNegative(contribution.RgbCoefficient) ||
                !IsFiniteNonNegative(contribution.ScalarWeight))
            {
                failure = "A GPU response contribution is missing or has invalid weights.";
                return false;
            }

            material.SetColor(RgbCoefficientId, contribution.RgbCoefficient);
            material.SetFloat(ScalarWeightId, contribution.ScalarWeight);
            if (atlas.Encoding ==
                DungeonPortalBakedRoomBasisData.ResponseAtlasEncoding.OnOffStatePair)
            {
                material.SetFloat(ResponseEncodingId, 0f);
                material.SetTexture(OffColorId, atlas.OffColor);
                material.SetTexture(OnColorId, atlas.OnColor);
                material.SetTexture(OffDirectionId, atlas.OffDirection);
                material.SetTexture(OnDirectionId, atlas.OnDirection);
                material.SetTexture(ColorDeltaId, Texture2D.blackTexture);
                material.SetTexture(DirectionalMomentDeltaId, Texture2D.blackTexture);
                SetCompactTexturesToBlack();
            }
            else if (atlas.Encoding ==
                     DungeonPortalBakedRoomBasisData.ResponseAtlasEncoding.PrecomputedDelta)
            {
                material.SetFloat(ResponseEncodingId, 1f);
                material.SetTexture(OffColorId, Texture2D.blackTexture);
                material.SetTexture(OnColorId, Texture2D.blackTexture);
                material.SetTexture(OffDirectionId, Texture2D.blackTexture);
                material.SetTexture(OnDirectionId, Texture2D.blackTexture);
                material.SetTexture(ColorDeltaId, atlas.ColorDelta);
                material.SetTexture(DirectionalMomentDeltaId, atlas.DirectionalMomentDelta);
                SetCompactTexturesToBlack();
            }
            else if (atlas.Encoding == DungeonPortalBakedRoomBasisData
                         .ResponseAtlasEncoding.NormalizedPositiveNegativeDelta)
            {
                if (!TryValidateCompactContribution(atlas, bucket, out failure))
                    return false;

                material.SetFloat(ResponseEncodingId, 2f);
                material.SetTexture(OffColorId, Texture2D.blackTexture);
                material.SetTexture(OnColorId, Texture2D.blackTexture);
                material.SetTexture(OffDirectionId, Texture2D.blackTexture);
                material.SetTexture(OnDirectionId, Texture2D.blackTexture);
                material.SetTexture(ColorDeltaId, Texture2D.blackTexture);
                material.SetTexture(DirectionalMomentDeltaId, Texture2D.blackTexture);
                material.SetTexture(ColorDeltaPositiveId, atlas.ColorDeltaPositive);
                material.SetTexture(ColorDeltaNegativeId, atlas.ColorDeltaNegative);
                material.SetTexture(
                    DirectionalMomentDeltaPositiveId,
                    atlas.DirectionalMomentDeltaPositive);
                material.SetTexture(
                    DirectionalMomentDeltaNegativeId,
                    atlas.DirectionalMomentDeltaNegative);

                CopyScaleArray(atlas.ColorDeltaPositiveScales, colorPositiveScaleBuffer);
                CopyScaleArray(atlas.ColorDeltaNegativeScales, colorNegativeScaleBuffer);
                CopyScaleArray(
                    atlas.DirectionalMomentDeltaPositiveScales,
                    momentPositiveScaleBuffer);
                CopyScaleArray(
                    atlas.DirectionalMomentDeltaNegativeScales,
                    momentNegativeScaleBuffer);
                material.SetVectorArray(ColorDeltaPositiveScalesId, colorPositiveScaleBuffer);
                material.SetVectorArray(ColorDeltaNegativeScalesId, colorNegativeScaleBuffer);
                material.SetVectorArray(
                    DirectionalMomentDeltaPositiveScalesId,
                    momentPositiveScaleBuffer);
                material.SetVectorArray(
                    DirectionalMomentDeltaNegativeScalesId,
                    momentNegativeScaleBuffer);
            }
            else
            {
                failure = $"Unsupported response encoding '{atlas.Encoding}'.";
                return false;
            }

            failure = null;
            return true;
        }

        private void SetCompactTexturesToBlack()
        {
            material.SetTexture(ColorDeltaPositiveId, Texture2D.blackTexture);
            material.SetTexture(ColorDeltaNegativeId, Texture2D.blackTexture);
            material.SetTexture(DirectionalMomentDeltaPositiveId, Texture2D.blackTexture);
            material.SetTexture(DirectionalMomentDeltaNegativeId, Texture2D.blackTexture);
        }

        private static void CopyScaleArray(Vector4[] source, Vector4[] destination)
        {
            for (int i = 0; i < destination.Length; i++)
                destination[i] = i < source.Length ? source[i] : Vector4.one;
        }

        private static bool TryValidateCompactContribution(
            DungeonPortalBakedRoomBasisData.ResponseAtlas atlas,
            DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket,
            out string failure)
        {
            if (bucket == null)
            {
                failure = "A compact GPU response has no native endpoint bucket.";
                return false;
            }

            TextureFormat colorStorageFormat = atlas.ColorDeltaPositive != null
                ? atlas.ColorDeltaPositive.format
                : TextureFormat.RGBA32;
            if ((colorStorageFormat != TextureFormat.BC6H &&
                 colorStorageFormat != TextureFormat.RGBAHalf) ||
                atlas.ColorDeltaNegative == null ||
                atlas.ColorDeltaNegative.format != colorStorageFormat)
            {
                failure = "Compact GPU response color deltas must use BC6H or RGBAHalf " +
                          "in the same format for the positive/negative pair.";
                return false;
            }
            TextureFormat momentStorageFormat = atlas.DirectionalMomentDeltaPositive != null
                ? atlas.DirectionalMomentDeltaPositive.format
                : TextureFormat.RGBA32;
            if ((momentStorageFormat != TextureFormat.BC7 &&
                 momentStorageFormat != TextureFormat.RGBAHalf) ||
                atlas.DirectionalMomentDeltaNegative == null ||
                atlas.DirectionalMomentDeltaNegative.format != momentStorageFormat)
            {
                failure = "Compact GPU response directional-moment deltas must use BC7 or RGBAHalf " +
                          "in the same format for the positive/negative pair.";
                return false;
            }

            if (!TryValidateCompactTexture(
                    atlas.ColorDeltaPositive,
                    bucket.Power0Color,
                    bucket,
                    colorStorageFormat,
                    "positive color",
                    out failure) ||
                !TryValidateCompactTexture(
                    atlas.ColorDeltaNegative,
                    bucket.Power0Color,
                    bucket,
                    colorStorageFormat,
                    "negative color",
                    out failure) ||
                !TryValidateCompactTexture(
                    atlas.DirectionalMomentDeltaPositive,
                    bucket.Power0Direction,
                    bucket,
                    momentStorageFormat,
                    "positive directional moment",
                    out failure) ||
                !TryValidateCompactTexture(
                    atlas.DirectionalMomentDeltaNegative,
                    bucket.Power0Direction,
                    bucket,
                    momentStorageFormat,
                    "negative directional moment",
                    out failure))
            {
                return false;
            }

            int mipCount = atlas.ColorDeltaPositive.mipmapCount;
            if (!TryValidateCompactScales(
                    atlas.ColorDeltaPositiveScales,
                    mipCount,
                    "positive color",
                    out failure) ||
                !TryValidateCompactScales(
                    atlas.ColorDeltaNegativeScales,
                    mipCount,
                    "negative color",
                    out failure) ||
                !TryValidateCompactScales(
                    atlas.DirectionalMomentDeltaPositiveScales,
                    mipCount,
                    "positive directional moment",
                    out failure) ||
                !TryValidateCompactScales(
                    atlas.DirectionalMomentDeltaNegativeScales,
                    mipCount,
                    "negative directional moment",
                    out failure))
            {
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryValidateCompactTexture(
            Texture2D texture,
            Texture2D samplingPrototype,
            DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket,
            TextureFormat expectedFormat,
            string label,
            out string failure)
        {
            if (texture == null || samplingPrototype == null)
            {
                failure = $"Compact GPU response {label} texture or sampling prototype is missing.";
                return false;
            }
            if (texture.width != bucket.Width || texture.height != bucket.Height ||
                texture.mipmapCount != bucket.MipCount)
            {
                failure = $"Compact GPU response {label} does not match native endpoint " +
                          $"dimensions or its {bucket.MipCount}-mip chain.";
                return false;
            }
            if (texture.format != expectedFormat ||
                GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat) ||
                !SystemInfo.SupportsTextureFormat(texture.format))
            {
                failure = $"Compact GPU response {label} must use supported linear " +
                          $"{expectedFormat}, not {texture.format}/{texture.graphicsFormat}.";
                return false;
            }
            if (texture.filterMode != samplingPrototype.filterMode ||
                texture.wrapModeU != samplingPrototype.wrapModeU ||
                texture.wrapModeV != samplingPrototype.wrapModeV ||
                texture.wrapModeW != samplingPrototype.wrapModeW ||
                texture.anisoLevel != samplingPrototype.anisoLevel ||
                !Mathf.Approximately(texture.mipMapBias, samplingPrototype.mipMapBias))
            {
                failure = $"Compact GPU response {label} does not match the native endpoint " +
                          "mip sampling state.";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryValidateCompactScales(
            Vector4[] scales,
            int mipCount,
            string label,
            out string failure)
        {
            if (mipCount <= 0 ||
                mipCount > DungeonPortalBakedRoomBasisData.NormalizedDeltaMaximumMipCount ||
                scales == null || scales.Length != mipCount)
            {
                failure = $"Compact GPU response {label} needs exactly {mipCount} scale " +
                          $"entries within the 1.." +
                          $"{DungeonPortalBakedRoomBasisData.NormalizedDeltaMaximumMipCount} " +
                          "supported mip range.";
                return false;
            }

            for (int mip = 0; mip < scales.Length; mip++)
            {
                Vector4 scale = scales[mip];
                if (!IsFinitePositive(scale.x) || !IsFinitePositive(scale.y) ||
                    !IsFinitePositive(scale.z) || !IsFinitePositive(scale.w))
                {
                    failure = $"Compact GPU response {label} scale at mip {mip} must be " +
                              "finite and strictly positive.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private bool EnsureTargets(int width, int height, out string failure)
        {
            if (width <= 0 || height <= 0 || width > SystemInfo.maxTextureSize ||
                height > SystemInfo.maxTextureSize)
            {
                failure = $"Invalid composition target size {width}x{height}.";
                return false;
            }

            if (targetWidth == width && targetHeight == height &&
                colorA != null && colorA.IsCreated())
            {
                failure = null;
                return true;
            }

            DestroyTarget(ref directionOutput);
            DestroyTarget(ref momentB);
            DestroyTarget(ref momentA);
            DestroyTarget(ref colorB);
            DestroyTarget(ref colorA);

            try
            {
                colorA = CreateTarget(width, height, "ColorA");
                colorB = CreateTarget(width, height, "ColorB");
                momentA = CreateTarget(width, height, "MomentA");
                momentB = CreateTarget(width, height, "MomentB");
                directionOutput = CreateTarget(width, height, "DirectionOutput");
                targetWidth = width;
                targetHeight = height;
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = $"Could not allocate RGBAHalf composition targets: " +
                          $"{exception.GetType().Name}: {exception.Message}";
                return false;
            }
        }

        private static RenderTexture CreateTarget(int width, int height, string suffix)
        {
            var target = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear)
            {
                name = "__DungeonPortalBakedBasis_" + suffix,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!target.Create())
            {
                DestroyObject(target);
                throw new InvalidOperationException("RenderTexture.Create returned false for " + suffix + ".");
            }

            return target;
        }

        private void Draw(RenderTexture destination, int pass)
        {
            Graphics.Blit(Texture2D.blackTexture, destination, material, pass);
        }

        private static bool TryValidateOutput(
            Texture2D output,
            DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket,
            Texture2D samplingPrototype,
            string label,
            out string failure)
        {
            if (output.width != bucket.Width || output.height != bucket.Height ||
                output.format != TextureFormat.RGBAHalf ||
                output.mipmapCount != bucket.MipCount)
            {
                failure = $"Runtime {label} output does not match bucket '{bucket.BucketId}' " +
                          $"dimensions, RGBAHalf format, or {bucket.MipCount}-mip layout.";
                return false;
            }

            if (samplingPrototype == null ||
                output.filterMode != samplingPrototype.filterMode ||
                output.wrapModeU != samplingPrototype.wrapModeU ||
                output.wrapModeV != samplingPrototype.wrapModeV ||
                output.wrapModeW != samplingPrototype.wrapModeW ||
                output.anisoLevel != samplingPrototype.anisoLevel ||
                !Mathf.Approximately(output.mipMapBias, samplingPrototype.mipMapBias))
            {
                failure = $"Runtime {label} output does not preserve bucket " +
                          $"'{bucket.BucketId}' canonical sampling state.";
                return false;
            }

            failure = null;
            return true;
        }

        private static void Swap(ref RenderTexture left, ref RenderTexture right)
        {
            RenderTexture temporary = left;
            left = right;
            right = temporary;
        }

        private static void DestroyTarget(ref RenderTexture target)
        {
            if (target == null)
                return;
            target.Release();
            DestroyObject(target);
            target = null;
        }

        private static void DestroyObject(Object value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(value);
            else
                Object.DestroyImmediate(value);
        }

        private static bool IsFiniteNonNegative(Color value)
        {
            return IsFiniteNonNegative(value.r) && IsFiniteNonNegative(value.g) &&
                   IsFiniteNonNegative(value.b);
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }
    }
}
