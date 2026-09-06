using System.Collections.Generic;
using UnityEngine;

namespace DungeonPortalTransportPoC.KExactBasisV1
{
    internal static class KExactRendererLayerLeaseTable
    {
        private sealed class LeaseRecord
        {
            public uint OriginalMask;
            public readonly Dictionary<KExactPortalConnection, uint> BitsByOwner =
                new Dictionary<KExactPortalConnection, uint>();

            public uint ExpectedMask
            {
                get
                {
                    uint result = OriginalMask;
                    foreach (KeyValuePair<KExactPortalConnection, uint> pair in BitsByOwner)
                        result |= pair.Value;
                    return result;
                }
            }
        }

        private static readonly Dictionary<Renderer, LeaseRecord> Records =
            new Dictionary<Renderer, LeaseRecord>();

        public static int ActiveRendererCount => Records.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Records.Clear();
        }

        public static bool IsLeased(Renderer renderer)
        {
            return renderer != null && Records.ContainsKey(renderer);
        }

        public static bool TryAcquire(
            KExactPortalConnection owner,
            Dictionary<Renderer, uint> requestedBits,
            out string failure)
        {
            if (owner == null || requestedBits == null || requestedBits.Count == 0)
            {
                failure = "Renderer layer lease owner or request is empty.";
                return false;
            }

            foreach (KeyValuePair<Renderer, uint> request in requestedBits)
            {
                Renderer renderer = request.Key;
                if (renderer == null || request.Value == 0u)
                {
                    failure = "Renderer layer lease contains a missing renderer or zero bit mask.";
                    return false;
                }

                if (!Records.TryGetValue(renderer, out LeaseRecord existing))
                    continue;

                if (existing.BitsByOwner.ContainsKey(owner))
                {
                    failure = $"Renderer '{renderer.name}' is already leased by this connection.";
                    return false;
                }

                if (renderer.renderingLayerMask != existing.ExpectedMask)
                {
                    failure = $"Renderer '{renderer.name}' renderingLayerMask drifted before " +
                              "an additional lease.";
                    return false;
                }
            }

            var acquired = new List<Renderer>(requestedBits.Count);
            try
            {
                foreach (KeyValuePair<Renderer, uint> request in requestedBits)
                {
                    Renderer renderer = request.Key;
                    var immediateParity = new KExactRendererParitySnapshot(renderer);
                    if (!Records.TryGetValue(renderer, out LeaseRecord record))
                    {
                        record = new LeaseRecord
                        {
                            OriginalMask = renderer.renderingLayerMask
                        };
                        Records.Add(renderer, record);
                    }

                    record.BitsByOwner.Add(owner, request.Value);
                    acquired.Add(renderer);
                    renderer.renderingLayerMask = record.ExpectedMask;
                    bool maskMismatch = renderer.renderingLayerMask != record.ExpectedMask;
                    bool parityMatches = immediateParity.MatchesExceptRenderingLayer(
                        out string parityFailure);
                    if (maskMismatch || !parityMatches)
                    {
                        throw new LayerLeaseException(
                            $"Renderer '{renderer.name}' did not preserve parity after layer OR: " +
                            (maskMismatch
                                ? $"renderingLayerMask expected 0x{record.ExpectedMask:X8}, " +
                                  $"got 0x{renderer.renderingLayerMask:X8}."
                                : parityFailure));
                    }

                }

                failure = null;
                return true;
            }
            catch (System.Exception exception)
            {
                for (int i = acquired.Count - 1; i >= 0; i--)
                    ReleaseSingle(owner, acquired[i], out _);

                failure = exception.Message;
                return false;
            }
        }

        public static bool TryValidateOwner(
            KExactPortalConnection owner,
            out string failure)
        {
            foreach (KeyValuePair<Renderer, LeaseRecord> pair in Records)
            {
                LeaseRecord record = pair.Value;
                if (!record.BitsByOwner.ContainsKey(owner))
                    continue;

                Renderer renderer = pair.Key;
                if (renderer == null)
                {
                    failure = "An active renderer layer lease target was destroyed.";
                    return false;
                }

                if (renderer.renderingLayerMask != record.ExpectedMask)
                {
                    failure = $"Active renderingLayerMask lease drifted on '{renderer.name}'.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        public static bool ReleaseOwner(
            KExactPortalConnection owner,
            out string failure)
        {
            var owned = new List<Renderer>();
            foreach (KeyValuePair<Renderer, LeaseRecord> pair in Records)
            {
                if (pair.Value.BitsByOwner.ContainsKey(owner))
                    owned.Add(pair.Key);
            }

            bool success = true;
            failure = null;
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                if (ReleaseSingle(owner, owned[i], out string currentFailure))
                    continue;

                success = false;
                failure ??= currentFailure;
            }

            return success;
        }

        private static bool ReleaseSingle(
            KExactPortalConnection owner,
            Renderer renderer,
            out string failure)
        {
            if (ReferenceEquals(renderer, null) ||
                !Records.TryGetValue(renderer, out LeaseRecord record) ||
                !record.BitsByOwner.Remove(owner))
            {
                failure = null;
                return true;
            }

            if (renderer == null)
            {
                if (record.BitsByOwner.Count == 0)
                    Records.Remove(renderer);
                failure = "A destroyed renderer prevented exact renderingLayerMask restore.";
                return false;
            }

            uint restoreMask = record.ExpectedMask;
            renderer.renderingLayerMask = restoreMask;

            if (record.BitsByOwner.Count == 0)
            {
                restoreMask = record.OriginalMask;
                renderer.renderingLayerMask = restoreMask;
                Records.Remove(renderer);
            }

            if (renderer.renderingLayerMask != restoreMask)
            {
                failure = $"Could not restore exact renderingLayerMask on '{renderer.name}'.";
                return false;
            }

            failure = null;
            return true;
        }

        private sealed class LayerLeaseException : System.Exception
        {
            public LayerLeaseException(string message) : base(message)
            {
            }
        }
    }
}
