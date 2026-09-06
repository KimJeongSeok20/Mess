using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DungeonTilePowerBakeSet : MonoBehaviour
{
    [Serializable]
    public struct EmissionMaterialEntry
    {
        public string relativePath;
        public int rendererBucketIndex;
        public int materialIndex;
        public Material power100Material;
        public Material power00Material;
    }

    [SerializeField] private DungeonTileBakeData power100Bake;
    [SerializeField] private DungeonTileBakeData power00Bake;

    [SerializeField, Min(0f)] private float power100LightIntensityScale = 1f;
    [SerializeField, Min(0f)] private float power00LightIntensityScale = 0f;

    [SerializeField] private EmissionMaterialEntry[] emissionMaterialEntries = Array.Empty<EmissionMaterialEntry>();

    public DungeonTileBakeData Power100Bake => power100Bake;
    public DungeonTileBakeData Power00Bake => power00Bake;

    public float Power100LightIntensityScale => power100LightIntensityScale;
    public float Power00LightIntensityScale => power00LightIntensityScale;

    public EmissionMaterialEntry[] EmissionMaterialEntries => emissionMaterialEntries ?? Array.Empty<EmissionMaterialEntry>();

    public void ConfigureRuntimeBakeData(DungeonTileBakeData power100, DungeonTileBakeData power0)
    {
        power100Bake = power100;
        power00Bake = power0;
    }
}
