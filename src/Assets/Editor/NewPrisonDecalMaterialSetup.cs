using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class NewPrisonDecalMaterialSetup
{
    private const string NewPrisonRoot = "Assets/Prefabs/map_piece/NewPrison";
    private const string DecalMaterialFolder = NewPrisonRoot + "/DECAL_MATERIAL";
    private const string AlbedoShaderPath = DecalMaterialFolder + "/NewPrison_Decals_Albedo_CutoutLightmapped.shader";
    private const string CompleteShaderPath = DecalMaterialFolder + "/NewPrison_Decals_Complete_CutoutLightmapped.shader";
    private const string FloorAlbedoMaterialPath = DecalMaterialFolder + "/Decals_01_Albedo_FloorLightmapped.mat";
    private const string FloorYellowMaterialPath = DecalMaterialFolder + "/Decals_01_Yellow_FloorLightmapped.mat";
    private const int TransparentRenderQueue = 3000;
    private const float DefaultAlphaCutoff = 0.5f;

    private static readonly DecalMaterialMapping[] MaterialMappings =
    {
        new(
            "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials/Decals/Decals_01_Albedo.mat",
            DecalMaterialFolder + "/Decals_01_Albedo_CutoutLightmapped.mat",
            AlbedoShaderPath),
        new(
            "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials/Decals/Decals_01_Yellow.mat",
            DecalMaterialFolder + "/Decals_01_Yellow_CutoutLightmapped.mat",
            AlbedoShaderPath),
        new(
            "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials/Decals/Decals_02_Black.mat",
            DecalMaterialFolder + "/Decals_02_Black_CutoutLightmapped.mat",
            AlbedoShaderPath),
        new(
            "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials/Decals/Decal_ExposedConcrete_01.mat",
            DecalMaterialFolder + "/Decal_ExposedConcrete_01_CutoutLightmapped.mat",
            CompleteShaderPath)
    };

    private static readonly DecalMaterialMapping[] FloorMaterialMappings =
    {
        new(
            "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials/Decals/Decals_01_Albedo.mat",
            FloorAlbedoMaterialPath,
            AlbedoShaderPath),
        new(
            "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials/Decals/Decals_01_Yellow.mat",
            FloorYellowMaterialPath,
            AlbedoShaderPath)
    };

    [MenuItem("Tools/Dungeon/Setup NewPrison Decal Materials")]
    public static void SetupAndApplyAllDecalsMenu()
    {
        Debug.Log(SetupAndApplyAllDecals());
    }

    public static string SetupAndApplyAllDecals()
    {
        EnsureFolder(DecalMaterialFolder);
        AssetDatabase.Refresh();

        var report = new StringBuilder();
        var maps = BuildReplacementMaps(report);
        ApplyResult result = ApplyToNewPrisonPrefabs(maps);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        report.AppendLine(
            $"[NewPrisonDecalMaterialSetup] applied prefabs={result.prefabs}, changedPrefabs={result.changedPrefabs}, decalRenderers={result.decalRenderers}, floorDecals={result.floorDecals}, wallDecals={result.wallDecals}, materialSlots={result.materialSlots}, replacedSlots={result.replacedSlots}, floorGiEnabled={result.floorGiEnabled}, wallGiDisabled={result.wallGiDisabled}, shadowDisabled={result.shadowDisabled}, receiveShadowsDisabled={result.receiveShadowsDisabled}, probeReceiversAdded={result.probeReceiversAdded}, probeReceiversRemoved={result.probeReceiversRemoved}");
        return report.ToString();
    }

    public static string ReportAllDecals()
    {
        var report = new StringBuilder();
        int prefabs = 0;
        int prefabsWithDecals = 0;
        int decalRenderers = 0;
        int floorDecalRenderers = 0;
        int wallDecalRenderers = 0;
        int materialSlots = 0;
        int newPrisonSlots = 0;
        int oldKriptoFxSlots = 0;
        int contributeGi = 0;
        int floorMissingContributeGi = 0;
        int wallContributeGi = 0;
        int shadowCasters = 0;
        int receiveShadowsEnabled = 0;
        int wallMissingProbeReceivers = 0;
        int floorProbeReceiversPresent = 0;
        int wrongRenderQueue = 0;
        int missingAlphaTestKeyword = 0;
        var oldReferences = new List<string>();

        foreach (string prefabPath in FindNewPrisonPrefabPaths())
        {
            prefabs++;
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                bool hasDecal = false;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!NewPrisonDecalUtility.IsDecalRenderer(renderer))
                        continue;

                    hasDecal = true;
                    decalRenderers++;
                    bool isFloorDecal = NewPrisonDecalUtility.IsFloorDecalRenderer(renderer);
                    if (isFloorDecal)
                        floorDecalRenderers++;
                    else
                        wallDecalRenderers++;

                    var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                    if ((flags & StaticEditorFlags.ContributeGI) != 0)
                    {
                        contributeGi++;
                        if (!isFloorDecal)
                            wallContributeGi++;
                    }
                    else if (isFloorDecal)
                    {
                        floorMissingContributeGi++;
                    }

                    if (renderer.shadowCastingMode != ShadowCastingMode.Off)
                        shadowCasters++;

                    if (renderer.receiveShadows)
                        receiveShadowsEnabled++;

                    bool hasProbeReceiver = renderer.GetComponent<DungeonDynamicProbeReceiver>() != null;
                    if (isFloorDecal && hasProbeReceiver)
                        floorProbeReceiversPresent++;
                    else if (!isFloorDecal && !hasProbeReceiver)
                        wallMissingProbeReceivers++;

                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        Material material = materials[i];
                        if (material == null)
                            continue;

                        materialSlots++;
                        string materialPath = AssetDatabase.GetAssetPath(material);
                        if (materialPath.StartsWith(DecalMaterialFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            newPrisonSlots++;
                            ValidateTransparentMaterial(
                                material,
                                ref wrongRenderQueue,
                                ref missingAlphaTestKeyword);
                        }
                        else if (IsOriginalDecalMaterialPath(materialPath))
                        {
                            oldKriptoFxSlots++;
                            if (oldReferences.Count < 30)
                                oldReferences.Add($"{prefabPath} | {GetRelativePath(root.transform, renderer.transform)} | {materialPath}");
                        }
                    }
                }

                if (hasDecal)
                    prefabsWithDecals++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        report.AppendLine("[NewPrisonDecalMaterialSetup] Verification report");
        report.AppendLine($"prefabs={prefabs}, prefabsWithDecals={prefabsWithDecals}, decalRenderers={decalRenderers}, floorDecalRenderers={floorDecalRenderers}, wallDecalRenderers={wallDecalRenderers}, materialSlots={materialSlots}");
        report.AppendLine($"newPrisonSlots={newPrisonSlots}, oldKriptoFxSlots={oldKriptoFxSlots}, contributeGi={contributeGi}, floorMissingContributeGi={floorMissingContributeGi}, wallContributeGi={wallContributeGi}, shadowCasters={shadowCasters}, receiveShadowsEnabled={receiveShadowsEnabled}, wallMissingProbeReceivers={wallMissingProbeReceivers}, floorProbeReceiversPresent={floorProbeReceiversPresent}");
        report.AppendLine($"wrongRenderQueue={wrongRenderQueue}, missingAlphaTestKeyword={missingAlphaTestKeyword}");
        foreach (string oldReference in oldReferences)
            report.AppendLine("  old: " + oldReference);

        return report.ToString();
    }

    public static bool ReplaceRendererDecalMaterials(Renderer renderer)
    {
        if (renderer == null)
            return false;

        var maps = BuildReplacementMaps(null);
        Dictionary<string, Material> map = NewPrisonDecalUtility.IsFloorDecalRenderer(renderer)
            ? maps.floorMap
            : maps.wallMap;
        return ReplaceRendererDecalMaterials(renderer, map);
    }

    public static bool NormalizeRenderer(Renderer renderer)
    {
        if (renderer == null || !NewPrisonDecalUtility.IsDecalRenderer(renderer))
            return false;

        var maps = BuildReplacementMaps(null);
        var result = new ApplyResult();
        return NormalizeRenderer(renderer, maps, ref result);
    }

    private static ApplyResult ApplyToNewPrisonPrefabs(DecalReplacementMaps maps)
    {
        var result = new ApplyResult();
        foreach (string prefabPath in FindNewPrisonPrefabPaths())
        {
            result.prefabs++;
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            bool changed = false;
            try
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!NewPrisonDecalUtility.IsDecalRenderer(renderer))
                        continue;

                    result.decalRenderers++;
                    bool isFloorDecal = NewPrisonDecalUtility.IsFloorDecalRenderer(renderer);
                    if (isFloorDecal)
                        result.floorDecals++;
                    else
                        result.wallDecals++;

                    int slotCount = renderer.sharedMaterials.Count(material => material != null);
                    result.materialSlots += slotCount;

                    Dictionary<string, Material> map = isFloorDecal ? maps.floorMap : maps.wallMap;
                    int before = CountMappedSlots(renderer, map);
                    if (NormalizeRenderer(renderer, maps, ref result))
                        changed = true;
                    result.replacedSlots += before;
                }

                if (changed)
                {
                    result.changedPrefabs++;
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        return result;
    }

    private static bool NormalizeRenderer(
        Renderer renderer,
        DecalReplacementMaps maps,
        ref ApplyResult result)
    {
        if (renderer == null)
            return false;

        bool changed = false;
        bool isFloorDecal = NewPrisonDecalUtility.IsFloorDecalRenderer(renderer);
        Dictionary<string, Material> map = isFloorDecal ? maps.floorMap : maps.wallMap;
        if (ReplaceRendererDecalMaterials(renderer, map))
            changed = true;

        var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
        bool hasContributeGi = (flags & StaticEditorFlags.ContributeGI) != 0;
        if (isFloorDecal)
        {
            if (!hasContributeGi)
            {
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags | StaticEditorFlags.ContributeGI);
                result.floorGiEnabled++;
                changed = true;
            }

            if (RemoveProbeReceiver(renderer))
            {
                result.probeReceiversRemoved++;
                changed = true;
            }
        }
        else
        {
            if (hasContributeGi)
            {
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags & ~StaticEditorFlags.ContributeGI);
                result.wallGiDisabled++;
                changed = true;
            }

            if (EnsureProbeReceiver(renderer))
            {
                result.probeReceiversAdded++;
                changed = true;
            }
        }

        if (renderer.shadowCastingMode != ShadowCastingMode.Off)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            result.shadowDisabled++;
            changed = true;
        }

        if (renderer.receiveShadows)
        {
            renderer.receiveShadows = false;
            result.receiveShadowsDisabled++;
            changed = true;
        }

        return changed;
    }

    private static bool EnsureProbeReceiver(Renderer renderer)
    {
        if (renderer == null || renderer.GetComponent<DungeonDynamicProbeReceiver>() != null)
            return false;

        var receiver = renderer.gameObject.AddComponent<DungeonDynamicProbeReceiver>();
        var serialized = new SerializedObject(receiver);
        var includeInactiveProp = serialized.FindProperty("includeInactiveRenderers");
        var updateIntervalProp = serialized.FindProperty("updateInterval");
        var movementThresholdProp = serialized.FindProperty("movementThreshold");
        var applyOnEnableProp = serialized.FindProperty("applyOnEnable");
        var restoreOriginalProp = serialized.FindProperty("restoreOriginalUsageWhenNoSample");
        var logDiagnosticsProp = serialized.FindProperty("logSampleDiagnostics");

        if (includeInactiveProp != null) includeInactiveProp.boolValue = true;
        if (updateIntervalProp != null) updateIntervalProp.floatValue = 0.5f;
        if (movementThresholdProp != null) movementThresholdProp.floatValue = 0f;
        if (applyOnEnableProp != null) applyOnEnableProp.boolValue = true;
        if (restoreOriginalProp != null) restoreOriginalProp.boolValue = true;
        if (logDiagnosticsProp != null) logDiagnosticsProp.boolValue = false;

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(receiver);
        return true;
    }

    private static bool RemoveProbeReceiver(Renderer renderer)
    {
        if (renderer == null)
            return false;

        var receiver = renderer.GetComponent<DungeonDynamicProbeReceiver>();
        if (receiver == null)
            return false;

        UnityEngine.Object.DestroyImmediate(receiver, true);
        return true;
    }

    private static bool ReplaceRendererDecalMaterials(Renderer renderer, Dictionary<string, Material> map)
    {
        if (renderer == null || map == null || map.Count == 0)
            return false;

        bool changed = false;
        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null)
                continue;

            string materialPath = AssetDatabase.GetAssetPath(material);
            if (!map.TryGetValue(materialPath, out Material replacement) || replacement == null || replacement == material)
                continue;

            materials[i] = replacement;
            changed = true;
        }

        if (changed)
            renderer.sharedMaterials = materials;

        return changed;
    }

    private static int CountMappedSlots(Renderer renderer, Dictionary<string, Material> map)
    {
        if (renderer == null || map == null || map.Count == 0)
            return 0;

        int count = 0;
        foreach (Material material in renderer.sharedMaterials)
        {
            if (material == null)
                continue;

            if (map.ContainsKey(AssetDatabase.GetAssetPath(material)))
                count++;
        }

        return count;
    }

    private static Dictionary<string, Material> BuildReplacementMap(StringBuilder report)
    {
        EnsureFolder(DecalMaterialFolder);

        var map = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in MaterialMappings)
        {
            Material target = CreateOrUpdateMappedMaterial(mapping, report, "overlay");
            if (target == null)
                continue;

            map[mapping.sourceMaterialPath] = target;
        }

        for (int i = 0; i < FloorMaterialMappings.Length; i++)
        {
            var floorMapping = FloorMaterialMappings[i];
            for (int j = 0; j < MaterialMappings.Length; j++)
            {
                var wallMapping = MaterialMappings[j];
                if (!string.Equals(floorMapping.sourceMaterialPath, wallMapping.sourceMaterialPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                Material wallTarget = AssetDatabase.LoadAssetAtPath<Material>(wallMapping.targetMaterialPath);
                if (wallTarget != null)
                    map[floorMapping.targetMaterialPath] = wallTarget;
                break;
            }
        }

        return map;
    }

    private static DecalReplacementMaps BuildReplacementMaps(StringBuilder report)
    {
        return new DecalReplacementMaps(
            BuildReplacementMap(report),
            BuildFloorReplacementMap(report));
    }

    private static Dictionary<string, Material> BuildFloorReplacementMap(StringBuilder report)
    {
        EnsureFolder(DecalMaterialFolder);

        var map = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in FloorMaterialMappings)
        {
            Material target = CreateOrUpdateMappedMaterial(mapping, report, "floor");
            if (target == null)
                continue;

            map[mapping.sourceMaterialPath] = target;

            for (int i = 0; i < MaterialMappings.Length; i++)
            {
                var wallMapping = MaterialMappings[i];
                if (!string.Equals(mapping.sourceMaterialPath, wallMapping.sourceMaterialPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                map[wallMapping.targetMaterialPath] = target;
                break;
            }
        }

        return map;
    }

    private static Material CreateOrUpdateMappedMaterial(
        DecalMaterialMapping mapping,
        StringBuilder report,
        string kind)
    {
        Material source = AssetDatabase.LoadAssetAtPath<Material>(mapping.sourceMaterialPath);
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(mapping.shaderPath);
        if (source == null || shader == null)
        {
            report?.AppendLine($"missing source={mapping.sourceMaterialPath}, shader={mapping.shaderPath}");
            return null;
        }

        Material target = AssetDatabase.LoadAssetAtPath<Material>(mapping.targetMaterialPath);
        if (target == null)
        {
            target = new Material(source)
            {
                name = System.IO.Path.GetFileNameWithoutExtension(mapping.targetMaterialPath)
            };
            AssetDatabase.CreateAsset(target, mapping.targetMaterialPath);
        }
        else
        {
            target.CopyPropertiesFromMaterial(source);
        }

        target.shader = shader;
        NormalizeTransparentMaterial(target);
        EditorUtility.SetDirty(target);

        report?.AppendLine($"material-{kind}: {mapping.sourceMaterialPath} -> {mapping.targetMaterialPath} (queue={TransparentRenderQueue}, alphaCutoff={DefaultAlphaCutoff:0.##})");
        return target;
    }

    private static void NormalizeTransparentMaterial(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = TransparentRenderQueue;
        material.EnableKeyword("_ALPHATEST_ON");
        SetFloatIfPresent(material, "_AlphaCutoff", DefaultAlphaCutoff);
    }

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
            material.SetFloat(propertyName, value);
    }

    private static void ValidateTransparentMaterial(
        Material material,
        ref int wrongRenderQueue,
        ref int missingAlphaTestKeyword)
    {
        if (material.renderQueue != TransparentRenderQueue)
            wrongRenderQueue++;

        if (!material.IsKeywordEnabled("_ALPHATEST_ON"))
            missingAlphaTestKeyword++;
    }

    private static IEnumerable<string> FindNewPrisonPrefabPaths()
    {
        return AssetDatabase.FindAssets("t:Prefab", new[] { NewPrisonRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !string.IsNullOrEmpty(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsOriginalDecalMaterialPath(string materialPath)
    {
        if (string.IsNullOrEmpty(materialPath))
            return false;

        return MaterialMappings.Any(mapping =>
            string.Equals(mapping.sourceMaterialPath, materialPath, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
        if (root == target)
            return string.Empty;

        var names = new Stack<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }

    private static void EnsureFolder(string folderPath)
    {
        string normalized = folderPath.Replace("\\", "/");
        if (AssetDatabase.IsValidFolder(normalized))
            return;

        string[] parts = normalized.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private readonly struct DecalMaterialMapping
    {
        public readonly string sourceMaterialPath;
        public readonly string targetMaterialPath;
        public readonly string shaderPath;

        public DecalMaterialMapping(string sourceMaterialPath, string targetMaterialPath, string shaderPath)
        {
            this.sourceMaterialPath = sourceMaterialPath;
            this.targetMaterialPath = targetMaterialPath;
            this.shaderPath = shaderPath;
        }
    }

    private readonly struct DecalReplacementMaps
    {
        public readonly Dictionary<string, Material> wallMap;
        public readonly Dictionary<string, Material> floorMap;

        public DecalReplacementMaps(
            Dictionary<string, Material> wallMap,
            Dictionary<string, Material> floorMap)
        {
            this.wallMap = wallMap ?? new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            this.floorMap = floorMap ?? new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private struct ApplyResult
    {
        public int prefabs;
        public int changedPrefabs;
        public int decalRenderers;
        public int floorDecals;
        public int wallDecals;
        public int materialSlots;
        public int replacedSlots;
        public int floorGiEnabled;
        public int wallGiDisabled;
        public int shadowDisabled;
        public int receiveShadowsDisabled;
        public int probeReceiversAdded;
        public int probeReceiversRemoved;
    }
}
