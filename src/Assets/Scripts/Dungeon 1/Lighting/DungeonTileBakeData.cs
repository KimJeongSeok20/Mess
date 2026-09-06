using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(fileName = "DungeonTileBakeData", menuName = "Dungeon/Lighting/Tile Bake Data")]
public sealed class DungeonTileBakeData : ScriptableObject
{
    [Serializable]
    public struct RendererBakeEntry
    {
        public string relativePath;
        public int lightmapIndex;
        public Vector4 lightmapScaleOffset;
    }

    [Serializable]
    public struct ReflectionProbeBakeEntry
    {
        public string relativePath;
        public Cubemap bakedTexture;
    }

    [Serializable]
    public struct LightProbeBakeEntry
    {
        public Vector3 localPosition;
        public Vector3 coefficient0;
        public Vector3 coefficient1;
        public Vector3 coefficient2;
        public Vector3 coefficient3;
        public Vector3 coefficient4;
        public Vector3 coefficient5;
        public Vector3 coefficient6;
        public Vector3 coefficient7;
        public Vector3 coefficient8;
        public Vector4 occlusion;

        public static LightProbeBakeEntry FromProbe(Vector3 localPosition, SphericalHarmonicsL2 probe, Vector4 occlusion)
        {
            return new LightProbeBakeEntry
            {
                localPosition = localPosition,
                coefficient0 = GetCoefficient(probe, 0),
                coefficient1 = GetCoefficient(probe, 1),
                coefficient2 = GetCoefficient(probe, 2),
                coefficient3 = GetCoefficient(probe, 3),
                coefficient4 = GetCoefficient(probe, 4),
                coefficient5 = GetCoefficient(probe, 5),
                coefficient6 = GetCoefficient(probe, 6),
                coefficient7 = GetCoefficient(probe, 7),
                coefficient8 = GetCoefficient(probe, 8),
                occlusion = occlusion
            };
        }

        public SphericalHarmonicsL2 ToSphericalHarmonics()
        {
            var probe = new SphericalHarmonicsL2();
            SetCoefficient(ref probe, 0, coefficient0);
            SetCoefficient(ref probe, 1, coefficient1);
            SetCoefficient(ref probe, 2, coefficient2);
            SetCoefficient(ref probe, 3, coefficient3);
            SetCoefficient(ref probe, 4, coefficient4);
            SetCoefficient(ref probe, 5, coefficient5);
            SetCoefficient(ref probe, 6, coefficient6);
            SetCoefficient(ref probe, 7, coefficient7);
            SetCoefficient(ref probe, 8, coefficient8);
            return probe;
        }

        public Vector3 GetCoefficient(int index)
        {
            switch (index)
            {
                case 0: return coefficient0;
                case 1: return coefficient1;
                case 2: return coefficient2;
                case 3: return coefficient3;
                case 4: return coefficient4;
                case 5: return coefficient5;
                case 6: return coefficient6;
                case 7: return coefficient7;
                case 8: return coefficient8;
                default: throw new ArgumentOutOfRangeException(nameof(index), index, "SH coefficient index must be 0..8.");
            }
        }

        private static Vector3 GetCoefficient(SphericalHarmonicsL2 probe, int coefficientIndex)
        {
            return new Vector3(probe[0, coefficientIndex], probe[1, coefficientIndex], probe[2, coefficientIndex]);
        }

        private static void SetCoefficient(ref SphericalHarmonicsL2 probe, int coefficientIndex, Vector3 value)
        {
            probe[0, coefficientIndex] = value.x;
            probe[1, coefficientIndex] = value.y;
            probe[2, coefficientIndex] = value.z;
        }
    }

    public LightmapsMode lightmapsMode = LightmapsMode.NonDirectional;
    public Texture2D[] lightmapColors = Array.Empty<Texture2D>();
    public Texture2D[] lightmapDirections = Array.Empty<Texture2D>();
    public RendererBakeEntry[] rendererEntries = Array.Empty<RendererBakeEntry>();
    public ReflectionProbeBakeEntry[] reflectionProbeEntries = Array.Empty<ReflectionProbeBakeEntry>();
    public LightProbeBakeEntry[] lightProbeEntries = Array.Empty<LightProbeBakeEntry>();
}
