using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DungeonPortalTransportPoC.KExactBasisV1
{
    internal static class KExactRenderingLayerRegistry
    {
        // Rendering-layer bits 0 and 1 remain reserved for the production/default
        // surface contract. This project defines only bits 0..7; URP removes
        // undefined bits before light upload, so portal routing is limited to 2..7.
        public const uint AllowedPortalLayerBits = 0xFCu;

        private sealed class Reservation
        {
            public string ConnectionKey;
            public string DirectedKeyA;
            public string DirectedKeyB;
            public string LayerGroupA;
            public string LayerGroupB;
            public uint ReceiverBitA;
            public uint ReceiverBitB;
            public uint CasterBit;
            public uint DoorReceiverBit;
        }

        private static readonly Dictionary<KExactPortalConnection, Reservation> ByOwner =
            new Dictionary<KExactPortalConnection, Reservation>();
        private static readonly Dictionary<string, KExactPortalConnection> UniqueKeys =
            new Dictionary<string, KExactPortalConnection>(StringComparer.Ordinal);
        private static readonly Dictionary<uint, string> ReceiverGroupByBit =
            new Dictionary<uint, string>();
        private static readonly Dictionary<uint, int> ReceiverBitRefCounts =
            new Dictionary<uint, int>();
        private static readonly Dictionary<uint, int> CasterBitRefCounts =
            new Dictionary<uint, int>();
        private static readonly Dictionary<uint, int> DoorReceiverBitRefCounts =
            new Dictionary<uint, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ByOwner.Clear();
            UniqueKeys.Clear();
            ReceiverGroupByBit.Clear();
            ReceiverBitRefCounts.Clear();
            CasterBitRefCounts.Clear();
            DoorReceiverBitRefCounts.Clear();
        }

        public static bool IsSingleAllowedBit(uint bit)
        {
            uint definedBits = RenderingLayerMask.GetDefinedRenderingLayersCombinedMaskValue();
            return bit != 0u && (bit & (bit - 1u)) == 0u &&
                   (bit & AllowedPortalLayerBits) == bit &&
                   (bit & definedBits) == bit;
        }

        public static bool TryReserve(
            KExactPortalConnection owner,
            string connectionKey,
            KExactDirectedTransportBinding aToB,
            KExactDirectedTransportBinding bToA,
            uint casterBit,
            uint doorReceiverBit,
            out string failure)
        {
            if (owner == null || aToB == null || bToA == null)
            {
                failure = "Layer reservation owner or directed binding is missing.";
                return false;
            }

            if (ByOwner.ContainsKey(owner))
            {
                failure = "This connection already owns a rendering-layer reservation.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(connectionKey) ||
                string.IsNullOrWhiteSpace(aToB.DirectedKey) ||
                string.IsNullOrWhiteSpace(bToA.DirectedKey) ||
                string.IsNullOrWhiteSpace(aToB.LayerGroupKey) ||
                string.IsNullOrWhiteSpace(bToA.LayerGroupKey))
            {
                failure = "Connection, directed, and layer-group keys must be non-empty.";
                return false;
            }

            if (string.Equals(aToB.DirectedKey, bToA.DirectedKey, StringComparison.Ordinal) ||
                string.Equals(connectionKey, aToB.DirectedKey, StringComparison.Ordinal) ||
                string.Equals(connectionKey, bToA.DirectedKey, StringComparison.Ordinal))
            {
                failure = "Connection and directed keys must be unique.";
                return false;
            }

            uint bitA = aToB.ReceiverRenderingLayerBit;
            uint bitB = bToA.ReceiverRenderingLayerBit;
            if (!IsSingleAllowedBit(bitA) || !IsSingleAllowedBit(bitB) ||
                !IsSingleAllowedBit(casterBit) || !IsSingleAllowedBit(doorReceiverBit) ||
                bitA == bitB || bitA == casterBit || bitB == casterBit ||
                bitA == doorReceiverBit || bitB == doorReceiverBit ||
                casterBit == doorReceiverBit)
            {
                failure = "A->B, B->A, caster, and door receiver masks must be four distinct " +
                          "single bits " +
                          "within defined rendering-layer bits 2..7.";
                return false;
            }

            if (UniqueKeys.ContainsKey(connectionKey) ||
                UniqueKeys.ContainsKey(aToB.DirectedKey) ||
                UniqueKeys.ContainsKey(bToA.DirectedKey))
            {
                failure = "A duplicate active K-exact connection/directed key was detected.";
                return false;
            }

            if (!CanShareReceiverBit(bitA, aToB.LayerGroupKey, out failure) ||
                !CanShareReceiverBit(bitB, bToA.LayerGroupKey, out failure))
            {
                return false;
            }

            if (!TryValidateDedicatedBitSeparation(
                    bitA,
                    bitB,
                    casterBit,
                    doorReceiverBit,
                    out failure))
            {
                return false;
            }

            if (CasterBitRefCounts.Count > 0 && !CasterBitRefCounts.ContainsKey(casterBit))
            {
                failure = "All active K-exact connections must share the same dedicated caster bit.";
                return false;
            }

            if (DoorReceiverBitRefCounts.Count > 0 &&
                !DoorReceiverBitRefCounts.ContainsKey(doorReceiverBit))
            {
                failure = "All active K-exact connections must share the same dedicated door " +
                          "receiver bit.";
                return false;
            }

            uint newlyIntroducedBits = 0u;
            if (!ReceiverBitRefCounts.ContainsKey(bitA))
                newlyIntroducedBits |= bitA;
            if (!ReceiverBitRefCounts.ContainsKey(bitB))
                newlyIntroducedBits |= bitB;
            if (!CasterBitRefCounts.ContainsKey(casterBit))
                newlyIntroducedBits |= casterBit;
            if (!DoorReceiverBitRefCounts.ContainsKey(doorReceiverBit))
                newlyIntroducedBits |= doorReceiverBit;

            if (!TryValidateSceneBitAvailability(newlyIntroducedBits, out failure))
                return false;

            var reservation = new Reservation
            {
                ConnectionKey = connectionKey,
                DirectedKeyA = aToB.DirectedKey,
                DirectedKeyB = bToA.DirectedKey,
                LayerGroupA = aToB.LayerGroupKey,
                LayerGroupB = bToA.LayerGroupKey,
                ReceiverBitA = bitA,
                ReceiverBitB = bitB,
                CasterBit = casterBit,
                DoorReceiverBit = doorReceiverBit
            };

            ByOwner.Add(owner, reservation);
            UniqueKeys.Add(connectionKey, owner);
            UniqueKeys.Add(aToB.DirectedKey, owner);
            UniqueKeys.Add(bToA.DirectedKey, owner);
            AddReceiverBit(bitA, aToB.LayerGroupKey);
            AddReceiverBit(bitB, bToA.LayerGroupKey);
            AddRefCount(CasterBitRefCounts, casterBit);
            AddRefCount(DoorReceiverBitRefCounts, doorReceiverBit);
            failure = null;
            return true;
        }

        public static bool IsReservedBy(KExactPortalConnection owner)
        {
            return owner != null && ByOwner.ContainsKey(owner);
        }

        public static bool TryValidateOwner(
            KExactPortalConnection owner,
            out string failure)
        {
            if (owner == null || !ByOwner.TryGetValue(owner, out Reservation reservation))
            {
                failure = "The active connection lost its rendering-layer reservation.";
                return false;
            }

            if (!UniqueKeys.TryGetValue(reservation.ConnectionKey, out KExactPortalConnection c) ||
                c != owner ||
                !UniqueKeys.TryGetValue(reservation.DirectedKeyA, out KExactPortalConnection a) ||
                a != owner ||
                !UniqueKeys.TryGetValue(reservation.DirectedKeyB, out KExactPortalConnection b) ||
                b != owner ||
                !ReceiverGroupByBit.TryGetValue(
                    reservation.ReceiverBitA,
                    out string groupA) ||
                !string.Equals(groupA, reservation.LayerGroupA, StringComparison.Ordinal) ||
                !ReceiverGroupByBit.TryGetValue(
                    reservation.ReceiverBitB,
                    out string groupB) ||
                !string.Equals(groupB, reservation.LayerGroupB, StringComparison.Ordinal) ||
                !ReceiverBitRefCounts.ContainsKey(reservation.ReceiverBitA) ||
                !ReceiverBitRefCounts.ContainsKey(reservation.ReceiverBitB) ||
                !CasterBitRefCounts.ContainsKey(reservation.CasterBit) ||
                !DoorReceiverBitRefCounts.ContainsKey(reservation.DoorReceiverBit))
            {
                failure = "The active rendering-layer registry allocation drifted.";
                return false;
            }

            return TryValidateSceneBitAvailability(
                reservation.ReceiverBitA |
                reservation.ReceiverBitB |
                reservation.CasterBit |
                reservation.DoorReceiverBit,
                out failure);
        }

        public static void Release(KExactPortalConnection owner)
        {
            if (owner == null || !ByOwner.TryGetValue(owner, out Reservation reservation))
                return;

            ByOwner.Remove(owner);
            UniqueKeys.Remove(reservation.ConnectionKey);
            UniqueKeys.Remove(reservation.DirectedKeyA);
            UniqueKeys.Remove(reservation.DirectedKeyB);
            RemoveReceiverBit(reservation.ReceiverBitA);
            RemoveReceiverBit(reservation.ReceiverBitB);
            RemoveRefCount(CasterBitRefCounts, reservation.CasterBit);
            RemoveRefCount(DoorReceiverBitRefCounts, reservation.DoorReceiverBit);
        }

        private static bool CanShareReceiverBit(
            uint bit,
            string groupKey,
            out string failure)
        {
            if (ReceiverGroupByBit.TryGetValue(bit, out string existingGroup) &&
                !string.Equals(existingGroup, groupKey, StringComparison.Ordinal))
            {
                failure = $"Rendering-layer bit 0x{bit:X} is already reserved for graph-color " +
                          $"group '{existingGroup}', not '{groupKey}'.";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryValidateDedicatedBitSeparation(
            uint receiverBitA,
            uint receiverBitB,
            uint casterBit,
            uint doorReceiverBit,
            out string failure)
        {
            if (CasterBitRefCounts.ContainsKey(receiverBitA) ||
                CasterBitRefCounts.ContainsKey(receiverBitB) ||
                DoorReceiverBitRefCounts.ContainsKey(receiverBitA) ||
                DoorReceiverBitRefCounts.ContainsKey(receiverBitB))
            {
                failure = "A directed room receiver bit collides with an active dedicated " +
                          "caster or door receiver bit.";
                return false;
            }

            if (ReceiverBitRefCounts.ContainsKey(casterBit) ||
                ReceiverBitRefCounts.ContainsKey(doorReceiverBit))
            {
                failure = "A dedicated caster or door receiver bit collides with an active " +
                          "directed room receiver bit.";
                return false;
            }

            if (CasterBitRefCounts.ContainsKey(doorReceiverBit) ||
                DoorReceiverBitRefCounts.ContainsKey(casterBit))
            {
                failure = "Dedicated caster and door receiver bit roles cannot overlap " +
                          "across active K-exact connections.";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryValidateSceneBitAvailability(uint bits, out string failure)
        {
            if (bits == 0u)
            {
                failure = null;
                return true;
            }

            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(
                FindObjectsInactive.Include);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null || light.GetComponent<KExactRuntimeLightMarker>() != null)
                    continue;

                UniversalAdditionalLightData additional =
                    light.GetComponent<UniversalAdditionalLightData>();
                uint lightingLayers = additional != null
                    ? additional.renderingLayers.value
                    : unchecked((uint)light.renderingLayerMask);
                uint shadowLayers = additional != null && additional.customShadowLayers
                    ? additional.shadowRenderingLayers.value
                    : lightingLayers;
                if (((lightingLayers | shadowLayers) & bits) == 0u)
                    continue;

                failure = $"Rendering-layer collision: existing light '{GetPath(light.transform)}' " +
                          $"uses reserved candidate bits 0x{bits:X}.";
                return false;
            }

            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || KExactRendererLayerLeaseTable.IsLeased(renderer) ||
                    (renderer.renderingLayerMask & bits) == 0u)
                {
                    continue;
                }

                failure = $"Rendering-layer collision: existing renderer " +
                          $"'{GetPath(renderer.transform)}' already uses candidate bits 0x{bits:X}.";
                return false;
            }

            failure = null;
            return true;
        }

        private static void AddReceiverBit(uint bit, string group)
        {
            ReceiverGroupByBit[bit] = group;
            AddRefCount(ReceiverBitRefCounts, bit);
        }

        private static void RemoveReceiverBit(uint bit)
        {
            RemoveRefCount(ReceiverBitRefCounts, bit);
            if (!ReceiverBitRefCounts.ContainsKey(bit))
                ReceiverGroupByBit.Remove(bit);
        }

        private static void AddRefCount(Dictionary<uint, int> counts, uint bit)
        {
            counts.TryGetValue(bit, out int count);
            counts[bit] = count + 1;
        }

        private static void RemoveRefCount(Dictionary<uint, int> counts, uint bit)
        {
            if (!counts.TryGetValue(bit, out int count))
                return;
            if (count <= 1)
                counts.Remove(bit);
            else
                counts[bit] = count - 1;
        }

        private static string GetPath(Transform target)
        {
            if (target == null)
                return "<missing>";

            string path = target.name;
            for (Transform current = target.parent; current != null; current = current.parent)
                path = current.name + "/" + path;
            return path;
        }
    }
}
