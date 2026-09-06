using System;
using UnityEngine;

[CreateAssetMenu(fileName = "DungeonTileRotationLightingSetV2", menuName = "Dungeon/Lighting/Rotation Lighting Set V2")]
public sealed class DungeonTileRotationLightingSetV2 : ScriptableObject
{
    [Serializable]
    public sealed class RotationVariant
    {
        [Range(0, 270)] public int rotationY;
        public DungeonTileBakeData power100;
        public DungeonTileBakeData power0;
    }

    [SerializeField] private string sourceTileName;
    [SerializeField] private string sourcePrefabAssetPath;
    [SerializeField] private string canonicalPrefabAssetPath;
    [SerializeField] private string sourceDependencyHash;
    [SerializeField] private string generatedAtUtc;
    [SerializeField] private RotationVariant[] variants = Array.Empty<RotationVariant>();

    public string SourceTileName => sourceTileName;
    public string SourcePrefabAssetPath => sourcePrefabAssetPath;
    public string CanonicalPrefabAssetPath => canonicalPrefabAssetPath;
    public string SourceDependencyHash => sourceDependencyHash;
    public string GeneratedAtUtc => generatedAtUtc;
    public RotationVariant[] Variants => variants ?? Array.Empty<RotationVariant>();

    public void Configure(string tileName, RotationVariant[] rotationVariants)
    {
        sourceTileName = tileName ?? string.Empty;
        variants = rotationVariants ?? Array.Empty<RotationVariant>();
    }

    public void Configure(
        string tileName,
        RotationVariant[] rotationVariants,
        string sourcePath,
        string canonicalPath,
        string dependencyHash,
        string generatedUtc)
    {
        Configure(tileName, rotationVariants);
        sourcePrefabAssetPath = sourcePath ?? string.Empty;
        canonicalPrefabAssetPath = canonicalPath ?? string.Empty;
        sourceDependencyHash = dependencyHash ?? string.Empty;
        generatedAtUtc = generatedUtc ?? string.Empty;
    }

    public RotationVariant Resolve(float worldYaw)
    {
        RotationVariant[] available = Variants;
        if (available.Length == 0)
            return null;

        int quantized = QuantizeRotation(worldYaw);
        for (int i = 0; i < available.Length; i++)
        {
            RotationVariant variant = available[i];
            if (variant != null && QuantizeRotation(variant.rotationY) == quantized)
                return variant;
        }

        return available[0];
    }

    public static int QuantizeRotation(float yaw)
    {
        int quarterTurns = Mathf.RoundToInt(yaw / 90f);
        return ((quarterTurns % 4) + 4) % 4 * 90;
    }
}
