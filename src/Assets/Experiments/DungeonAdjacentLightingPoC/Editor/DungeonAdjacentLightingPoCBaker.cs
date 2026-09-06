using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonAdjacentLightingPoC.Editor
{
    public static class DungeonAdjacentLightingPoCBaker
    {
        private const string RootFolder = "Assets/Experiments/DungeonAdjacentLightingPoC";
        private const string StartScenePath =
            RootFolder + "/Scenes/StartRoom_Doorways_Door_SM_A_DoorWayPoint_ExtensionBake.unity";
        private const string AdminScenePath =
            RootFolder + "/Scenes/Admin_Doorways_Door_SM_A_DoorWayPoint_ExtensionBake.unity";
        private const string CapturedFolder = RootFolder + "/Generated/Captured";

        private static readonly (string source, string power100, string power0)[] EmissionVariants =
        {
            (
                "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials/Lamps_01.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_01_P100.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_01_P0_Black.mat"),
            (
                "Assets/Prefabs/map_piece/NewPrison/Power_Material/Lamps_02.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_02_P100.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_02_P0_Black.mat"),
            (
                "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials/Lamps_05.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P100.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P0_Black.mat"),
            (
                "Assets/Prefabs/map_piece/NewPrison/Power_Material/Lamps_05.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P100.mat",
                "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P0_Black.mat")
        };

        [MenuItem("Tools/Dungeon/Lighting/Adjacent Lightmap PoC/Bake Start Extension P100")]
        public static void BakeStartFromMenu()
        {
            Debug.Log(BakeStartP100());
        }

        [MenuItem("Tools/Dungeon/Lighting/Adjacent Lightmap PoC/Bake Admin Extension P100")]
        public static void BakeAdminFromMenu()
        {
            Debug.Log(BakeAdminP100());
        }

        public static string BakeStartP100()
        {
            return BakeAndCaptureP100(StartScenePath, "StartRoom");
        }

        public static string BakeAdminP100()
        {
            return BakeAndCaptureP100(AdminScenePath, "AdminstrativeSegregation");
        }

        [MenuItem("Tools/Dungeon/Lighting/Adjacent Lightmap PoC/Bake Start Extension P0")]
        public static void BakeStartP0FromMenu()
        {
            Debug.Log(BakeStartP0());
        }

        [MenuItem("Tools/Dungeon/Lighting/Adjacent Lightmap PoC/Bake Admin Extension P0")]
        public static void BakeAdminP0FromMenu()
        {
            Debug.Log(BakeAdminP0());
        }

        public static string BakeStartP0()
        {
            return BakeAndCapture(StartScenePath, "StartRoom", DungeonTileLightmapSwitcher.PowerLevel.P0);
        }

        public static string BakeAdminP0()
        {
            return BakeAndCapture(AdminScenePath, "AdminstrativeSegregation", DungeonTileLightmapSwitcher.PowerLevel.P0);
        }

        private static string BakeAndCaptureP100(string scenePath, string label)
        {
            return BakeAndCapture(scenePath, label, DungeonTileLightmapSwitcher.PowerLevel.P100);
        }

        private static string BakeAndCapture(
            string scenePath,
            string label,
            DungeonTileLightmapSwitcher.PowerLevel powerLevel)
        {
            if (Lightmapping.isRunning)
                return "FAIL: another lightmap bake is already running.";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                return $"FAIL: isolated bake scene is missing: {scenePath}";

            DungeonAdjacentLightingPoCEditorState originalState =
                DungeonAdjacentLightingPoCEditorState.Capture();
            Scene bakeScene = default;
            try
            {
                bakeScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(bakeScene);

                DungeonAdjacentLightmapExtension[] allExtensions =
                    UnityEngine.Object.FindObjectsByType<DungeonAdjacentLightmapExtension>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None);
                DungeonAdjacentLightmapExtension[] extensions =
                    FindExtensionsInScene(allExtensions, bakeScene);
                if (extensions.Length == 0)
                    return $"FAIL: no extension renderers in {scenePath}";

                for (int i = 0; i < extensions.Length; i++)
                {
                    if (extensions[i].ExtensionRenderer == null)
                        return $"FAIL: extension '{extensions[i].DoorwayId}' has no renderer in {scenePath}";
                    StabilizeCapturedState(
                        extensions[i],
                        label,
                        DungeonTileLightmapSwitcher.PowerLevel.P100);
                }

                ApplyPowerState(bakeScene, powerLevel);
                for (int i = 0; i < extensions.Length; i++)
                    extensions[i].ExtensionRenderer.enabled = true;

                Lightmapping.Clear();
                DateTime started = DateTime.UtcNow;
                bool baked = Lightmapping.Bake();
                TimeSpan elapsed = DateTime.UtcNow - started;
                if (!baked)
                    return $"FAIL: Lightmapping.Bake returned false for {label} {powerLevel} after {elapsed.TotalSeconds:F1}s.";

                LightmapData[] lightmaps = LightmapSettings.lightmaps;
                var capturedAtlases = new Dictionary<int, CapturedAtlas>();
                var report = new StringBuilder();
                report.AppendLine($"PASS {label} room-local extensions {powerLevel} bake");
                report.AppendLine($"scene={scenePath}");
                report.AppendLine($"elapsedSeconds={elapsed.TotalSeconds:F1}");
                report.AppendLine($"lightmapCount={(lightmaps != null ? lightmaps.Length : 0)}");
                report.AppendLine($"extensionCount={extensions.Length}");

                for (int i = 0; i < extensions.Length; i++)
                {
                    DungeonAdjacentLightmapExtension extension = extensions[i];
                    MeshRenderer renderer = extension.ExtensionRenderer;
                    int lightmapIndex = renderer.lightmapIndex;
                    if (lightmapIndex < 0 || lightmaps == null || lightmapIndex >= lightmaps.Length ||
                        lightmaps[lightmapIndex] == null || lightmaps[lightmapIndex].lightmapColor == null)
                    {
                        return
                            $"FAIL: {label} extension '{extension.DoorwayId}' has no valid lightmap " +
                            $"after bake. index={lightmapIndex}";
                    }

                    if (!capturedAtlases.TryGetValue(lightmapIndex, out CapturedAtlas captured))
                    {
                        LightmapData lightmap = lightmaps[lightmapIndex];
                        captured = new CapturedAtlas
                        {
                            color = CopyTextureAsset(
                                lightmap.lightmapColor,
                                label,
                                powerLevel,
                                $"LM{lightmapIndex}_Color"),
                            direction = CopyTextureAsset(
                                lightmap.lightmapDir,
                                label,
                                powerLevel,
                                $"LM{lightmapIndex}_Direction")
                        };
                        capturedAtlases.Add(lightmapIndex, captured);
                    }

                    extension.CaptureState(
                        powerLevel,
                        captured.color,
                        captured.direction,
                        renderer.lightmapScaleOffset);
                    EditorUtility.SetDirty(extension);
                    report.AppendLine(
                        $"extension[{i}]={extension.DoorwayId}|lightmapIndex={lightmapIndex}|" +
                        $"scaleOffset={renderer.lightmapScaleOffset}|directional={captured.direction != null}");
                }

                report.AppendLine($"capturedAtlasCount={capturedAtlases.Count}");
                report.AppendLine("pairBakeData=NONE");
                report.AppendLine("complexity=O(roomCount*powerStateCount); all doorways share each room-state bake");

                // Keep isolated scenes in their authored P100 state so P0 -> P100 rebakes are deterministic.
                ApplyPowerState(bakeScene, DungeonTileLightmapSwitcher.PowerLevel.P100);
                EditorSceneManager.MarkSceneDirty(bakeScene);
                EditorSceneManager.SaveScene(bakeScene);
                AssetDatabase.SaveAssets();

                string reportPath = $"{RootFolder}/Generated/{label}_{powerLevel}_BakeReport.txt";
                File.WriteAllText(reportPath, report.ToString());
                AssetDatabase.ImportAsset(reportPath);
                return report.ToString();
            }
            catch (Exception exception)
            {
                return $"FAIL: {label} {powerLevel} bake threw {exception}";
            }
            finally
            {
                if (bakeScene.IsValid() && bakeScene.isLoaded)
                    EditorSceneManager.CloseScene(bakeScene, true);
                originalState.Restore();
            }
        }

        private static void StabilizeCapturedState(
            DungeonAdjacentLightmapExtension extension,
            string label,
            DungeonTileLightmapSwitcher.PowerLevel powerLevel)
        {
            DungeonAdjacentLightmapExtension.BakedState state = extension.GetState(powerLevel);
            if (!state.IsValid)
                return;

            string path = AssetDatabase.GetAssetPath(state.lightmapColor);
            if (!string.IsNullOrEmpty(path) && path.StartsWith(CapturedFolder + "/", StringComparison.Ordinal))
                return;

            string doorwayToken = Hash128.Compute(extension.DoorwayId ?? string.Empty).ToString();
            Texture2D stableColor = CopyTextureAsset(
                state.lightmapColor,
                label,
                powerLevel,
                $"{doorwayToken}_Color");
            Texture2D stableDirection = CopyTextureAsset(
                state.lightmapDirection,
                label,
                powerLevel,
                $"{doorwayToken}_Direction");
            extension.CaptureState(powerLevel, stableColor, stableDirection, state.lightmapScaleOffset);
            EditorUtility.SetDirty(extension);
            EditorSceneManager.MarkSceneDirty(extension.gameObject.scene);
            EditorSceneManager.SaveScene(extension.gameObject.scene);
            AssetDatabase.SaveAssets();
        }

        private static Texture2D CopyTextureAsset(
            Texture2D source,
            string label,
            DungeonTileLightmapSwitcher.PowerLevel powerLevel,
            string suffix)
        {
            if (source == null)
                return null;

            EnsureFolder(CapturedFolder);
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(extension))
                throw new InvalidOperationException($"Cannot persist non-asset lightmap texture '{source.name}'.");

            string destination = $"{CapturedFolder}/{label}_{powerLevel}_{suffix}{extension}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(destination) != null)
                AssetDatabase.DeleteAsset(destination);
            if (!AssetDatabase.CopyAsset(sourcePath, destination))
                throw new InvalidOperationException($"Failed to copy '{sourcePath}' to '{destination}'.");
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(destination);
        }

        private static void ApplyPowerState(
            Scene scene,
            DungeonTileLightmapSwitcher.PowerLevel powerLevel)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Light[] lights = roots[rootIndex].GetComponentsInChildren<Light>(true);
                for (int lightIndex = 0; lightIndex < lights.Length; lightIndex++)
                {
                    Light light = lights[lightIndex];
                    IgnoreLightControl marker = light.GetComponentInParent<IgnoreLightControl>(true);
                    light.enabled =
                        powerLevel == DungeonTileLightmapSwitcher.PowerLevel.P100 ||
                        (marker != null && marker.enabled);
                }

                Renderer[] renderers = roots[rootIndex].GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    Renderer renderer = renderers[rendererIndex];
                    IgnoreEmissionControl marker = renderer.GetComponentInParent<IgnoreEmissionControl>(true);
                    if (marker != null && marker.enabled)
                        continue;

                    Material[] materials = renderer.sharedMaterials;
                    bool changed = false;
                    for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    {
                        Material replacement = ResolvePowerMaterial(
                            materials[materialIndex],
                            powerLevel);
                        if (replacement == null || replacement == materials[materialIndex])
                            continue;
                        materials[materialIndex] = replacement;
                        changed = true;
                    }

                    if (changed)
                        renderer.sharedMaterials = materials;
                }
            }
        }

        private static Material ResolvePowerMaterial(
            Material current,
            DungeonTileLightmapSwitcher.PowerLevel powerLevel)
        {
            if (current == null)
                return null;

            string currentPath = AssetDatabase.GetAssetPath(current);
            for (int i = 0; i < EmissionVariants.Length; i++)
            {
                if (!string.Equals(currentPath, EmissionVariants[i].source, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(currentPath, EmissionVariants[i].power100, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(currentPath, EmissionVariants[i].power0, StringComparison.OrdinalIgnoreCase))
                    continue;

                string targetPath = powerLevel == DungeonTileLightmapSwitcher.PowerLevel.P0
                    ? EmissionVariants[i].power0
                    : EmissionVariants[i].power100;
                return AssetDatabase.LoadAssetAtPath<Material>(targetPath);
            }
            return current;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
                return;

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string name = Path.GetFileName(assetFolder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new InvalidOperationException($"Invalid asset folder '{assetFolder}'.");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static DungeonAdjacentLightmapExtension[] FindExtensionsInScene(
            DungeonAdjacentLightmapExtension[] extensions,
            Scene scene)
        {
            var result = new List<DungeonAdjacentLightmapExtension>();
            for (int i = 0; i < extensions.Length; i++)
            {
                if (extensions[i] != null && extensions[i].gameObject.scene == scene)
                    result.Add(extensions[i]);
            }
            result.Sort((left, right) => string.CompareOrdinal(left.DoorwayId, right.DoorwayId));
            return result.ToArray();
        }

        private struct CapturedAtlas
        {
            public Texture2D color;
            public Texture2D direction;
        }
    }
}
