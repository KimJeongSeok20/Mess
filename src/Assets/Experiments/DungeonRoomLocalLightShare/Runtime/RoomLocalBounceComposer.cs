using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    /// <summary>
    /// Adds the baked unit response (Full - DirectOnly) to private runtime lightmaps.
    /// The authored bounce atlas layout is remapped into the live production layout;
    /// live renderer lightmap scale/offset values are never replaced by bake-workspace UVs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomLocalBounceComposer : MonoBehaviour
    {
        private const int BaseColorPass = 0;
        private const int AddColorPass = 1;
        private const int FinalizeColorPass = 2;
        private const int BaseMomentPass = 3;
        private const int AddMomentPass = 4;
        private const int EncodeDirectionPass = 5;
        private const int RequiredPassCount = 6;
        private const float MinimumContribution = 0.0001f;
        private const float MinimumRectScale = 0.000001f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseDirectionId = Shader.PropertyToID("_BaseDirection");
        private static readonly int AccumulatorId = Shader.PropertyToID("_Accumulator");
        private static readonly int ComposedColorId = Shader.PropertyToID("_ComposedColor");
        private static readonly int FullColorId = Shader.PropertyToID("_FullColor");
        private static readonly int DirectOnlyColorId = Shader.PropertyToID("_DirectOnlyColor");
        private static readonly int FullDirectionId = Shader.PropertyToID("_FullDirection");
        private static readonly int DirectOnlyDirectionId = Shader.PropertyToID("_DirectOnlyDirection");
        private static readonly int TransferRgbId = Shader.PropertyToID("_TransferRgb");
        private static readonly int SourceRectId = Shader.PropertyToID("_SourceRect");
        private static readonly int DestinationRectId = Shader.PropertyToID("_DestinationRect");
        private static readonly int SourceMipId = Shader.PropertyToID("_SourceMip");

        [SerializeField] private IncomingBounceData bounceData;
        [SerializeField] private Transform roomRoot;
        [SerializeField] private Shader composeShader;
        [SerializeField, Min(0f)] private float responseScale = 1f;

        private readonly struct BindingRemap
        {
            public readonly int AuthoredAtlas;
            public readonly Vector4 SourceRect;
            public readonly Vector4 DestinationRect;

            public BindingRemap(int authoredAtlas, Vector4 sourceRect, Vector4 destinationRect)
            {
                AuthoredAtlas = authoredAtlas;
                SourceRect = sourceRect;
                DestinationRect = destinationRect;
            }
        }

        private sealed class SlotState
        {
            public Texture2D BaseColor;
            public Texture2D BaseDirection;
            public readonly List<BindingRemap> Remaps = new List<BindingRemap>();
        }

        private Material material;
        private RoomLocalLightmapRegistry.Registration registration;
        private LightmapData[] workingSlots = Array.Empty<LightmapData>();
        private SlotState[] slotStates = Array.Empty<SlotState>();
        private readonly List<RoomLocalLightmapRegistry.AssignmentRequest> assignments =
            new List<RoomLocalLightmapRegistry.AssignmentRequest>();
        private RenderTexture colorA;
        private RenderTexture colorB;
        private RenderTexture momentA;
        private RenderTexture momentB;
        private RenderTexture directionOutput;
        private int targetWidth;
        private int targetHeight;
        private bool active;
        private bool faultLatched;
        private string faultReason;

        public bool IsActive => active;
        public bool IsFaultLatched => faultLatched;
        public string FaultReason => faultReason;

        public void Configure(
            IncomingBounceData data,
            Transform root,
            Shader shader,
            float scale = 1f)
        {
            bounceData = data;
            roomRoot = root;
            composeShader = shader;
            responseScale = Mathf.Max(0f, scale);
        }

        public bool TryActivate(out string failure)
        {
            if (active)
            {
                failure = null;
                return true;
            }

            if (faultLatched)
            {
                failure = faultReason;
                return false;
            }

            if (bounceData == null)
            {
                failure = "Incoming bounce data is missing.";
                return false;
            }

            if (!bounceData.TryValidate(out failure))
                return false;
            if (roomRoot == null)
            {
                failure = "Bounce composer room root is missing.";
                return false;
            }

            if (composeShader == null)
                composeShader = Shader.Find("Hidden/DungeonRoomLocalLightShare/BounceCompose");
            if (composeShader == null || !composeShader.isSupported ||
                composeShader.passCount < RequiredPassCount)
            {
                failure = "Bounce compose shader is missing or unsupported.";
                return false;
            }

            if (!RoomLocalRendererKeys.TryBuildKeyMap(
                    roomRoot,
                    out Dictionary<string, Renderer> keyMap,
                    out failure))
                return false;

            if (!TryBuildAssignments(keyMap, out failure))
                return false;

            material = new Material(composeShader)
            {
                name = "__RoomLocalBounceComposeMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };

            if (!RoomLocalLightmapRegistry.TryRegister(
                    this,
                    workingSlots,
                    assignments,
                    out registration,
                    out failure))
            {
                DestroyObject(material);
                material = null;
                DestroyWorkingSlots();
                return false;
            }

            active = true;
            failure = null;
            return true;
        }

        public bool TrySetContribution(
            float openFraction,
            Color transferRadiance,
            out string failure)
        {
            Color transferRgb = ClampNonNegative(transferRadiance) * Mathf.Max(0f, responseScale);
            if (RoomLocalLightShareMath.Luminance(transferRgb) <= MinimumContribution ||
                openFraction <= MinimumContribution)
                return TryDeactivate(true, out failure);

            if (!active && !TryActivate(out failure))
                return false;

            if (!bounceData.TryGetSurroundingPoses(
                    openFraction,
                    out IncomingBounceData.PoseCapture lower,
                    out IncomingBounceData.PoseCapture upper,
                    out float blend,
                    out failure))
            {
                Latch(failure);
                return false;
            }

            if (!TryComposePoses(lower, upper, blend, transferRgb, out failure))
            {
                Latch(failure);
                return false;
            }

            failure = null;
            return true;
        }

        public bool TryDeactivate(bool restoreRenderers, out string failure)
        {
            failure = null;
            if (!active && registration == null)
                return true;

            bool ok = RoomLocalLightmapRegistry.TryUnregister(
                registration,
                restoreRenderers,
                out failure);
            registration = null;
            active = false;
            DestroyWorkingSlots();
            DestroyTargets();
            DestroyObject(material);
            material = null;
            return ok;
        }

        public bool TryReleaseForExternalLightmapApply(out string failure)
        {
            return TryDeactivate(false, out failure);
        }

        private void OnDisable()
        {
            TryDeactivate(true, out _);
        }

        private void OnDestroy()
        {
            TryDeactivate(true, out _);
        }

        private bool TryBuildAssignments(
            Dictionary<string, Renderer> keyMap,
            out string failure)
        {
            assignments.Clear();
            IncomingBounceData.RendererBinding[] bindings = bounceData.RendererBindings;
            LightmapData[] current = LightmapSettings.lightmaps;
            var slotByProductionAtlas = new Dictionary<int, int>();
            var slotList = new List<LightmapData>();
            var states = new List<SlotState>();

            for (int i = 0; i < bindings.Length; i++)
            {
                IncomingBounceData.RendererBinding binding = bindings[i];
                if (binding == null || string.IsNullOrEmpty(binding.canonicalKey))
                    continue;
                if (!keyMap.TryGetValue(binding.canonicalKey, out Renderer renderer) ||
                    renderer == null || renderer.lightmapIndex < 0)
                    continue;

                int productionAtlas = renderer.lightmapIndex;
                if (current == null || productionAtlas >= current.Length ||
                    current[productionAtlas] == null ||
                    current[productionAtlas].lightmapColor == null)
                {
                    failure = "Renderer '" + binding.canonicalKey +
                              "' has no current production lightmap to copy.";
                    return false;
                }

                if (!IsUsableRect(binding.lightmapScaleOffset) ||
                    !IsUsableRect(renderer.lightmapScaleOffset))
                {
                    failure = "Renderer '" + binding.canonicalKey +
                              "' has an invalid authored or production lightmap rectangle.";
                    return false;
                }

                if (!slotByProductionAtlas.TryGetValue(productionAtlas, out int localSlot))
                {
                    LightmapData source = current[productionAtlas];
                    Texture2D baseDirection = source.lightmapDir != null
                        ? source.lightmapDir
                        : source.lightmapColor;
                    localSlot = slotList.Count;
                    slotByProductionAtlas.Add(productionAtlas, localSlot);
                    slotList.Add(new LightmapData
                    {
                        lightmapColor = CreateWorkingCopy(
                            source.lightmapColor,
                            "BounceColor_" + localSlot),
                        lightmapDir = CreateWorkingCopy(
                            baseDirection,
                            "BounceDir_" + localSlot)
                    });
                    states.Add(new SlotState
                    {
                        BaseColor = source.lightmapColor,
                        BaseDirection = baseDirection
                    });
                }

                states[localSlot].Remaps.Add(new BindingRemap(
                    binding.lightmapIndex,
                    binding.lightmapScaleOffset,
                    renderer.lightmapScaleOffset));
                assignments.Add(new RoomLocalLightmapRegistry.AssignmentRequest(
                    renderer,
                    localSlot,
                    renderer.lightmapScaleOffset));
            }

            if (assignments.Count == 0 || slotList.Count == 0)
            {
                failure = "No canonical bounce renderers matched the live room.";
                return false;
            }

            workingSlots = slotList.ToArray();
            slotStates = states.ToArray();
            failure = null;
            return true;
        }

        private bool TryComposePoses(
            IncomingBounceData.PoseCapture lower,
            IncomingBounceData.PoseCapture upper,
            float blend,
            Color transferRgb,
            out string failure)
        {
            if (lower == null || upper == null)
            {
                failure = "Bounce pose is missing.";
                return false;
            }

            float upperWeight = lower == upper ? 0f : Mathf.Clamp01(blend);
            float lowerWeight = 1f - upperWeight;
            bool previousSrgbWrite = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                for (int slot = 0; slot < workingSlots.Length; slot++)
                {
                    if (!ComposeSlot(
                            slotStates[slot],
                            lower,
                            lowerWeight,
                            upper,
                            upperWeight,
                            transferRgb,
                            workingSlots[slot].lightmapColor,
                            workingSlots[slot].lightmapDir,
                            out failure))
                        return false;
                }

                failure = null;
                return true;
            }
            finally
            {
                GL.sRGBWrite = previousSrgbWrite;
            }
        }

        private bool ComposeSlot(
            SlotState state,
            IncomingBounceData.PoseCapture lower,
            float lowerWeight,
            IncomingBounceData.PoseCapture upper,
            float upperWeight,
            Color transferRgb,
            Texture2D outputColor,
            Texture2D outputDirection,
            out string failure)
        {
            if (state == null || state.BaseColor == null || state.BaseDirection == null ||
                outputColor == null || outputDirection == null)
            {
                failure = "A bounce compose slot is missing a base or output atlas.";
                return false;
            }

            if (!EnsureTargets(outputColor.width, outputColor.height, out failure))
                return false;

            material.SetFloat(SourceMipId, 0f);
            material.SetTexture(BaseColorId, state.BaseColor);
            material.SetTexture(BaseDirectionId, state.BaseDirection);

            Draw(colorA, BaseColorPass);
            if (!ApplyPoseColor(state, lower, lowerWeight, transferRgb, ref colorA, ref colorB, out failure) ||
                !ApplyPoseColor(state, upper, upperWeight, transferRgb, ref colorA, ref colorB, out failure))
                return false;
            material.SetTexture(AccumulatorId, colorA);
            Draw(colorB, FinalizeColorPass);
            Swap(ref colorA, ref colorB);

            Draw(momentA, BaseMomentPass);
            if (!ApplyPoseMoment(state, lower, lowerWeight, transferRgb, ref momentA, ref momentB, out failure) ||
                !ApplyPoseMoment(state, upper, upperWeight, transferRgb, ref momentA, ref momentB, out failure))
                return false;

            material.SetTexture(AccumulatorId, momentA);
            material.SetTexture(ComposedColorId, colorA);
            Draw(directionOutput, EncodeDirectionPass);

            Graphics.CopyTexture(colorA, outputColor);
            Graphics.CopyTexture(directionOutput, outputDirection);
            failure = null;
            return true;
        }

        private bool ApplyPoseColor(
            SlotState state,
            IncomingBounceData.PoseCapture pose,
            float poseWeight,
            Color transferRgb,
            ref RenderTexture accumulator,
            ref RenderTexture destination,
            out string failure)
        {
            if (poseWeight <= MinimumContribution)
            {
                failure = null;
                return true;
            }

            Color weightedRgb = transferRgb * poseWeight;
            for (int i = 0; i < state.Remaps.Count; i++)
            {
                BindingRemap remap = state.Remaps[i];
                if (!TryBindPoseTextures(pose, remap, weightedRgb, out failure))
                    return false;
                material.SetTexture(AccumulatorId, accumulator);
                Draw(destination, AddColorPass);
                Swap(ref accumulator, ref destination);
            }

            failure = null;
            return true;
        }

        private bool ApplyPoseMoment(
            SlotState state,
            IncomingBounceData.PoseCapture pose,
            float poseWeight,
            Color transferRgb,
            ref RenderTexture accumulator,
            ref RenderTexture destination,
            out string failure)
        {
            if (poseWeight <= MinimumContribution)
            {
                failure = null;
                return true;
            }

            Color weightedRgb = transferRgb * poseWeight;
            for (int i = 0; i < state.Remaps.Count; i++)
            {
                BindingRemap remap = state.Remaps[i];
                if (!TryBindPoseTextures(pose, remap, weightedRgb, out failure))
                    return false;
                material.SetTexture(AccumulatorId, accumulator);
                Draw(destination, AddMomentPass);
                Swap(ref accumulator, ref destination);
            }

            failure = null;
            return true;
        }

        private bool TryBindPoseTextures(
            IncomingBounceData.PoseCapture pose,
            BindingRemap remap,
            Color transferRgb,
            out string failure)
        {
            int atlas = remap.AuthoredAtlas;
            if (pose == null || atlas < 0 || atlas >= pose.fullColor.Length)
            {
                failure = "Bounce pose is missing authored atlas " + atlas + ".";
                return false;
            }

            Texture2D fullColor = pose.fullColor[atlas];
            Texture2D directColor = pose.directOnlyColor[atlas];
            Texture2D fullDirection = pose.fullDirection[atlas];
            Texture2D directDirection = pose.directOnlyDirection[atlas];
            if (fullColor == null || directColor == null ||
                fullDirection == null || directDirection == null)
            {
                failure = "Bounce pose '" + pose.poseId + "' atlas " + atlas +
                          " is missing a required texture.";
                return false;
            }

            material.SetTexture(FullColorId, fullColor);
            material.SetTexture(DirectOnlyColorId, directColor);
            material.SetTexture(FullDirectionId, fullDirection);
            material.SetTexture(DirectOnlyDirectionId, directDirection);
            material.SetColor(TransferRgbId, transferRgb);
            material.SetVector(SourceRectId, remap.SourceRect);
            material.SetVector(DestinationRectId, remap.DestinationRect);
            failure = null;
            return true;
        }

        private bool EnsureTargets(int width, int height, out string failure)
        {
            if (width <= 0 || height <= 0)
            {
                failure = "Invalid bounce compose target size.";
                return false;
            }

            if (targetWidth == width && targetHeight == height && colorA != null)
            {
                failure = null;
                return true;
            }

            DestroyTargets();
            colorA = CreateTarget(width, height, "ColorA");
            colorB = CreateTarget(width, height, "ColorB");
            momentA = CreateTarget(width, height, "MomentA");
            momentB = CreateTarget(width, height, "MomentB");
            directionOutput = CreateTarget(width, height, "Direction");
            targetWidth = width;
            targetHeight = height;
            failure = null;
            return true;
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
                name = "__RoomLocalBounce_" + suffix,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!target.Create())
                throw new InvalidOperationException("RenderTexture.Create failed for " + suffix + ".");
            return target;
        }

        private void Draw(RenderTexture destination, int pass)
        {
            Graphics.Blit(Texture2D.blackTexture, destination, material, pass);
        }

        private static Texture2D CreateWorkingCopy(Texture2D prototype, string name)
        {
            var texture = new Texture2D(
                prototype.width,
                prototype.height,
                TextureFormat.RGBAHalf,
                false,
                true)
            {
                name = name,
                filterMode = prototype.filterMode,
                wrapMode = prototype.wrapMode,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Apply(false, false);
            return texture;
        }

        private void DestroyWorkingSlots()
        {
            for (int i = 0; i < workingSlots.Length; i++)
            {
                if (workingSlots[i] == null)
                    continue;
                DestroyObject(workingSlots[i].lightmapColor);
                DestroyObject(workingSlots[i].lightmapDir);
            }

            workingSlots = Array.Empty<LightmapData>();
            slotStates = Array.Empty<SlotState>();
            assignments.Clear();
        }

        private void DestroyTargets()
        {
            DestroyObject(directionOutput);
            DestroyObject(momentB);
            DestroyObject(momentA);
            DestroyObject(colorB);
            DestroyObject(colorA);
            directionOutput = null;
            momentB = null;
            momentA = null;
            colorB = null;
            colorA = null;
            targetWidth = 0;
            targetHeight = 0;
        }

        private void Latch(string reason)
        {
            faultLatched = true;
            faultReason = reason;
            TryDeactivate(true, out _);
        }

        private static bool IsUsableRect(Vector4 rect)
        {
            // Unity can author slightly negative atlas offsets (typically a few texels)
            // for dilation/padding. Those are valid and the lightmap texture's Clamp mode
            // handles them. Reject only non-finite values or unusable scales.
            return IsFinite(rect.x) && IsFinite(rect.y) &&
                   IsFinite(rect.z) && IsFinite(rect.w) &&
                   rect.x > MinimumRectScale && rect.y > MinimumRectScale;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static Color ClampNonNegative(Color color)
        {
            return new Color(
                Mathf.Max(0f, color.r),
                Mathf.Max(0f, color.g),
                Mathf.Max(0f, color.b),
                1f);
        }

        private static void Swap(ref RenderTexture left, ref RenderTexture right)
        {
            RenderTexture temporary = left;
            left = right;
            right = temporary;
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }
}
