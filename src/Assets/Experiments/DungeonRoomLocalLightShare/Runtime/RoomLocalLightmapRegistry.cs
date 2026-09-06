using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    /// <summary>
    /// Adapted from DungeonPortalBakedBasisLightmapRegistry.
    /// Appends private working lightmap slots and remaps only leased renderers.
    /// </summary>
    public static class RoomLocalLightmapRegistry
    {
        internal readonly struct AssignmentRequest
        {
            public readonly Renderer Renderer;
            public readonly int BucketIndex;
            public readonly Vector4 RuntimeScaleOffset;

            public AssignmentRequest(Renderer renderer, int bucketIndex, Vector4 runtimeScaleOffset)
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
                    failure = "Compositor '" + owner.name + "' is already registered.";
                    return false;
                }
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
                    failure = "Renderer assignment " + i +
                              " is missing, duplicated, or already owned.";
                    ResetEmptyCaptureIfNeeded();
                    return false;
                }

                if (request.BucketIndex < 0 || request.BucketIndex >= runtimeSlots.Length ||
                    renderer.lightmapIndex < 0 ||
                    renderer.lightmapIndex >= currentBase.Length)
                {
                    failure = "Renderer '" + renderer.name +
                              "' has an invalid bucket or original lightmap index.";
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
                    failure = "Runtime lightmap slot " + i + " is missing color or direction.";
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
                failure = "Could not install runtime lightmap slots: " + exception.Message;
                ResetEmptyCaptureIfNeeded();
                return false;
            }
        }

        internal static bool TryUnregister(
            Registration registration,
            bool restoreRenderers,
            out string failure)
        {
            if (registration == null || !Registrations.Contains(registration))
            {
                failure = null;
                return true;
            }

            Registrations.Remove(registration);
            for (int i = 0; i < registration.Assignments.Length; i++)
                RendererOwners.Remove(registration.Assignments[i].Renderer);

            try
            {
                if (Registrations.Count == 0)
                {
                    LightmapSettings.lightmapsMode = baseMode;
                    LightmapSettings.lightmaps = CloneArray(baseLightmaps);
                    baseLightmaps = null;
                    baseMode = default;
                }
                else
                {
                    ApplyManagedLayout();
                }

                if (restoreRenderers)
                {
                    for (int i = registration.Originals.Length - 1; i >= 0; i--)
                        registration.Originals[i].Restore();
                }
            }
            catch (Exception exception)
            {
                if (restoreRenderers)
                {
                    for (int i = registration.Originals.Length - 1; i >= 0; i--)
                        registration.Originals[i].Restore();
                }

                failure = "Registry teardown threw " + exception.Message;
                return false;
            }

            failure = null;
            return true;
        }

        private static void ApplyManagedLayout()
        {
            LightmapData[] expected = BuildExpectedLayout();
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

            for (int i = 0; i < Registrations.Count; i++)
            {
                Registration registration = Registrations[i];
                registration.FirstGlobalSlot = cursor;
                Array.Copy(registration.Slots, 0, result, cursor, registration.Slots.Length);
                cursor += registration.Slots.Length;
            }

            return result;
        }

        private static void ApplySurvivingRendererIndices()
        {
            for (int i = 0; i < Registrations.Count; i++)
            {
                Registration registration = Registrations[i];
                for (int j = 0; j < registration.Assignments.Length; j++)
                {
                    AssignmentRequest assignment = registration.Assignments[j];
                    if (assignment.Renderer == null)
                        throw new MissingReferenceException(
                            "A leased renderer was destroyed while bounce composition was active.");
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

            LightmapData[] current = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
            LightmapData[] expected = BuildExpectedLayout();
            if (current.Length != expected.Length)
            {
                failure = "Lightmap slot count changed from " + expected.Length +
                          " to " + current.Length + ".";
                return false;
            }

            for (int i = 0; i < current.Length; i++)
            {
                LightmapData left = current[i];
                LightmapData right = expected[i];
                if (left == null || right == null ||
                    left.lightmapColor != right.lightmapColor ||
                    left.lightmapDir != right.lightmapDir)
                {
                    failure = "Lightmap slot " + i + " drifted from the registry layout.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private static void ResetEmptyCaptureIfNeeded()
        {
            if (Registrations.Count == 0)
            {
                baseLightmaps = null;
                baseMode = default;
            }
        }

        private static LightmapData[] CloneArray(LightmapData[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<LightmapData>();
            var copy = new LightmapData[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}
