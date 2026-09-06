using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonAdjacentLightingPoC.Editor
{
    public static class DungeonAdjacentPairLightmapBaker
    {
        private const string RootFolder = "Assets/Experiments/DungeonAdjacentLightingPoC";
        private const string PairScenePath = RootFolder + "/Scenes/Start_Admin_PairReference.unity";
        private const string PairDataFolder = RootFolder + "/Generated/PairBake";
        private const string PairDataPath = PairDataFolder + "/Start_Admin_R000_PairBakeData.asset";
        private const string CapturedRoot = PairDataFolder + "/Captured";
        private const string ReportPath = PairDataFolder + "/Start_Admin_R000_PairBakeReport.txt";
        private const string StartRootName = "StartRoom_PairReference";
        private const string AdministrativeRootName = "AdminstrativeSegregation_PairReference";
        private const string DoorwayPath = "Doorways/Door_SM_A/DoorWayPoint";

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

        [MenuItem("Tools/Dungeon/Lighting/Adjacent Lightmap PoC/Bake Four-State Pair Reference")]
        public static void BakeAllFromMenu()
        {
            Debug.Log(BakeAllStates());
        }

        public static string BakeAllStates()
        {
            if (Lightmapping.isRunning)
                return "FAIL: another lightmap bake is already running.";
            if (EditorApplication.isPlaying)
                return "FAIL: exit Play Mode before pair baking.";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PairScenePath) == null)
                return $"FAIL: pair reference scene is missing: {PairScenePath}";

            EnsureFolder(PairDataFolder);
            EnsureFolder(CapturedRoot);
            string runFolder = CapturedRoot + "/Run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            EnsureFolder(runFolder);

            var accumulators = new Dictionary<string, RendererAccumulator>(StringComparer.Ordinal);
            var stateReports = new List<string>();
            DungeonAdjacentLightingPoCEditorState originalState =
                DungeonAdjacentLightingPoCEditorState.Capture();

            try
            {
                Array states = Enum.GetValues(typeof(DungeonAdjacentPairLightmapData.PairPowerState));
                foreach (DungeonAdjacentPairLightmapData.PairPowerState state in states)
                {
                    string stateReport = BakeAndCaptureState(state, runFolder, accumulators);
                    stateReports.Add(stateReport);
                    if (stateReport.StartsWith("FAIL", StringComparison.Ordinal))
                        return string.Join("\n", stateReports);
                }

                var entries = new List<DungeonAdjacentPairLightmapData.RendererStateSet>();
                foreach (RendererAccumulator accumulator in accumulators.Values)
                {
                    DungeonAdjacentPairLightmapData.RendererStateSet entry = accumulator.ToEntry();
                    if (entry.IsComplete)
                        entries.Add(entry);
                }
                entries.Sort(CompareEntries);

                DungeonAdjacentPairLightmapData data =
                    AssetDatabase.LoadAssetAtPath<DungeonAdjacentPairLightmapData>(PairDataPath);
                if (data == null)
                {
                    data = ScriptableObject.CreateInstance<DungeonAdjacentPairLightmapData>();
                    data.name = "Start_Admin_R000_PairBakeData";
                    AssetDatabase.CreateAsset(data, PairDataPath);
                }
                data.ConfigureAuthoring("StartRoom_R000__AdminstrativeSegregation_R000", entries.ToArray());
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();

                string report =
                    "PASS four-state Start/Admin pair bake\n" +
                    string.Join("\n", stateReports) + "\n" +
                    $"captureRun={runFolder}\n" +
                    $"pairData={PairDataPath}\n" +
                    $"completeRendererEntries={entries.Count}\n";
                File.WriteAllText(ReportPath, report);
                AssetDatabase.ImportAsset(ReportPath);
                return report;
            }
            catch (Exception exception)
            {
                return $"FAIL: four-state pair bake threw {exception}";
            }
            finally
            {
                originalState.Restore();
            }
        }

        private static string BakeAndCaptureState(
            DungeonAdjacentPairLightmapData.PairPowerState state,
            string runFolder,
            Dictionary<string, RendererAccumulator> accumulators)
        {
            Scene scene = default;
            try
            {
                scene = EditorSceneManager.OpenScene(PairScenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                GameObject start = FindRoot(scene, StartRootName);
                GameObject administrative = FindRoot(scene, AdministrativeRootName);
                if (start == null || administrative == null)
                    return $"FAIL {state}: pair scene roots are missing.";

                Transform startDoorway = start.transform.Find(DoorwayPath);
                Transform administrativeDoorway = administrative.transform.Find(DoorwayPath);
                if (startDoorway == null || administrativeDoorway == null)
                    return $"FAIL {state}: pair doorway paths are missing.";
                ConfigureConnectedDoorway(startDoorway);
                ConfigureConnectedDoorway(administrativeDoorway);

                ResolveStatePower(
                    state,
                    out DungeonTileLightmapSwitcher.PowerLevel startPower,
                    out DungeonTileLightmapSwitcher.PowerLevel administrativePower);
                ApplyPowerState(start, startPower);
                ApplyPowerState(administrative, administrativePower);

                Lightmapping.Clear();
                DateTime started = DateTime.UtcNow;
                if (!Lightmapping.Bake())
                    return $"FAIL {state}: Lightmapping.Bake returned false.";
                TimeSpan elapsed = DateTime.UtcNow - started;

                LightmapData[] lightmaps = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
                Texture2D[] stableColors = new Texture2D[lightmaps.Length];
                Texture2D[] stableDirections = new Texture2D[lightmaps.Length];
                string stateFolder = runFolder + "/" + state;
                EnsureFolder(stateFolder);
                for (int i = 0; i < lightmaps.Length; i++)
                {
                    if (lightmaps[i] == null)
                        continue;
                    stableColors[i] = CopyTextureAsset(
                        lightmaps[i].lightmapColor,
                        stateFolder,
                        $"Lightmap_{i}_Color");
                    stableDirections[i] = CopyTextureAsset(
                        lightmaps[i].lightmapDir,
                        stateFolder,
                        $"Lightmap_{i}_Direction");
                }

                int startCount = CaptureRoom(
                    start,
                    DungeonAdjacentPairLightmapData.RoomRole.Start,
                    state,
                    lightmaps,
                    stableColors,
                    stableDirections,
                    accumulators);
                int administrativeCount = CaptureRoom(
                    administrative,
                    DungeonAdjacentPairLightmapData.RoomRole.Administrative,
                    state,
                    lightmaps,
                    stableColors,
                    stableDirections,
                    accumulators);
                return
                    $"PASS {state}: elapsedSeconds={elapsed.TotalSeconds:F1}, " +
                    $"lightmaps={lightmaps.Length}, startReceivers={startCount}, " +
                    $"administrativeReceivers={administrativeCount}";
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static int CaptureRoom(
            GameObject room,
            DungeonAdjacentPairLightmapData.RoomRole role,
            DungeonAdjacentPairLightmapData.PairPowerState pairState,
            LightmapData[] lightmaps,
            Texture2D[] stableColors,
            Texture2D[] stableDirections,
            Dictionary<string, RendererAccumulator> accumulators)
        {
            MeshRenderer[] renderers = room.GetComponentsInChildren<MeshRenderer>(true);
            var pathUseCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            int captured = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                string relativePath = AnimationUtility.CalculateTransformPath(
                    renderer.transform,
                    room.transform);
                pathUseCounts.TryGetValue(relativePath, out int bucketIndex);
                pathUseCounts[relativePath] = bucketIndex + 1;

                if (!DungeonAdjacentRendererUtility.TryGetEligibleMesh(
                        renderer,
                        out MeshFilter filter,
                        out Mesh mesh))
                    continue;

                int lightmapIndex = renderer.lightmapIndex;
                if (lightmapIndex < 0 || lightmapIndex >= lightmaps.Length ||
                    lightmapIndex >= stableColors.Length || stableColors[lightmapIndex] == null)
                    continue;

                string key = BuildKey(role, relativePath, bucketIndex);
                if (!accumulators.TryGetValue(key, out RendererAccumulator accumulator))
                {
                    accumulator = new RendererAccumulator(
                        role,
                        relativePath,
                        bucketIndex,
                        mesh.vertexCount);
                    accumulators.Add(key, accumulator);
                }

                accumulator.SetState(
                    pairState,
                    new DungeonAdjacentPairLightmapData.LightmapState
                    {
                        lightmapColor = stableColors[lightmapIndex],
                        lightmapDirection = lightmapIndex < stableDirections.Length
                            ? stableDirections[lightmapIndex]
                            : null,
                        lightmapScaleOffset = renderer.lightmapScaleOffset
                    });
                captured++;
            }
            return captured;
        }

        private static void ApplyPowerState(
            GameObject root,
            DungeonTileLightmapSwitcher.PowerLevel powerLevel)
        {
            Light[] lights = root.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                IgnoreLightControl marker = light.GetComponentInParent<IgnoreLightControl>(true);
                if (marker != null && marker.enabled)
                    continue;
                light.enabled = powerLevel == DungeonTileLightmapSwitcher.PowerLevel.P100;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                IgnoreEmissionControl marker = renderer.GetComponentInParent<IgnoreEmissionControl>(true);
                if (marker != null && marker.enabled)
                    continue;

                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material replacement = ResolvePowerMaterial(materials[materialIndex], powerLevel);
                    if (replacement == null || replacement == materials[materialIndex])
                        continue;
                    materials[materialIndex] = replacement;
                    changed = true;
                }
                if (changed)
                    renderer.sharedMaterials = materials;
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
                string target = powerLevel == DungeonTileLightmapSwitcher.PowerLevel.P100
                    ? EmissionVariants[i].power100
                    : EmissionVariants[i].power0;
                return AssetDatabase.LoadAssetAtPath<Material>(target);
            }
            return current;
        }

        private static void ResolveStatePower(
            DungeonAdjacentPairLightmapData.PairPowerState state,
            out DungeonTileLightmapSwitcher.PowerLevel startPower,
            out DungeonTileLightmapSwitcher.PowerLevel administrativePower)
        {
            bool startOn = state == DungeonAdjacentPairLightmapData.PairPowerState.P100P0 ||
                           state == DungeonAdjacentPairLightmapData.PairPowerState.P100P100;
            bool administrativeOn = state == DungeonAdjacentPairLightmapData.PairPowerState.P0P100 ||
                                    state == DungeonAdjacentPairLightmapData.PairPowerState.P100P100;
            startPower = startOn
                ? DungeonTileLightmapSwitcher.PowerLevel.P100
                : DungeonTileLightmapSwitcher.PowerLevel.P0;
            administrativePower = administrativeOn
                ? DungeonTileLightmapSwitcher.PowerLevel.P100
                : DungeonTileLightmapSwitcher.PowerLevel.P0;
        }

        private static Texture2D CopyTextureAsset(Texture2D source, string folder, string name)
        {
            if (source == null)
                return null;
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(extension))
                throw new InvalidOperationException($"Cannot persist non-asset lightmap texture '{source.name}'.");
            string destination = folder + "/" + name + extension;
            if (!AssetDatabase.CopyAsset(sourcePath, destination))
                throw new InvalidOperationException($"Failed to copy '{sourcePath}' to '{destination}'.");
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(destination);
        }

        private static GameObject FindRoot(Scene scene, string rootName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == rootName)
                    return roots[i];
            }
            return null;
        }

        private static void ConfigureConnectedDoorway(Transform doorway)
        {
            Transform anchor = doorway.parent;
            Transform blocker = anchor != null ? anchor.Find("Blocker_SM_A") : null;
            Transform openPassage = anchor != null ? anchor.Find("No_Door_Placement") : null;
            if (blocker == null || openPassage == null)
                throw new InvalidOperationException($"Connected doorway geometry is missing under '{anchor?.name}'.");
            blocker.gameObject.SetActive(false);
            openPassage.gameObject.SetActive(true);
        }

        private static string BuildKey(
            DungeonAdjacentPairLightmapData.RoomRole role,
            string path,
            int bucketIndex)
        {
            return role + "|" + path + "|" + bucketIndex;
        }

        private static int CompareEntries(
            DungeonAdjacentPairLightmapData.RendererStateSet left,
            DungeonAdjacentPairLightmapData.RendererStateSet right)
        {
            int roomComparison = left.receiverRoom.CompareTo(right.receiverRoom);
            if (roomComparison != 0)
                return roomComparison;
            int pathComparison = string.CompareOrdinal(left.relativePath, right.relativePath);
            return pathComparison != 0
                ? pathComparison
                : left.rendererBucketIndex.CompareTo(right.rendererBucketIndex);
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

        private sealed class RendererAccumulator
        {
            private readonly DungeonAdjacentPairLightmapData.RoomRole roomRole;
            private readonly string relativePath;
            private readonly int rendererBucketIndex;
            private readonly int vertexCount;
            private DungeonAdjacentPairLightmapData.LightmapState p0p0;
            private DungeonAdjacentPairLightmapData.LightmapState p100p0;
            private DungeonAdjacentPairLightmapData.LightmapState p0p100;
            private DungeonAdjacentPairLightmapData.LightmapState p100p100;

            public RendererAccumulator(
                DungeonAdjacentPairLightmapData.RoomRole role,
                string path,
                int bucketIndex,
                int meshVertexCount)
            {
                roomRole = role;
                relativePath = path;
                rendererBucketIndex = bucketIndex;
                vertexCount = meshVertexCount;
            }

            public void SetState(
                DungeonAdjacentPairLightmapData.PairPowerState state,
                DungeonAdjacentPairLightmapData.LightmapState lightmapState)
            {
                switch (state)
                {
                    case DungeonAdjacentPairLightmapData.PairPowerState.P100P0:
                        p100p0 = lightmapState;
                        break;
                    case DungeonAdjacentPairLightmapData.PairPowerState.P0P100:
                        p0p100 = lightmapState;
                        break;
                    case DungeonAdjacentPairLightmapData.PairPowerState.P100P100:
                        p100p100 = lightmapState;
                        break;
                    default:
                        p0p0 = lightmapState;
                        break;
                }
            }

            public DungeonAdjacentPairLightmapData.RendererStateSet ToEntry()
            {
                return new DungeonAdjacentPairLightmapData.RendererStateSet
                {
                    receiverRoom = roomRole,
                    relativePath = relativePath,
                    rendererBucketIndex = rendererBucketIndex,
                    vertexCount = vertexCount,
                    p0p0 = p0p0,
                    p100p0 = p100p0,
                    p0p100 = p0p100,
                    p100p100 = p100p100
                };
            }
        }
    }
}
