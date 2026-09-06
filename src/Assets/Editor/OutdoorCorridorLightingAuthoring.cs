using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Applies Outdoor_Corridor-only lighting authoring. The visible lamp material stays
/// emissive, but it is excluded from baked GI so the baked spot lights own illumination.
/// </summary>
public static class OutdoorCorridorLightingAuthoring
{
    private const string SourcePrefabPath =
        "Assets/Prefabs/map_piece/NewPrison/Tiles/Outdoor_Corridor.prefab";
    private const string MaterialFolder =
        "Assets/Prefabs/map_piece/NewPrison/Tiles/LightingMaterials";
    private const string MaterialPath = MaterialFolder + "/Outdoor_Corridor_Lamp_VisibleOnly.mat";
    private const float VisibleEmission = 10f;
    private const float BakedSpotIntensity = 10f;
    private const float BakedSpotIndirectMultiplier = 0.5f;
    private static string s_buildState = "Idle";
    private static string s_buildResult = string.Empty;

    [MenuItem("Tools/Dungeon Lighting/V2/Outdoor Corridor/Apply Clean Baked Lighting")]
    public static void ApplyMenu()
    {
        Debug.Log(ApplyCli());
    }

    public static string ApplyCli()
    {
        GameObject root = null;
        try
        {
            EnsureFolder(MaterialFolder);
            root = PrefabUtility.LoadPrefabContents(SourcePrefabPath);

            Renderer[] lampRenderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(IsLampGlowRenderer)
                .ToArray();
            if (lampRenderers.Length == 0)
                throw new InvalidOperationException(
                    "No Outdoor_Corridor lamp glow renderer named Light_18 was found below Lights.");

            Material source = lampRenderers
                .SelectMany(renderer => renderer.sharedMaterials ?? Array.Empty<Material>())
                .FirstOrDefault(material => material != null && material.HasProperty("_EmissionColor"));
            if (source == null)
                throw new InvalidOperationException("Outdoor_Corridor lamp glow has no emissive source material.");

            Material visibleOnly = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (visibleOnly == null)
            {
                visibleOnly = new Material(source) { name = "Outdoor_Corridor_Lamp_VisibleOnly" };
                AssetDatabase.CreateAsset(visibleOnly, MaterialPath);
            }

            visibleOnly.CopyPropertiesFromMaterial(source);
            visibleOnly.name = "Outdoor_Corridor_Lamp_VisibleOnly";
            visibleOnly.EnableKeyword("_EMISSION");
            visibleOnly.SetColor("_EmissionColor", new Color(VisibleEmission, VisibleEmission, VisibleEmission, 1f));
            // URP derives the _EMISSION shader keyword from AnyEmissive. EmissiveIsBlack
            // therefore makes the lamp look completely off after material validation.
            // RealtimeEmissive keeps the visible shader emission enabled without marking
            // the material as BakedEmissive, so the baked spot lights remain the only
            // contributors to the lightmap.
            visibleOnly.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(visibleOnly);

            int replacedSlots = 0;
            foreach (Renderer renderer in lampRenderers)
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null || !material.HasProperty("_EmissionColor"))
                        continue;

                    materials[i] = visibleOnly;
                    replacedSlots++;
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }

            Light[] bakedSpots = root.GetComponentsInChildren<Light>(true)
                .Where(light => light.type == LightType.Spot)
                .ToArray();
            if (bakedSpots.Length == 0)
                throw new InvalidOperationException("Outdoor_Corridor has no spot lights to configure.");

            foreach (Light light in bakedSpots)
            {
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.intensity = BakedSpotIntensity;
                light.bounceIntensity = BakedSpotIndirectMultiplier;
                EditorUtility.SetDirty(light);
            }

