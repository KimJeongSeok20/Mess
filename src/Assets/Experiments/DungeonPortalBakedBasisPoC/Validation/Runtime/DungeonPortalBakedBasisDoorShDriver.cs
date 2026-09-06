using System;
using System.Collections.Generic;
using DungeonPortalBakedBasisPoC;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonPortalBakedBasisPoC.Validation
{
    /// <summary>
    /// Applies the room-local baked SH basis to the split door renderers without changing their
    /// mesh or materials. At D0 it restores the exact pre-existing LightProbeUsage and every
    /// global/per-material MaterialPropertyBlock captured on entry, so the baked-basis validation
    /// scene retains Adjacent-OFF parity with the production clone.
    /// </summary>
    [DefaultExecutionOrder(150)]
    [DisallowMultipleComponent]
    public sealed class DungeonPortalBakedBasisDoorShDriver : MonoBehaviour
    {
        private const float ZeroApertureEpsilon = 0.0001f;
        private const string AdministrativeToStartSuffix = "/AdministrativeToStart";
        private const string StartToAdministrativeSuffix = "/StartToAdministrative";

        [SerializeField] private DungeonPortalBakedBasisConnectionDriver connectionDriver;
        [SerializeField] private Transform doorRoot;
        [SerializeField] private Transform startRoomRoot;
        [SerializeField] private Transform administrativeRoomRoot;
        [SerializeField] private DungeonPortalBakedRoomBasisData startRoomBasis;
        [SerializeField] private DungeonPortalBakedRoomBasisData administrativeRoomBasis;
        [SerializeField] private string startDoorId;
        [SerializeField] private string administrativeDoorId;

        private readonly List<RendererBinding> bindings = new List<RendererBinding>();
        private readonly SphericalHarmonicsL2[] singleProbe = new SphericalHarmonicsL2[1];
        private readonly Vector4[] singleOcclusion = new Vector4[1];
        private readonly float[] doorwayProbeDelta =
            new float[DungeonPortalBakedRoomBasisData.SphericalHarmonicsCoefficientCount];
        private MaterialPropertyBlock workingBlock;
        private bool initialized;
        private bool snapshotCaptured;
        private bool applied;
        private bool faultLatched;
        private string faultReason;

        public bool IsActive => applied;
        public bool IsFaultLatched => faultLatched;
        public string FaultReason => faultReason;
        public int BoundRendererCount => bindings.Count;

        public void ConfigureAuthoring(
            DungeonPortalBakedBasisConnectionDriver driver,
            Transform doorTransform,
            Transform startTransform,
            Transform administrativeTransform,
            DungeonPortalBakedRoomBasisData startBasis,
            DungeonPortalBakedRoomBasisData administrativeBasis,
            string stableStartDoorId,
            string stableAdministrativeDoorId)
        {
            connectionDriver = driver;
            doorRoot = doorTransform;
            startRoomRoot = startTransform;
            administrativeRoomRoot = administrativeTransform;
            startRoomBasis = startBasis;
            administrativeRoomBasis = administrativeBasis;
            startDoorId = stableStartDoorId ?? string.Empty;
            administrativeDoorId = stableAdministrativeDoorId ?? string.Empty;
            initialized = false;
            faultLatched = false;
            faultReason = null;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
                return;
            if (!TryInitialize(out string failure))
                LatchFault(failure);
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || faultLatched)
                return;
            if (!initialized && !TryInitialize(out string initializationFailure))
            {
                LatchFault(initializationFailure);
                return;
            }

            if (!TryApplyNow(out string failure))
                LatchFault(failure);
        }

        public bool TryValidateConfiguration(out string failure)
        {
            if (connectionDriver == null || doorRoot == null || startRoomRoot == null ||
                administrativeRoomRoot == null || startRoomBasis == null ||
                administrativeRoomBasis == null || string.IsNullOrWhiteSpace(startDoorId) ||
                string.IsNullOrWhiteSpace(administrativeDoorId) ||
                string.IsNullOrWhiteSpace(connectionDriver.ConnectionId))
            {
                failure = "Door SH driver is missing its connection, room, or doorway contract.";
                return false;
            }

            string administrativeToStartId =
                connectionDriver.ConnectionId + AdministrativeToStartSuffix;
            string startToAdministrativeId =
                connectionDriver.ConnectionId + StartToAdministrativeSuffix;
            if (!TryValidateIncomingConnection(
                    connectionDriver.StartRoomCompositor,
                    administrativeToStartId,
                    startDoorId,
                    administrativeRoomBasis,
                    administrativeDoorId,
                    out failure) ||
                !TryValidateIncomingConnection(
                    connectionDriver.AdministrativeRoomCompositor,
                    startToAdministrativeId,
                    administrativeDoorId,
                    startRoomBasis,
                    startDoorId,
                    out failure))
            {
                return false;
            }

            bool startDefinitionValid = startRoomBasis.TryValidateDefinition(
                out string startDefinitionFailure);
            bool administrativeDefinitionValid = administrativeRoomBasis.TryValidateDefinition(
                out string administrativeDefinitionFailure);
            if (connectionDriver.StartRoomCompositor == null ||
                connectionDriver.AdministrativeRoomCompositor == null ||
                connectionDriver.StartRoomCompositor.RoomBasis != startRoomBasis ||
                connectionDriver.AdministrativeRoomCompositor.RoomBasis != administrativeRoomBasis ||
                !startDefinitionValid || !administrativeDefinitionValid)
            {
                failure = "Door SH room-basis definition mismatch. Start=" +
                          startDefinitionFailure + " Administrative=" +
                          administrativeDefinitionFailure;
                return false;
            }

            if (!TryResolveDirectionalBasis(
                    startRoomBasis,
                    startDoorId,
                    administrativeRoomBasis,
                    administrativeDoorId,
                    out _,
                    out failure) ||
                !TryResolveDirectionalBasis(
                    administrativeRoomBasis,
                    administrativeDoorId,
                    startRoomBasis,
                    startDoorId,
                    out _,
                    out failure))
            {
                return false;
            }

            failure = null;
            return true;
        }

        public bool TryApplyNow(out string failure)
        {
            if (!initialized && !TryInitialize(out failure))
                return false;
            if (faultLatched)
            {
                failure = "Door SH driver is fault-latched: " + faultReason;
                return false;
            }

            float aperture = connectionDriver.AdjacentTransportEnabled
                ? Mathf.Clamp01(connectionDriver.CurrentAperture01)
                : 0f;
            if (aperture <= ZeroApertureEpsilon || connectionDriver.IsFaultLatched)
            {
                RestoreOriginalState();
                failure = null;
                return true;
            }

            if (!TryComposeProbe(
                    connectionDriver.StartRoomCompositor,
                    connectionDriver.ConnectionId + AdministrativeToStartSuffix,
                    startRoomBasis,
                    connectionDriver.CurrentStartPower01,
                    out SphericalHarmonicsL2 startFacingProbe,
                    out failure) ||
                !TryComposeProbe(
                    connectionDriver.AdministrativeRoomCompositor,
                    connectionDriver.ConnectionId + StartToAdministrativeSuffix,
                    administrativeRoomBasis,
                    connectionDriver.CurrentAdministrativePower01,
                    out SphericalHarmonicsL2 administrativeFacingProbe,
                    out failure))
            {
                return false;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                RendererBinding binding = bindings[i];
                SphericalHarmonicsL2 target = binding.Group == DungeonDoorProbeRendererGroup.Group.Edge
                    ? Average(startFacingProbe, administrativeFacingProbe)
                    : binding.StartFacing
                        ? startFacingProbe
                        : administrativeFacingProbe;
                ApplyProbe(binding, target);
            }

            applied = true;
            failure = null;
            return true;
        }

        public bool TryRestoreOriginal(out string failure)
        {
            RestoreOriginalState();
            failure = null;
            return true;
        }

        private bool TryInitialize(out string failure)
        {
            if (initialized)
            {
                failure = null;
                return true;
            }

            if (!TryValidateConfiguration(out failure))
                return false;
            if (doorRoot.GetComponentsInChildren<DungeonDoorDualSideProbeReceiver>(true).Length > 0)
            {
                DungeonDoorDualSideProbeReceiver[] conflicts =
                    doorRoot.GetComponentsInChildren<DungeonDoorDualSideProbeReceiver>(true);
                for (int i = 0; i < conflicts.Length; i++)
                {
                    if (conflicts[i] != null && conflicts[i].enabled)
                    {
                        failure = "The production DungeonDoorDualSideProbeReceiver is enabled; " +
                                  "disable it in the validation clone before enabling DPBB door SH.";
                        return false;
                    }
                }
            }

            CacheBindings(out failure);
            if (failure != null)
                return false;
            CaptureOriginalState();
            workingBlock = new MaterialPropertyBlock();
            initialized = true;
            failure = null;
            return true;
        }

        private void CacheBindings(out string failure)
        {
            bindings.Clear();
            DungeonDoorProbeRendererGroup[] groups =
                doorRoot.GetComponentsInChildren<DungeonDoorProbeRendererGroup>(true);
            if (groups == null || groups.Length == 0)
            {
                failure = "The door has no split DungeonDoorProbeRendererGroup bindings.";
                return;
            }

            Vector3 startCenter = EstimateRendererCenter(startRoomRoot);
            Vector3 administrativeCenter = EstimateRendererCenter(administrativeRoomRoot);
            var uniqueRenderers = new HashSet<Renderer>();
            for (int i = 0; i < groups.Length; i++)
            {
                DungeonDoorProbeRendererGroup group = groups[i];
                if (group == null)
                    continue;
                Renderer renderer = group.GetComponent<Renderer>();
                if (renderer == null)
                    renderer = group.GetComponentInChildren<Renderer>(true);
                if (renderer == null || !uniqueRenderers.Add(renderer))
                    continue;

                Vector3 normal = group.transform.forward;
                if (group.ProbeGroup == DungeonDoorProbeRendererGroup.Group.NegativeZ)
                    normal = -normal;
                if (normal.sqrMagnitude <= 0.00001f)
                    normal = doorRoot.forward;
                normal.Normalize();
                Vector3 center = renderer.bounds.center;
                bool startFacing = Vector3.Dot(normal, startCenter - center) >=
                                   Vector3.Dot(normal, administrativeCenter - center);

                bindings.Add(new RendererBinding(
                    renderer,
                    group.ProbeGroup,
                    startFacing,
                    renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0));
            }

            if (bindings.Count == 0)
            {
                failure = "No unique split-door renderers could be resolved.";
                return;
            }

            failure = null;
        }

        private void CaptureOriginalState()
        {
            if (snapshotCaptured)
                return;
            for (int i = 0; i < bindings.Count; i++)
                bindings[i].CaptureOriginalState();
            snapshotCaptured = true;
        }

        private void ApplyProbe(RendererBinding binding, SphericalHarmonicsL2 probe)
        {
            Renderer renderer = binding.Renderer;
            if (renderer == null)
                return;
            if (renderer.lightProbeUsage != LightProbeUsage.CustomProvided)
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;

            singleProbe[0] = probe;
            singleOcclusion[0] = Vector4.one;
            renderer.GetPropertyBlock(workingBlock);
            CopyProbeToWorkingBlock();
            renderer.SetPropertyBlock(workingBlock);

            for (int materialIndex = 0; materialIndex < binding.MaterialCount; materialIndex++)
            {
                renderer.GetPropertyBlock(workingBlock, materialIndex);
                CopyProbeToWorkingBlock();
                renderer.SetPropertyBlock(workingBlock, materialIndex);
            }
        }

        private void CopyProbeToWorkingBlock()
        {
            workingBlock.CopySHCoefficientArraysFrom(singleProbe);
            workingBlock.CopyProbeOcclusionArrayFrom(singleOcclusion);
        }

        private void RestoreOriginalState()
        {
            if (!snapshotCaptured)
                return;
            for (int i = 0; i < bindings.Count; i++)
                bindings[i].RestoreOriginalState();
            applied = false;
        }

        private bool TryComposeProbe(
            DungeonPortalBakedBasisRoomCompositor receiverCompositor,
            string incomingConnectionId,
            DungeonPortalBakedRoomBasisData receiverBasis,
            float receiverPower01,
            out SphericalHarmonicsL2 result,
            out string failure)
        {
            result = default;
            DungeonPortalBakedRoomBasisData.OptionalAmbientProbeStates ambient =
                receiverBasis != null ? receiverBasis.AmbientProbeStates : null;
            if (receiverCompositor == null || receiverCompositor.RoomBasis != receiverBasis ||
                string.IsNullOrWhiteSpace(incomingConnectionId) ||
                ambient == null || !ambient.Authored ||
                !IsValidSh(ambient.Power0Coefficients) ||
                !IsValidSh(ambient.Power100Coefficients))
            {
                failure = "Door SH requires a matching receiver compositor and authored 27-float " +
                          "room ambient basis.";
                return false;
            }

            if (!receiverCompositor.TryEvaluateIncomingDoorwayProbeDelta(
                    incomingConnectionId,
                    doorwayProbeDelta,
                    out string deltaFailure))
            {
                failure = $"Door SH incoming connection '{incomingConnectionId}' failed closed: " +
                          deltaFailure;
                return false;
            }

            float receiverPower = Mathf.Clamp01(receiverPower01);
            for (int channel = 0; channel < 3; channel++)
            {
                for (int coefficient = 0; coefficient < 9; coefficient++)
                {
                    int index = channel * 9 + coefficient;
                    result[channel, coefficient] = Mathf.Lerp(
                        ambient.Power0Coefficients[index],
                        ambient.Power100Coefficients[index],
                        receiverPower);
                    result[channel, coefficient] += doorwayProbeDelta[index];
                }
            }

            failure = null;
            return true;
        }

        private static bool TryResolveDirectionalBasis(
            DungeonPortalBakedRoomBasisData receiverBasis,
            string receiverDoorId,
            DungeonPortalBakedRoomBasisData sourceBasis,
            string sourceDoorId,
            out DungeonPortalBakedRoomBasisData.ReceiverDoorBasis receiverDoor,
            out string failure)
        {
            receiverDoor = null;
            DungeonPortalBakedRoomBasisData.OptionalAmbientProbeStates ambient =
                receiverBasis != null ? receiverBasis.AmbientProbeStates : null;
            if (ambient == null || !ambient.Authored || !IsValidSh(ambient.Power0Coefficients) ||
                !IsValidSh(ambient.Power100Coefficients) ||
                !receiverBasis.TryGetReceiverDoor(receiverDoorId, out receiverDoor) ||
                !sourceBasis.TryGetSourceDoor(sourceDoorId,
                    out DungeonPortalBakedRoomBasisData.SourceDoorBasis sourceDoor))
            {
                failure = "Door SH base/doorway basis is incomplete.";
                return false;
            }

            DungeonPortalBakedRoomBasisData.ReceiverResponseLobe[] lobes =
                receiverDoor.ResponseLobes;
            if (!receiverDoor.UsesPoseResponses)
            {
                if (!TryValidateProbeLobes("legacy D100", lobes, sourceDoor, out failure))
                    return false;
            }
            else
            {
                DungeonPortalBakedRoomBasisData.ReceiverDoorPoseResponse[] poses =
                    receiverDoor.PoseResponses;
                for (int poseIndex = 0; poseIndex < poses.Length; poseIndex++)
                {
                    DungeonPortalBakedRoomBasisData.ReceiverDoorPoseResponse pose =
                        poses[poseIndex];
                    string label = pose != null
                        ? $"D{Mathf.RoundToInt(pose.OpenFraction * 100f):000}"
                        : $"pose {poseIndex}";
                    if (pose == null)
                    {
                        failure = $"Door SH {label} response is missing.";
                        return false;
                    }
                    if (!TryValidateProbeLobes(
                            label,
                            pose.ResponseLobes,
                            sourceDoor,
                            out failure))
                    {
                        return false;
                    }
                }
            }

            failure = null;
            return true;
        }

        private static bool TryValidateIncomingConnection(
            DungeonPortalBakedBasisRoomCompositor receiverCompositor,
            string connectionId,
            string expectedReceiverDoorId,
            DungeonPortalBakedRoomBasisData expectedSourceBasis,
            string expectedSourceDoorId,
            out string failure)
        {
            if (receiverCompositor == null || string.IsNullOrWhiteSpace(connectionId) ||
                string.IsNullOrWhiteSpace(expectedReceiverDoorId) || expectedSourceBasis == null ||
                string.IsNullOrWhiteSpace(expectedSourceDoorId))
            {
                failure = "Door SH incoming connection expectation is incomplete.";
                return false;
            }

            DungeonPortalBakedBasisRoomCompositor.IncomingDoorState match = null;
            int matchCount = 0;
            DungeonPortalBakedBasisRoomCompositor.IncomingDoorState[] incoming =
                receiverCompositor.IncomingDoors;
            for (int i = 0; i < incoming.Length; i++)
            {
                DungeonPortalBakedBasisRoomCompositor.IncomingDoorState state = incoming[i];
                if (state == null ||
                    !string.Equals(state.ConnectionId, connectionId, StringComparison.Ordinal))
                {
                    continue;
                }

                match = state;
                matchCount++;
            }

            if (matchCount != 1 || match == null ||
                !string.Equals(
                    match.ReceiverDoorId,
                    expectedReceiverDoorId,
                    StringComparison.Ordinal) ||
                match.SourceRoomBasis != expectedSourceBasis ||
                !string.Equals(match.SourceDoorId, expectedSourceDoorId, StringComparison.Ordinal))
            {
                failure = $"Door SH incoming connection '{connectionId}' does not exactly match " +
                          $"receiver '{expectedReceiverDoorId}' and source " +
                          $"'{expectedSourceBasis.RoomId}/{expectedSourceDoorId}'.";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryValidateProbeLobes(
            string poseLabel,
            DungeonPortalBakedRoomBasisData.ReceiverResponseLobe[] lobes,
            DungeonPortalBakedRoomBasisData.SourceDoorBasis sourceDoor,
            out string failure)
        {
            lobes = lobes ?? Array.Empty<DungeonPortalBakedRoomBasisData.ReceiverResponseLobe>();
            if (lobes.Length == 0)
            {
                failure = $"Door SH {poseLabel} has no response lobes.";
                return false;
            }

            for (int i = 0; i < lobes.Length; i++)
            {
                DungeonPortalBakedRoomBasisData.ReceiverResponseLobe lobe = lobes[i];
                DungeonPortalBakedRoomBasisData.OptionalDoorwayProbeResponse response =
                    lobe != null ? lobe.DoorwayProbeResponse : null;
                if (lobe == null || response == null || !response.Authored ||
                    !IsValidSh(response.OffCoefficients) || !IsValidSh(response.OnCoefficients) ||
                    !sourceDoor.TryGetCoefficient(lobe.BasisId, out _))
                {
                    failure = $"Door SH {poseLabel} lobe '" +
                              (lobe != null ? lobe.BasisId : "<null>") +
                              "' is missing authored OFF/ON SH or its source coefficient.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private static SphericalHarmonicsL2 Average(
            SphericalHarmonicsL2 left,
            SphericalHarmonicsL2 right)
        {
            SphericalHarmonicsL2 result = default;
            for (int channel = 0; channel < 3; channel++)
            for (int coefficient = 0; coefficient < 9; coefficient++)
                result[channel, coefficient] =
                    (left[channel, coefficient] + right[channel, coefficient]) * 0.5f;
            return result;
        }

        private static bool IsValidSh(float[] values)
        {
            if (values == null || values.Length != DungeonPortalBakedRoomBasisData.SphericalHarmonicsCoefficientCount)
                return false;
            for (int i = 0; i < values.Length; i++)
            {
                if (float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                    return false;
            }
            return true;
        }

        private static Vector3 EstimateRendererCenter(Transform roomRoot)
        {
            Renderer[] renderers = roomRoot.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds bounds = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return hasBounds ? bounds.center : roomRoot.position;
        }

        private void LatchFault(string reason)
        {
            if (faultLatched)
                return;
            faultLatched = true;
            faultReason = string.IsNullOrWhiteSpace(reason)
                ? "Unknown door SH failure."
                : reason;
            RestoreOriginalState();
            Debug.LogError("[DungeonPortalBakedBasisDoorShDriver] '" + name +
                           "' disabled fail-closed: " + faultReason, this);
        }

        private void OnDisable()
        {
            RestoreOriginalState();
        }

        private sealed class RendererBinding
        {
            public readonly Renderer Renderer;
            public readonly DungeonDoorProbeRendererGroup.Group Group;
            public readonly bool StartFacing;
            public readonly int MaterialCount;

            private LightProbeUsage originalLightProbeUsage;
            private MaterialPropertyBlock originalGlobalBlock;
            private bool originalGlobalEmpty;
            private MaterialPropertyBlock[] originalMaterialBlocks;
            private bool[] originalMaterialEmpty;
            private bool captured;

            public RendererBinding(
                Renderer renderer,
                DungeonDoorProbeRendererGroup.Group group,
                bool startFacing,
                int materialCount)
            {
                Renderer = renderer;
                Group = group;
                StartFacing = startFacing;
                MaterialCount = Mathf.Max(0, materialCount);
            }

            public void CaptureOriginalState()
            {
                if (captured || Renderer == null)
                    return;
                captured = true;
                originalLightProbeUsage = Renderer.lightProbeUsage;
                originalGlobalBlock = new MaterialPropertyBlock();
                Renderer.GetPropertyBlock(originalGlobalBlock);
                originalGlobalEmpty = originalGlobalBlock.isEmpty;
                originalMaterialBlocks = new MaterialPropertyBlock[MaterialCount];
                originalMaterialEmpty = new bool[MaterialCount];
                for (int i = 0; i < MaterialCount; i++)
                {
                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    Renderer.GetPropertyBlock(block, i);
                    originalMaterialBlocks[i] = block;
                    originalMaterialEmpty[i] = block.isEmpty;
                }
            }

            public void RestoreOriginalState()
            {
                if (!captured || Renderer == null)
                    return;
                Renderer.lightProbeUsage = originalLightProbeUsage;
                Renderer.SetPropertyBlock(originalGlobalEmpty ? null : originalGlobalBlock);
                for (int i = 0; i < MaterialCount; i++)
                {
                    Renderer.SetPropertyBlock(
                        originalMaterialEmpty[i] ? null : originalMaterialBlocks[i], i);
                }
            }
        }
    }
}
