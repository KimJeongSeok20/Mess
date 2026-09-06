using System;
using UnityEngine;

namespace DungeonAdjacentLightingPoC
{
    [CreateAssetMenu(
        fileName = "DungeonAdjacentPairLightmapData",
        menuName = "Dungeon/Lighting/Adjacent Pair Lightmap Data")]
    public sealed class DungeonAdjacentPairLightmapData : ScriptableObject
    {
        public enum RoomRole
        {
            Start,
            Administrative
        }

        public enum PairPowerState
        {
            P0P0,
            P100P0,
            P0P100,
            P100P100
        }

        [Serializable]
        public struct LightmapState
        {
            public Texture2D lightmapColor;
            public Texture2D lightmapDirection;
            public Vector4 lightmapScaleOffset;

            public bool IsValid => lightmapColor != null;
        }

        [Serializable]
        public struct RendererStateSet
        {
            public RoomRole receiverRoom;
            public string relativePath;
            public int rendererBucketIndex;
            public int vertexCount;
            public LightmapState p0p0;
            public LightmapState p100p0;
            public LightmapState p0p100;
            public LightmapState p100p100;

            public bool IsComplete =>
                p0p0.IsValid && p100p0.IsValid && p0p100.IsValid && p100p100.IsValid;

            public LightmapState GetState(PairPowerState state)
            {
                switch (state)
                {
                    case PairPowerState.P100P0:
                        return p100p0;
                    case PairPowerState.P0P100:
                        return p0p100;
                    case PairPowerState.P100P100:
                        return p100p100;
                    default:
                        return p0p0;
                }
            }
        }

        [SerializeField] private string pairId;
        [SerializeField] private RendererStateSet[] rendererStates = Array.Empty<RendererStateSet>();

        public string PairId => pairId;
        public RendererStateSet[] RendererStates => rendererStates;
        public int CompleteRendererCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < rendererStates.Length; i++)
                {
                    if (rendererStates[i].IsComplete)
                        count++;
                }
                return count;
            }
        }

        public void ConfigureAuthoring(string stablePairId, RendererStateSet[] states)
        {
            pairId = stablePairId;
            rendererStates = states ?? Array.Empty<RendererStateSet>();
        }

        public bool TryGetRendererStates(
            RoomRole receiverRoom,
            string relativePath,
            int rendererBucketIndex,
            out RendererStateSet states)
        {
            for (int i = 0; i < rendererStates.Length; i++)
            {
                RendererStateSet candidate = rendererStates[i];
                if (candidate.receiverRoom != receiverRoom ||
                    candidate.rendererBucketIndex != rendererBucketIndex ||
                    !string.Equals(candidate.relativePath, relativePath, StringComparison.Ordinal))
                {
                    continue;
                }

                states = candidate;
                return true;
            }

            states = default;
            return false;
        }

        public bool TryResolveTransfer(
            RoomRole receiverRoom,
            string relativePath,
            int rendererBucketIndex,
            DungeonTileLightmapSwitcher.PowerLevel receiverPower,
            DungeonTileLightmapSwitcher.PowerLevel sourcePower,
            out LightmapState current,
            out LightmapState baseline)
        {
            current = default;
            baseline = default;
            if (!TryGetRendererStates(receiverRoom, relativePath, rendererBucketIndex, out RendererStateSet states))
                return false;

            ResolveTransferStateKeys(
                receiverRoom,
                receiverPower,
                sourcePower,
                out PairPowerState currentKey,
                out PairPowerState baselineKey);
            current = states.GetState(currentKey);
            baseline = states.GetState(baselineKey);
            return current.IsValid && baseline.IsValid;
        }

        public static void ResolveTransferStateKeys(
            RoomRole receiverRoom,
            DungeonTileLightmapSwitcher.PowerLevel receiverPower,
            DungeonTileLightmapSwitcher.PowerLevel sourcePower,
            out PairPowerState current,
            out PairPowerState baseline)
        {
            DungeonAdjacentLightmapProjection.ResolvePairStateKeys(
                receiverRoom == RoomRole.Start,
                receiverPower == DungeonTileLightmapSwitcher.PowerLevel.P100,
                sourcePower == DungeonTileLightmapSwitcher.PowerLevel.P100,
                out int currentIndex,
                out int baselineIndex);
            current = (PairPowerState)currentIndex;
            baseline = (PairPowerState)baselineIndex;
        }

        public static PairPowerState Compose(
            DungeonTileLightmapSwitcher.PowerLevel startPower,
            DungeonTileLightmapSwitcher.PowerLevel administrativePower)
        {
            bool startOn = startPower == DungeonTileLightmapSwitcher.PowerLevel.P100;
            bool administrativeOn = administrativePower == DungeonTileLightmapSwitcher.PowerLevel.P100;
            if (startOn && administrativeOn)
                return PairPowerState.P100P100;
            if (startOn)
                return PairPowerState.P100P0;
            return administrativeOn ? PairPowerState.P0P100 : PairPowerState.P0P0;
        }
    }
}