            PrefabUtility.SaveAsPrefabAsset(root, SourcePrefabPath, out bool saved);
            if (!saved)
                throw new InvalidOperationException("Could not save Outdoor_Corridor source prefab.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return
                $"PASS: Outdoor_Corridor clean baked-light authoring applied. " +
                $"lampRenderers={lampRenderers.Length}, replacedMaterialSlots={replacedSlots}, " +
                $"bakedSpotLights={bakedSpots.Length}, emission={VisibleEmission}, " +
                $"spotIntensity={BakedSpotIntensity}, indirect={BakedSpotIndirectMultiplier}, " +
                $"material={MaterialPath}";
        }
        catch (Exception exception)
        {
            return "ERROR: " + exception;
        }
        finally
        {
            if (root != null)
                PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static string ReportCli()
    {
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(SourcePrefabPath);
            Renderer[] lampRenderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(IsLampGlowRenderer)
                .ToArray();
            Light[] spots = root.GetComponentsInChildren<Light>(true)
                .Where(light => light.type == LightType.Spot)
                .ToArray();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            int assignedSlots = lampRenderers.Sum(renderer =>
                (renderer.sharedMaterials ?? Array.Empty<Material>()).Count(candidate => candidate == material));
            bool allBaked = spots.All(light => light.lightmapBakeType == LightmapBakeType.Baked);
            bool valuesMatch = spots.All(light =>
                Mathf.Approximately(light.intensity, BakedSpotIntensity) &&
                Mathf.Approximately(light.bounceIntensity, BakedSpotIndirectMultiplier));
            bool visibleEmissionEnabled = material != null &&
                                          material.IsKeywordEnabled("_EMISSION") &&
                                          material.globalIlluminationFlags == MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return
                $"lampRenderers={lampRenderers.Length}, spots={spots.Length}, assignedSlots={assignedSlots}, " +
                $"allBaked={allBaked}, valuesMatch={valuesMatch}, materialExists={material != null}, " +
                $"visibleEmissionEnabled={visibleEmissionEnabled}, bakedEmissive={material != null && (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.BakedEmissive) != 0}";
        }
        finally
        {
            if (root != null)
                PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static string ScheduleFullRebuildCli()
    {
        if (EditorApplication.isPlaying)
            return "ERROR: Exit Play Mode before rebuilding Outdoor_Corridor.";
        if (Lightmapping.isRunning || string.Equals(s_buildState, "Running", StringComparison.Ordinal))
            return "ERROR: An Outdoor_Corridor bake is already running.";
        if (string.IsNullOrWhiteSpace(EditorSceneManager.GetActiveScene().path))
            return "ERROR: Save the current scene before rebuilding Outdoor_Corridor.";

        s_buildState = "Scheduled";
        s_buildResult = string.Empty;
        EditorApplication.delayCall += RunScheduledFullRebuild;
        return "PASS: Outdoor_Corridor full rebuild scheduled.";
    }

    public static string GetBuildStateCli()
    {
        return $"state={s_buildState}, lightmapping={Lightmapping.isRunning}\n{s_buildResult}";
    }

    public static string RunFullRebuildNowCli()
    {
        EditorApplication.delayCall -= RunScheduledFullRebuild;
        RunScheduledFullRebuild();
        return s_buildResult;
    }

    private static void RunScheduledFullRebuild()
    {
        s_buildState = "Running";
        try
        {
            s_buildResult = BAKEROTATETOOLV2.BuildRoomOneClickCli(SourcePrefabPath, false);
            s_buildState = s_buildResult.StartsWith("PASS:", StringComparison.Ordinal)
                ? "Completed"
                : "Failed";
        }
        catch (Exception exception)
        {
            s_buildResult = "ERROR: " + exception;
            s_buildState = "Failed";
        }

        if (s_buildState == "Completed")
            Debug.Log("[OutdoorCorridorLightingAuthoring] " + s_buildResult);
        else
            Debug.LogError("[OutdoorCorridorLightingAuthoring] " + s_buildResult);
    }

    private static bool IsLampGlowRenderer(Renderer renderer)
    {
        if (renderer == null || !string.Equals(renderer.gameObject.name, "Light_18", StringComparison.Ordinal))
            return false;

        for (Transform current = renderer.transform.parent; current != null; current = current.parent)
        {
            if (string.Equals(current.name, "Lights", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
