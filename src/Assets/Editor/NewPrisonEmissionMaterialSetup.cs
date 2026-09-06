using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class NewPrisonEmissionMaterialSetup
{
    private const string OutputFolder = "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL";
    private const string NewPrisonPowerMaterialFolder = "Assets/Prefabs/map_piece/NewPrison/Power_Material";
    private const string KriptoFxMaterialFolder = "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials";

    private static readonly Color P100Emission = new Color(4f, 3.792f, 3.792f, 1f);

    private static readonly EmissionSource[] EmissionSources =
    {
        new EmissionSource(
            "KriptoFX Source Lamps 01",
            $"{KriptoFxMaterialFolder}/Lamps_01.mat",
            $"{OutputFolder}/Lamps_01_P100.mat",
            $"{OutputFolder}/Lamps_01_P0_Black.mat",
            true
        ),
        new EmissionSource(
            "NewPrison Lamps 02",
            $"{NewPrisonPowerMaterialFolder}/Lamps_02.mat",
            $"{OutputFolder}/Lamps_02_P100.mat",
            $"{OutputFolder}/Lamps_02_P0_Black.mat",
            true
        ),
        new EmissionSource(
            "NewPrison Lamps 05",
            $"{NewPrisonPowerMaterialFolder}/Lamps_05.mat",
            $"{OutputFolder}/Lamps_05_P100.mat",
            $"{OutputFolder}/Lamps_05_P0_Black.mat",
            true
        ),
        new EmissionSource(
            "KriptoFX Source Lamps 05",
            $"{KriptoFxMaterialFolder}/Lamps_05.mat",
            $"{OutputFolder}/Lamps_05_P100.mat",
            $"{OutputFolder}/Lamps_05_P0_Black.mat",
            false
        )
    };

    private static readonly string[] RoomPrefabs =
    {
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/StartRoom.prefab",
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/AdminstrativeSegregation.prefab"
    };

    public static string SetupNewPrisonEmissionMaterials()
    {
        EnsureAssetFolder(OutputFolder);

        var report = new StringBuilder();
        report.AppendLine("[NewPrisonEmissionMaterialSetup] Updated emission material variants:");

        for (int i = 0; i < EmissionSources.Length; i++)
        {
            var source = EmissionSources[i];
            if (!source.createVariants)
                continue;

            var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(source.sourcePath);
            if (sourceMaterial == null)
            {
                report.AppendLine($"  {source.label}: missing source {source.sourcePath}");
                continue;
            }

            CreateOrUpdateVariant(sourceMaterial, source.power100Path, P100Emission, true, MaterialGlobalIlluminationFlags.BakedEmissive);
            CreateOrUpdateVariant(sourceMaterial, source.power0Path, Color.black, false, MaterialGlobalIlluminationFlags.EmissiveIsBlack);
            report.AppendLine($"  {source.label}: P100={source.power100Path}, P0={source.power0Path}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        report.Append(ReportRoomEmissionUsage());
        return report.ToString();
    }

    public static string ReportRoomEmissionUsage()
    {
        var trackedMaterials = LoadTrackedMaterials();
        var report = new StringBuilder();
        report.AppendLine("[NewPrisonEmissionMaterialSetup] Room emission material references:");

        for (int i = 0; i < RoomPrefabs.Length; i++)
        {
            string prefabPath = RoomPrefabs[i];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                report.AppendLine($"  {prefabPath}: missing");
                continue;
            }

            int rendererCount = 0;
            int trackedReferenceCount = 0;
            int untrackedEmissiveCount = 0;
            var untracked = new HashSet<string>();

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null)
                    continue;

                rendererCount++;
                var materials = renderer.sharedMaterials;
                for (int m = 0; m < materials.Length; m++)
                {
                    var material = materials[m];
                    if (material == null)
                        continue;

                    if (trackedMaterials.Contains(material))
                    {
                        trackedReferenceCount++;
                        continue;
                    }

                    if (IsEmissiveMaterial(material))
                    {
                        untrackedEmissiveCount++;
                        untracked.Add(AssetDatabase.GetAssetPath(material));
                    }
                }
            }

            report.AppendLine($"  {prefabPath}: renderers={rendererCount}, trackedLampRefs={trackedReferenceCount}, untrackedEmissiveRefs={untrackedEmissiveCount}");
            foreach (string path in untracked)
                report.AppendLine($"    untracked: {path}");
        }

        return report.ToString();
    }

    private static HashSet<Material> LoadTrackedMaterials()
    {
        var materials = new HashSet<Material>();
        for (int i = 0; i < EmissionSources.Length; i++)
        {
            AddMaterial(materials, EmissionSources[i].sourcePath);
            AddMaterial(materials, EmissionSources[i].power100Path);
            AddMaterial(materials, EmissionSources[i].power0Path);
        }

        return materials;
    }

    private static void AddMaterial(HashSet<Material> materials, string path)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
            materials.Add(material);
    }

    private static bool IsEmissiveMaterial(Material material)
    {
        if (material == null || !material.HasProperty("_EmissionColor"))
            return false;

        var color = material.GetColor("_EmissionColor");
        return material.IsKeywordEnabled("_EMISSION") || color.maxColorComponent > 0.0001f;
    }

    private static void CreateOrUpdateVariant(
        Material source,
        string assetPath,
        Color emissionColor,
        bool emissionEnabled,
        MaterialGlobalIlluminationFlags giFlags)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (material == null)
        {
            material = new Material(source);
            AssetDatabase.CreateAsset(material, assetPath);
        }
        else
        {
            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
        }

        material.name = Path.GetFileNameWithoutExtension(assetPath);
        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", emissionColor);

        if (emissionEnabled)
            material.EnableKeyword("_EMISSION");
        else
            material.DisableKeyword("_EMISSION");

        material.globalIlluminationFlags = giFlags;
        EditorUtility.SetDirty(material);
    }

    private static void EnsureAssetFolder(string folder)
    {
        string[] parts = folder.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
            return;

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }

    private readonly struct EmissionSource
    {
        public readonly string label;
        public readonly string sourcePath;
        public readonly string power100Path;
        public readonly string power0Path;
        public readonly bool createVariants;

        public EmissionSource(string label, string sourcePath, string power100Path, string power0Path, bool createVariants)
        {
            this.label = label;
            this.sourcePath = sourcePath;
            this.power100Path = power100Path;
            this.power0Path = power0Path;
            this.createVariants = createVariants;
        }
    }
}
