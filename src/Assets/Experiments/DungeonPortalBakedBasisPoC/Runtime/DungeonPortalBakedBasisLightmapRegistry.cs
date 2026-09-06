using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonPortalBakedBasisPoC
{
    /// <summary>
    /// Owns the process-wide LightmapSettings append while one or more room compositors are
    /// active. All mutations are centralized so rooms can be removed in any order without
    /// leaving stale indices on surviving rooms.
    /// </summary>
    public static class DungeonPortalBakedBasisLightmapRegistry
    {
        internal readonly struct AssignmentRequest
        {
            public readonly Renderer Renderer;
            public readonly int BucketIndex;
            public readonly Vector4 RuntimeScaleOffset;

            public AssignmentRequest(
                Renderer renderer,
                int bucketIndex,
                Vector4 runtimeScaleOffset)
            {
                Renderer = renderer;
                BucketIndex = bucketIndex;
                RuntimeScaleOffset = runtimeScaleOffset;
            }
        }

        internal readonly struct OriginalRendererState
        {
            public readonly Renderer Renderer;
            public readonly int LightmapIndex;
            public readonly Vector4 LightmapScaleOffset;

            public OriginalRendererState(Renderer renderer)
            {
                Renderer = renderer;
                LightmapIndex = renderer.lightmapIndex;
                LightmapScaleOffset = renderer.lightmapScaleOffset;
            }

            public void Restore()
            {
                if (Renderer == null)
                    return;
                Renderer.lightmapIndex = LightmapIndex;
                Renderer.lightmapScaleOffset = LightmapScaleOffset;
            }
        }

        internal sealed class Registration
        {
            internal readonly MonoBehaviour Owner;
            internal readonly LightmapData[] Slots;
            internal readonly AssignmentRequest[] Assignments;
            internal readonly OriginalRendererState[] Originals;
            internal int FirstGlobalSlot;

            internal Registration(
                MonoBehaviour owner,
                LightmapData[] slots,
                AssignmentRequest[] assignments,
                OriginalRendererState[] originals)
            {
                Owner = owner;
                Slots = slots;
                Assignments = assignments;
                Originals = originals;
                FirstGlobalSlot = -1;
            }
        }

        private static readonly List<Registration> Registrations = new List<Registration>();
        private static readonly Dictionary<Renderer, Registration> RendererOwners =
            new Dictionary<Renderer, Registration>();

        private static LightmapData[] baseLightmaps;
        private static LightmapsMode baseMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Registrations.Clear();
            RendererOwners.Clear();
            baseLightmaps = null;
            baseMode = default;
        }

        internal static bool TryRegister(
            MonoBehaviour owner,
            LightmapData[] runtimeSlots,
            IReadOnlyList<AssignmentRequest> assignmentRequests,
            out Registration registration,
            out string failure)
        {
            registration = null;
            if (owner == null || runtimeSlots == null || runtimeSlots.Length == 0 ||
                assignmentRequests == null || assignmentRequests.Count == 0)
            {
                failure = "Registry owner, runtime slots, or renderer assignments are missing.";
                return false;
            }

            for (int i = 0; i < Registrations.Count; i++)
            {
                if (Registrations[i].Owner == owner)
                {
                    failure = $"Compositor '{owner.name}' is already registered.";
                    return false;
                }
            }

            if (LightmapSettings.lightmapsMode != LightmapsMode.CombinedDirectional)
            {
                failure = $"The baked-basis compositor requires CombinedDirectional, but the " +
                          $"active scene uses {LightmapSettings.lightmapsMode}.";
                return false;
            }

            if (Registrations.Count == 0)
            {
                baseLightmaps = CloneArray(LightmapSettings.lightmaps);
                baseMode = LightmapSettings.lightmapsMode;
            }
            else if (!CurrentLayoutMatchesExpected(out failure))
            {
                return false;
            }

            LightmapData[] currentBase = baseLightmaps ?? Array.Empty<LightmapData>();
            var assignments = new AssignmentRequest[assignmentRequests.Count];
            var originals = new OriginalRendererState[assignmentRequests.Count];
            var localRenderers = new HashSet<Renderer>();
            for (int i = 0; i < assignmentRequests.Count; i++)
            {
                AssignmentRequest request = assignmentRequests[i];
                Renderer renderer = request.Renderer;
                if (renderer == null || !localRenderers.Add(renderer) ||
                    RendererOwners.ContainsKey(renderer))
                {
                    failure = $"Renderer assignment {i} is missing, duplicated, or already owned " +
                              "by another active baked-basis compositor.";
                    ResetEmptyCaptureIfNeeded();
                    return false;
                }

                if (request.BucketIndex < 0 || request.BucketIndex >= runtimeSlots.Length ||
                    !IsFinite(request.RuntimeScaleOffset) || renderer.lightmapIndex < 0 ||
                    renderer.lightmapIndex >= currentBase.Length)
                {
                    failure = $"Renderer '{renderer.name}' has an invalid bucket, scale/offset, " +
                              "or original lightmap index.";
                    ResetEmptyCaptureIfNeeded();
                    return false;
                }

                assignments[i] = request;
                originals[i] = new OriginalRendererState(renderer);
            }

            for (int i = 0; i < runtimeSlots.Length; i++)
            {
                LightmapData slot = runtimeSlots[i];
                if (slot == null || slot.lightmapColor == null || slot.lightmapDir == null)
                {
                    failure = $"Runtime lightmap slot {i} is missing its color or direction texture.";
                    ResetEmptyCaptureIfNeeded();
                    return false;
                }
            }

            registration = new Registration(owner, runtimeSlots, assignments, originals);
            LightmapData[] previous = CloneArray(LightmapSettings.lightmaps);
            Registrations.Add(registration);
            for (int i = 0; i < assignments.Length; i++)
                RendererOwners.Add(assignments[i].Renderer, registration);

            try
            {
                ApplyManagedLayout();
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                Registrations.Remove(registration);
                for (int i = 0; i < assignments.Length; i++)
                    RendererOwners.Remove(assignments[i].Renderer);
                for (int i = originals.Length - 1; i >= 0; i--)
                    originals[i].Restore();

                LightmapSettings.lightmapsMode = baseMode;
                LightmapSettings.lightmaps = previous;
                ApplySurvivingRendererIndices();
                registration = null;
                failure = $"Could not install runtime lightmap slots: " +
                          $"{exception.GetType().Name}: {exception.Message}";
                ResetEmptyCaptureIfNeeded();
                return false;
            }
        }

        internal static bool TryValidate(
            Registration registration,
            out string failure)
        {
            if (registration == null || !Registrations.Contains(registration))
            {
                failure = "The room compositor no longer has an active registry lease.";
                return false;
            }

            if (!CurrentLayoutMatchesExpected(out failure))
                return false;

            for (int i = 0; i < registration.Assignments.Length; i++)
            {
                AssignmentRequest assignment = registration.Assignments[i];
                int expectedIndex = registration.FirstGlobalSlot + assignment.BucketIndex;
                if (assignment.Renderer == null ||
                    assignment.Renderer.lightmapIndex != expectedIndex ||
                    assignment.Renderer.lightmapScaleOffset != assignment.RuntimeScaleOffset)
                {
                    failure = $"Assigned renderer {i} no longer has its leased lightmap index/ST.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        /// <summary>
        /// Returns the production lightmap layout with registry-owned compositor slots removed.
        /// Environment fingerprints use this view so changing a DPBB working texture is not
        /// mistaken for a StartMap environment mutation, while any external layout drift still
        /// fails closed through the registry's exact layout validator.
        /// </summary>
        public static bool TryGetProductionLayoutForFingerprint(
            out LightmapsMode mode,
            out LightmapData[] productionLightmaps,
            out string failure)
        {
            if (Registrations.Count == 0)
            {
                mode = LightmapSettings.lightmapsMode;
                productionLightmaps = CloneArray(LightmapSettings.lightmaps);
                failure = null;
                return true;
            }

            if (!CurrentLayoutMatchesExpected(out failure))
            {
                mode = default;
                productionLightmaps = Array.Empty<LightmapData>();
                return false;
            }

            mode = baseMode;
            productionLightmaps = CloneArray(baseLightmaps);
            failure = null;
            return true;
        }

        internal static bool TryUnregister(
            Registration registration,
            out string failure)
        {
            if (registration == null || !Registrations.Contains(registration))
            {
                failure = null;
                return true;
            }

            bool layoutWasExpected = CurrentLayoutMatchesExpected(out string layoutFailure);
            Registrations.Remove(registration);
            for (int i = 0; i < registration.Assignments.Length; i++)
                RendererOwners.Remove(registration.Assignments[i].Renderer);

            Exception restoreException = null;
            try
            {
                if (Registrations.Count == 0)
                {
                    LightmapSettings.lightmapsMode = baseMode;
                    LightmapSettings.lightmaps = CloneArray(baseLightmaps);
                }
                else
                {
                    ApplyManagedLayout();
                }

                for (int i = registration.Originals.Length - 1; i >= 0; i--)
                    registration.Originals[i].Restore();
            }
            catch (Exception exception)
            {
                restoreException = exception;
                // Renderer restoration is independent of the global array setter.
                for (int i = registration.Originals.Length - 1; i >= 0; i--)
                    registration.Originals[i].Restore();
            }

            if (Registrations.Count == 0)
            {
                baseLightmaps = null;
                baseMode = default;
            }

            if (restoreException != null)
            {
                failure = $"Registry teardown threw {restoreException.GetType().Name}: " +
                          restoreException.Message;
                return false;
            }

            if (!layoutWasExpected)
            {
                failure = "An external system changed LightmapSettings while the registry was " +
                          $"active. The registry restored its authoritative layout. {layoutFailure}";
                return false;
            }

            failure = null;
            return true;
        }

        private static void ApplyManagedLayout()
        {
            LightmapData[] expected = BuildExpectedLayout();
            LightmapSettings.lightmapsMode = LightmapsMode.CombinedDirectional;
            LightmapSettings.lightmaps = expected;
            ApplySurvivingRendererIndices();
        }

        private static LightmapData[] BuildExpectedLayout()
        {
            int total = baseLightmaps != null ? baseLightmaps.Length : 0;
            for (int i = 0; i < Registrations.Count; i++)
                total += Registrations[i].Slots.Length;

            var result = new LightmapData[total];
            int cursor = 0;
            if (baseLightmaps != null)
            {
                Array.Copy(baseLightmaps, 0, result, 0, baseLightmaps.Length);
                cursor = baseLightmaps.Length;
            }

            for (int registrationIndex = 0;
                 registrationIndex < Registrations.Count;
                 registrationIndex++)
            {
                Registration registration = Registrations[registrationIndex];
                registration.FirstGlobalSlot = cursor;
                Array.Copy(registration.Slots, 0, result, cursor, registration.Slots.Length);
                cursor += registration.Slots.Length;
            }

            return result;
        }

        private static void ApplySurvivingRendererIndices()
        {
            for (int registrationIndex = 0;
                 registrationIndex < Registrations.Count;
                 registrationIndex++)
            {
                Registration registration = Registrations[registrationIndex];
                for (int assignmentIndex = 0;
                     assignmentIndex < registration.Assignments.Length;
                     assignmentIndex++)
                {
                    AssignmentRequest assignment = registration.Assignments[assignmentIndex];
                    if (assignment.Renderer == null)
                        throw new MissingReferenceException(
                            "An explicitly assigned room renderer was destroyed while its " +
                            "baked-basis lightmap lease was active.");
                    assignment.Renderer.lightmapIndex =
                        registration.FirstGlobalSlot + assignment.BucketIndex;
                    assignment.Renderer.lightmapScaleOffset = assignment.RuntimeScaleOffset;
                }
            }
        }

        private static bool CurrentLayoutMatchesExpected(out string failure)
        {
            if (baseLightmaps == null)
            {
                failure = "The registry has no captured base lightmap layout.";
                return false;
            }

            if (LightmapSettings.lightmapsMode != LightmapsMode.CombinedDirectional)
            {
                failure = $"Lightmaps mode changed to {LightmapSettings.lightmapsMode}.";
                return false;
            }

            LightmapData[] current = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
            LightmapData[] expected = BuildExpectedLayout();
            if (current.Length != expected.Length)
            {
                failure = $"Lightmap slot count changed from {expected.Length} to {current.Length}.";
                return false;
            }

            for (int i = 0; i < current.Length; i++)
            {
                LightmapData left = current[i];
                LightmapData right = expected[i];
                if (left == null || right == null ||
                    left.lightmapColor != right.lightmapColor ||
                    left.lightmapDir != right.lightmapDir ||
                    left.shadowMask != right.shadowMask)
                {
                    failure = $"Lightmap slot {i} no longer matches the managed texture layout.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private static LightmapData[] CloneArray(LightmapData[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<LightmapData>();
            var clone = new LightmapData[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private static void ResetEmptyCaptureIfNeeded()
        {
            if (Registrations.Count != 0)
                return;
            baseLightmaps = null;
            baseMode = default;
        }

        private static bool IsFinite(Vector4 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                   IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
