using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class NewPrisonLightingPrefabNormalizer
{
    private const string LightingPrefabFolder = "Assets/Prefabs/map_piece/NewPrison/Lighting_Prefabs";
    private const string TileModifiedFolder = "Assets/Prefabs/map_piece/NewPrison/Tile_modified";
    private const string TilesFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles";
    private const string KriptoFxMaterialFolder = "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials";

    private const string CeilingSourcePrefab = "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Prefabs/Modules/Ceiling_Lights_DualSided.prefab";
    private const string Lamp01SpotSourcePrefab = "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Prefabs/Props/Lamps/Lamp_01_Spot.prefab";
    private const string Lamp02SourcePrefab = "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Prefabs/Props/Lamps/Lamp_02.prefab";
    private const string Lamp05SourcePrefab = "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Prefabs/Props/Lamps/Lamp_05.prefab";
    private const string DeprecatedLamp01BSpotPrefab = "Assets/Prefabs/map_piece/NewPrison/Lighting_Prefabs/Lamp_01_B_Spot.prefab";

    private static readonly Color WarmCeilingLight = new Color(1f, 0.945f, 0.875f, 1f);
    private static readonly Color WarmWallLight = new Color(1f, 0.974f, 0.904f, 1f);

    public static string NormalizeTileModifiedAndTilesLighting()
    {
        EnsureAssetFolder(LightingPrefabFolder);
        NewPrisonEmissionMaterialSetup.SetupNewPrisonEmissionMaterials();

        var materials = LoadMaterials();
        var prefabs = CreateSharedLightingPrefabs(materials);
        var report = new StringBuilder();
        report.AppendLine("[NewPrisonLightingPrefabNormalizer] Shared prefabs:");
        foreach (var prefab in prefabs)
            report.AppendLine($"  {prefab.Key}: {AssetDatabase.GetAssetPath(prefab.Value)}");

        int totalPrefabs = 0;
        int changedPrefabs = 0;
        int replacedObjects = 0;
        int removedObjects = 0;
        int materialReplacements = 0;
        int normalizedLights = 0;
        int normalizedDecals = 0;

        DeleteDeprecatedLightingPrefab();
        report.AppendLine(NewPrisonReceptionWindowBakeSafeSetup.NormalizeTileModifiedAndTilesReceptionWindows());

        foreach (string folder in new[] { TileModifiedFolder, TilesFolder })
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            foreach (string guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                totalPrefabs++;

                var result = NormalizePrefab(prefabPath, materials, prefabs);
                if (!result.changed)
                    continue;

                changedPrefabs++;
                replacedObjects += result.replacedObjects;
                removedObjects += result.removedObjects;
                materialReplacements += result.materialReplacements;
                normalizedLights += result.normalizedLights;
                normalizedDecals += result.normalizedDecals;
                report.AppendLine(
                    $"  {prefabPath}: replaced={result.replacedObjects}, removed={result.removedObjects}, materials={result.materialReplacements}, lights={result.normalizedLights}, decals={result.normalizedDecals}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        report.AppendLine(
            $"[NewPrisonLightingPrefabNormalizer] Done prefabs={totalPrefabs}, changed={changedPrefabs}, replaced={replacedObjects}, removed={removedObjects}, materials={materialReplacements}, lights={normalizedLights}, decals={normalizedDecals}");
        return report.ToString();
    }

    public static string ReportTileModifiedAndTilesLighting()
    {
        var report = new StringBuilder();
        var folders = new[] { TileModifiedFolder, TilesFolder };
        var offReferences = new List<string>();
        var realtimeLampLights = new List<string>();
        int centralPrefabInstances = 0;

        foreach (string folder in folders)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null)
                            continue;

                        foreach (var material in renderer.sharedMaterials)
                        {
                            if (material == null)
                                continue;

                            string materialPath = AssetDatabase.GetAssetPath(material);
                            if (IsOffVariantMaterial(material, materialPath))
                            {
                                offReferences.Add($"{prefabPath} | {GetRelativePath(root.transform, renderer.transform)} | {materialPath}");
                            }
                        }
                    }

                    foreach (var light in root.GetComponentsInChildren<Light>(true))
                    {
                        if (light == null || !IsLampRelatedPath(GetRelativePath(root.transform, light.transform)))
                            continue;

                        if (light.lightmapBakeType != LightmapBakeType.Baked)
                            realtimeLampLights.Add($"{prefabPath} | {GetRelativePath(root.transform, light.transform)} | {light.lightmapBakeType}");
                    }

                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (IsCentralLightingInstanceRoot(transform.gameObject))
                            centralPrefabInstances++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        report.AppendLine("[NewPrisonLightingPrefabNormalizer] Verification report");
        report.AppendLine($"  centralPrefabInstances={centralPrefabInstances}");
        report.AppendLine($"  offMaterialReferences={offReferences.Count}");
        foreach (string line in offReferences.Take(20))
            report.AppendLine($"    off: {line}");
        report.AppendLine($"  nonBakedLampLights={realtimeLampLights.Count}");
        foreach (string line in realtimeLampLights.Take(20))
            report.AppendLine($"    light: {line}");
        return report.ToString();
    }

    private static NormalizationResult NormalizePrefab(
        string prefabPath,
        MaterialSet materials,
        Dictionary<LightingPrefabKind, GameObject> prefabs)
    {
        var result = new NormalizationResult();
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            result.removedObjects = RemoveDeprecatedLamp01BSpot(root);
            result.changed |= result.removedObjects > 0;

            var candidates = CollectReplacementCandidates(root, materials);
            foreach (var candidate in candidates)
            {
                if (!prefabs.TryGetValue(candidate.kind, out var sharedPrefab) || sharedPrefab == null)
                    continue;

                ReplaceWithSharedPrefab(candidate.transform, sharedPrefab);
                result.replacedObjects++;
                result.changed = true;
            }

            result.materialReplacements = NormalizeLampMaterials(root, materials);
            result.normalizedLights = NormalizeExistingLampLights(root);
            result.normalizedDecals = NormalizeDecalRenderers(root);
            result.changed |= result.materialReplacements > 0 || result.normalizedLights > 0 || result.normalizedDecals > 0;

            if (result.changed)
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return result;
    }

    private static List<ReplacementCandidate> CollectReplacementCandidates(GameObject root, MaterialSet materials)
    {
        var candidates = new List<ReplacementCandidate>();
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == root.transform)
                continue;

            if (IsCentralLightingInstanceRoot(transform.gameObject))
                continue;

            string name = transform.name;
            LightingPrefabKind kind;
            if (name.StartsWith("Ceiling_Lights_DualSided", StringComparison.OrdinalIgnoreCase))
            {
                kind = LightingPrefabKind.CeilingLightsDualSided;
            }
            else if (name.StartsWith("Lamp_01_Spot", StringComparison.OrdinalIgnoreCase))
            {
                kind = LightingPrefabKind.Lamp01Spot;
            }
            else if (name.StartsWith("Lamp_02", StringComparison.OrdinalIgnoreCase) && HasMaterial(transform.gameObject, materials.lamps01Off))
            {
                kind = LightingPrefabKind.Lamp02UsingLamps01;
            }
            else if (name.StartsWith("Lamp_05", StringComparison.OrdinalIgnoreCase))
            {
                kind = LightingPrefabKind.Lamp05;
            }
            else
            {
                continue;
            }

            candidates.Add(new ReplacementCandidate(transform, kind));
        }

        var selected = new List<ReplacementCandidate>();
        foreach (var candidate in candidates.OrderBy(c => GetDepth(c.transform)))
        {
            bool hasSelectedAncestor = selected.Any(existing => candidate.transform.IsChildOf(existing.transform));
            if (!hasSelectedAncestor)
                selected.Add(candidate);
        }

        return selected;
    }

    private static void ReplaceWithSharedPrefab(Transform oldTransform, GameObject prefabAsset)
    {
        Transform parent = oldTransform.parent;
        int siblingIndex = oldTransform.GetSiblingIndex();
        string oldName = oldTransform.name;
        bool activeSelf = oldTransform.gameObject.activeSelf;
        Vector3 localPosition = oldTransform.localPosition;
        Quaternion localRotation = oldTransform.localRotation;
        Vector3 localScale = oldTransform.localScale;

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset);
        instance.name = oldName;
        instance.SetActive(activeSelf);
        instance.transform.SetParent(parent, false);
        instance.transform.SetSiblingIndex(siblingIndex);
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = localRotation;
        instance.transform.localScale = localScale;

        UnityEngine.Object.DestroyImmediate(oldTransform.gameObject);
    }

    private static int RemoveDeprecatedLamp01BSpot(GameObject root)
    {
        var targets = new List<Transform>();
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == root.transform)
                continue;

            if (transform.name.StartsWith("Lamp_01_B_Spot", StringComparison.OrdinalIgnoreCase))
                targets.Add(transform);
        }

        int removed = 0;
        foreach (var target in targets.OrderByDescending(GetDepth))
        {
            if (target == null)
                continue;

            UnityEngine.Object.DestroyImmediate(target.gameObject);
            removed++;
        }

        return removed;
    }

    private static int NormalizeLampMaterials(GameObject root, MaterialSet materials)
    {
        int changedCount = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
                continue;

            var sharedMaterials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                var replacement = ResolveMaterialReplacement(sharedMaterials[i], materials);
                if (replacement == null || replacement == sharedMaterials[i])
                    continue;

                sharedMaterials[i] = replacement;
                changed = true;
                changedCount++;
            }

            if (changed)
                renderer.sharedMaterials = sharedMaterials;
        }

        return changedCount;
    }

    private static Material ResolveMaterialReplacement(Material material, MaterialSet materials)
    {
        if (material == null)
            return null;

        if (material == materials.lamps01Off)
            return materials.lamps01;

        if (material == materials.lamps05Off || material == materials.newPrisonLamps05Off)
            return materials.lamps05;

        return null;
    }

    private static int NormalizeExistingLampLights(GameObject root)
    {
        int changed = 0;
        foreach (var light in root.GetComponentsInChildren<Light>(true))
        {
            if (light == null)
                continue;

            string path = GetRelativePath(root.transform, light.transform);
            if (!IsLampRelatedPath(path))
                continue;

            LightProfile profile = ResolveLightProfile(path);
            if (ApplyLightProfile(light, profile))
                changed++;
        }

        return changed;
    }

    private static int NormalizeDecalRenderers(GameObject root)
    {
        int changed = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !NewPrisonDecalUtility.IsDecalRenderer(renderer))
                continue;

            if (NewPrisonDecalMaterialSetup.NormalizeRenderer(renderer))
                changed++;
        }

        return changed;
    }

    private static Dictionary<LightingPrefabKind, GameObject> CreateSharedLightingPrefabs(MaterialSet materials)
    {
        var prefabs = new Dictionary<LightingPrefabKind, GameObject>
        {
            [LightingPrefabKind.CeilingLightsDualSided] = CreateSharedPrefab(
                CeilingSourcePrefab,
                $"{LightingPrefabFolder}/Ceiling_Lights_DualSided.prefab",
                root =>
                {
                    ReplaceAllLampMaterials(root, materials.lamps05);
                    EnsureLightCount(root, "Spotlight", 4);
                    ApplyLightProfileToChildren(root, LightProfile.Ceiling);
                }),
            [LightingPrefabKind.Lamp01Spot] = CreateSharedPrefab(
                Lamp01SpotSourcePrefab,
                $"{LightingPrefabFolder}/Lamp_01_Spot.prefab",
                root =>
                {
                    ReplaceAllLampMaterials(root, materials.lamps01);
                    EnsureLightCount(root, "Spotlight", 1);
                    ApplyLightProfileToChildren(root, LightProfile.Wall);
                }),
            [LightingPrefabKind.Lamp02UsingLamps01] = CreateSharedPrefab(
                Lamp02SourcePrefab,
                $"{LightingPrefabFolder}/Lamp_02_Lamps_01.prefab",
                root =>
                {
                    ReplaceAllLampMaterials(root, materials.lamps01);
                    EnsureLightCount(root, "Spotlight", 1);
                    ApplyLightProfileToChildren(root, LightProfile.Wall);
                }),
            [LightingPrefabKind.Lamp05] = CreateSharedPrefab(
                Lamp05SourcePrefab,
                $"{LightingPrefabFolder}/Lamp_05.prefab",
                root =>
                {
                    ReplaceAllLampMaterials(root, materials.lamps05);
                    EnsureLightCount(root, "Spotlight", 1);
                    ApplyLightProfileToChildren(root, LightProfile.Wall);
                })
        };

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return prefabs;
    }

    private static void DeleteDeprecatedLightingPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(DeprecatedLamp01BSpotPrefab) != null)
            AssetDatabase.DeleteAsset(DeprecatedLamp01BSpotPrefab);
    }

    private static GameObject CreateSharedPrefab(string sourcePrefabPath, string outputPath, Action<GameObject> normalize)
    {
        var sourceRoot = PrefabUtility.LoadPrefabContents(sourcePrefabPath);
        try
        {
            normalize(sourceRoot);
            return PrefabUtility.SaveAsPrefabAsset(sourceRoot, outputPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(sourceRoot);
        }
    }

    private static void ReplaceAllLampMaterials(GameObject root, Material material)
    {
        if (root == null || material == null)
            return;

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var sharedMaterials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                if (sharedMaterials[i] == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(sharedMaterials[i]);
                if (sharedMaterials[i].name.IndexOf("Lamp", StringComparison.OrdinalIgnoreCase) < 0 &&
                    path.IndexOf("Lamp", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                sharedMaterials[i] = material;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = sharedMaterials;
        }
    }

    private static void EnsureLightCount(GameObject root, string baseName, int count)
    {
        var lights = root.GetComponentsInChildren<Light>(true).ToList();
        while (lights.Count < count)
        {
            var lightObject = new GameObject(BuildIndexedName(baseName, lights.Count));
            lightObject.transform.SetParent(root.transform, false);
            lights.Add(lightObject.AddComponent<Light>());
        }

        for (int i = lights.Count - 1; i >= count; i--)
            UnityEngine.Object.DestroyImmediate(lights[i].gameObject);

        lights = root.GetComponentsInChildren<Light>(true).ToList();
        for (int i = 0; i < lights.Count; i++)
            lights[i].name = BuildIndexedName(baseName, i);
    }

    private static string BuildIndexedName(string baseName, int index)
    {
        return index == 0 ? baseName : $"{baseName} ({index})";
    }

    private static void ApplyLightProfileToChildren(GameObject root, LightProfile profile)
    {
        foreach (var light in root.GetComponentsInChildren<Light>(true))
            ApplyLightProfile(light, profile);
    }

    private static bool ApplyLightProfile(Light light, LightProfile profile)
    {
        if (light == null)
            return false;

        bool changed = false;
        LightType type = profile == LightProfile.Lamp03 || profile == LightProfile.Lamp03B
            ? LightType.Point
            : LightType.Spot;
        float intensity = profile == LightProfile.Ceiling ? 2f : 1f;
        float range = profile switch
        {
            LightProfile.Ceiling => 5f,
            LightProfile.Lamp03B => 6f,
            _ => 10f
        };
        Color color = profile == LightProfile.Ceiling ? WarmCeilingLight : WarmWallLight;

        if (!light.enabled)
        {
            light.enabled = true;
            changed = true;
        }

        if (light.type != type)
        {
            light.type = type;
            changed = true;
        }

        if (light.lightmapBakeType != LightmapBakeType.Baked)
        {
            light.lightmapBakeType = LightmapBakeType.Baked;
            changed = true;
        }

        if (!Mathf.Approximately(light.intensity, intensity))
        {
            light.intensity = intensity;
            changed = true;
        }

        if (!Mathf.Approximately(light.range, range))
        {
            light.range = range;
            changed = true;
        }

        if (light.color != color)
        {
            light.color = color;
            changed = true;
        }

        if (light.shadows != LightShadows.Soft)
        {
            light.shadows = LightShadows.Soft;
            changed = true;
        }

        return changed;
    }

    private static LightProfile ResolveLightProfile(string path)
    {
        if (path.IndexOf("Lamp_03_B", StringComparison.OrdinalIgnoreCase) >= 0)
            return LightProfile.Lamp03B;

        if (path.IndexOf("Lamp_03", StringComparison.OrdinalIgnoreCase) >= 0)
            return LightProfile.Lamp03;

        return path.IndexOf("Ceiling_Lights_DualSided", StringComparison.OrdinalIgnoreCase) >= 0
            ? LightProfile.Ceiling
            : LightProfile.Wall;
    }

    private static bool HasMaterial(GameObject root, Material target)
    {
        if (root == null || target == null)
            return false;

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.sharedMaterials.Any(material => material == target))
                return true;
        }

        return false;
    }

    private static bool IsLampRelatedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return path.IndexOf("Lamp", StringComparison.OrdinalIgnoreCase) >= 0 ||
               path.IndexOf("Ceiling_Lights", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsOffVariantMaterial(Material material, string materialPath)
    {
        if (material == null)
            return false;

        if (material.name.EndsWith("_Off", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(materialPath))
            return false;

        string fileName = Path.GetFileNameWithoutExtension(materialPath);
        return fileName.EndsWith("_Off", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCentralLightingInstanceRoot(GameObject gameObject)
    {
        if (gameObject == null || !PrefabUtility.IsAnyPrefabInstanceRoot(gameObject))
            return false;

        var source = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
        if (source == null)
            return false;

        string sourcePath = AssetDatabase.GetAssetPath(source);
        return sourcePath.StartsWith(LightingPrefabFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
        if (root == null || target == null)
            return string.Empty;

        var parts = new List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    private static int GetDepth(Transform transform)
    {
        int depth = 0;
        Transform current = transform;
        while (current != null)
        {
            depth++;
            current = current.parent;
        }

        return depth;
    }

    private static MaterialSet LoadMaterials()
    {
        var materials = new MaterialSet
        {
            lamps01 = LoadMaterial($"{KriptoFxMaterialFolder}/Lamps_01.mat"),
            lamps01Off = LoadMaterial($"{KriptoFxMaterialFolder}/Lamps_01_Off.mat"),
            lamps02 = LoadMaterial("Assets/Prefabs/map_piece/NewPrison/Power_Material/Lamps_02.mat"),
            lamps05 = LoadMaterial($"{KriptoFxMaterialFolder}/Lamps_05.mat"),
            lamps05Off = LoadMaterial($"{KriptoFxMaterialFolder}/Lamps_05_Off.mat"),
            newPrisonLamps05Off = LoadMaterial("Assets/Prefabs/map_piece/NewPrison/Power_Material/Lamps_05_Off.mat")
        };

        if (materials.lamps01 == null || materials.lamps01Off == null || materials.lamps02 == null ||
            materials.lamps05 == null || materials.lamps05Off == null)
        {
            throw new InvalidOperationException("Missing one or more NewPrison lamp materials.");
        }

        return materials;
    }

    private static Material LoadMaterial(string path)
    {
        return AssetDatabase.LoadAssetAtPath<Material>(path);
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

    private enum LightingPrefabKind
    {
        CeilingLightsDualSided,
        Lamp01Spot,
        Lamp02UsingLamps01,
        Lamp05
    }

    private enum LightProfile
    {
        Ceiling,
        Wall,
        Lamp03,
        Lamp03B
    }

    private struct ReplacementCandidate
    {
        public readonly Transform transform;
        public readonly LightingPrefabKind kind;

        public ReplacementCandidate(Transform transform, LightingPrefabKind kind)
        {
            this.transform = transform;
            this.kind = kind;
        }
    }

    private struct NormalizationResult
    {
        public bool changed;
        public int replacedObjects;
        public int removedObjects;
        public int materialReplacements;
        public int normalizedLights;
        public int normalizedDecals;
    }

    private sealed class MaterialSet
    {
        public Material lamps01;
        public Material lamps01Off;
        public Material lamps02;
        public Material lamps05;
        public Material lamps05Off;
        public Material newPrisonLamps05Off;
    }
}
