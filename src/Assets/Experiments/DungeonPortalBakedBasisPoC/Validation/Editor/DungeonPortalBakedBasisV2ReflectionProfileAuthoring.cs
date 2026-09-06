using System;
using System.Collections.Generic;
using DungeonPortalTransportPoC;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonPortalBakedBasisPoC.Validation.Editor
{
    /// <summary>
    /// Publishes validation-owned reflection profiles from the exact V2 canonical P0/P100
    /// BakeData. Production bake assets and the older PortalTransport profiles are read-only.
    /// </summary>
    public static class DungeonPortalBakedBasisV2ReflectionProfileAuthoring
    {
        public const string OutputRoot =
            "Assets/Experiments/DungeonPortalBakedBasisPoC/Data/ReflectionV2";
        public const string StartProfilePath =
            OutputRoot + "/StartRoom_R000_ReflectionProfile.asset";

        private const string StartP0Path =
            "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/Canonical/BakedData/" +
            "StartRoom_R000/P0/StartRoom_R000_BakeData.asset";
        private const string StartP100Path =
            "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/Canonical/BakedData/" +
            "StartRoom_R000/P100/StartRoom_R000_BakeData.asset";
        private const string AdministrativeP0Path =
            "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/Canonical/" +
            "BakedData/AdminstrativeSegregation_R000/P0/" +
            "AdminstrativeSegregation_R000_BakeData.asset";
        private const string AdministrativeP100Path =
            "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/Canonical/" +
            "BakedData/AdminstrativeSegregation_R000/P100/" +
            "AdminstrativeSegregation_R000_BakeData.asset";

        public static string AdministrativeProfilePath(int index)
        {
            if (index < 0 || index >= 4)
                throw new ArgumentOutOfRangeException(nameof(index));
            return OutputRoot + "/AdminstrativeSegregation_R000_S0" + index +
                   "_ReflectionProfile.asset";
        }

        [MenuItem("Tools/Dungeon/Lighting/Portal Baked Basis PoC/Author V2 Reflection Profiles")]
        public static void BuildFromMenu()
        {
            string result = Build();
            if (result.StartsWith("PASS", StringComparison.Ordinal)) Debug.Log(result);
            else Debug.LogError(result);
        }

        public static void BuildCli()
        {
            string result = Build();
            if (!result.StartsWith("PASS", StringComparison.Ordinal))
                throw new InvalidOperationException(result);
            Debug.Log(result);
        }

        public static string Build()
        {
            var created = new List<string>();
            try
            {
                RequireCleanEditMode();
                EnsureFolder(OutputRoot);
                string[] outputs =
                {
                    StartProfilePath,
                    AdministrativeProfilePath(0),
                    AdministrativeProfilePath(1),
                    AdministrativeProfilePath(2),
                    AdministrativeProfilePath(3)
                };
                for (int i = 0; i < outputs.Length; i++)
                {
                    if (AssetDatabase.LoadMainAssetAtPath(outputs[i]) != null)
                        throw new InvalidOperationException(
                            "V2 reflection output already exists; refusing to overwrite '" +
                            outputs[i] + "'.");
                }

                DungeonTileBakeData startP0 = LoadBake(StartP0Path);
                DungeonTileBakeData startP100 = LoadBake(StartP100Path);
                DungeonTileBakeData adminP0 = LoadBake(AdministrativeP0Path);
                DungeonTileBakeData adminP100 = LoadBake(AdministrativeP100Path);
                Dictionary<string, CubemapPair> startPairs = BuildPairs(startP0, startP100);
                Dictionary<string, CubemapPair> adminPairs = BuildPairs(adminP0, adminP100);
                if (startPairs.Count != 1 || adminPairs.Count != 4 ||
                    !startPairs.TryGetValue("Tile Reflection Probe", out CubemapPair start))
                {
                    throw new InvalidOperationException(
                        "V2 reflection BakeData must expose one Start and four Admin probe pairs.");
                }

                CreateProfile(StartProfilePath, start, created);
                for (int i = 0; i < 4; i++)
                {
                    string key = "Tile Reflection Probe_S0" + i;
                    if (!adminPairs.TryGetValue(key, out CubemapPair pair))
                        throw new InvalidOperationException("Missing V2 reflection pair '" + key + "'.");
                    CreateProfile(AdministrativeProfilePath(i), pair, created);
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                return "PASS authored five validation-owned V2 P0-residual/P100 reflection " +
                       "profiles; productionAssetsWritten=false.";
            }
            catch (Exception exception)
            {
                for (int i = created.Count - 1; i >= 0; i--)
                    AssetDatabase.DeleteAsset(created[i]);
                return "FAIL V2 reflection profile authoring: " + exception.Message;
            }
        }

        private static Dictionary<string, CubemapPair> BuildPairs(
            DungeonTileBakeData p0,
            DungeonTileBakeData p100)
        {
            var p0ByKey = SelectVariant(p0, "_P0");
            var p100ByKey = SelectVariant(p100, "_P100");
            if (p0ByKey.Count != p100ByKey.Count)
                throw new InvalidOperationException("V2 P0/P100 reflection-pair counts differ.");
            var result = new Dictionary<string, CubemapPair>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Cubemap> pair in p0ByKey)
            {
                if (!p100ByKey.TryGetValue(pair.Key, out Cubemap power100) ||
                    pair.Value == null || power100 == null || pair.Value == power100)
                {
                    throw new InvalidOperationException(
                        "V2 reflection pair is incomplete at '" + pair.Key + "'.");
                }
                result.Add(pair.Key, new CubemapPair(pair.Value, power100));
            }
            return result;
        }

        private static Dictionary<string, Cubemap> SelectVariant(
            DungeonTileBakeData bake,
            string suffix)
        {
            var result = new Dictionary<string, Cubemap>(StringComparer.Ordinal);
            DungeonTileBakeData.ReflectionProbeBakeEntry[] entries =
                bake.reflectionProbeEntries ?? Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>();
            for (int i = 0; i < entries.Length; i++)
            {
                string path = entries[i].relativePath ?? string.Empty;
                if (!path.EndsWith(suffix, StringComparison.Ordinal))
                    continue;
                string key = path.Substring(0, path.Length - suffix.Length);
                if (string.IsNullOrWhiteSpace(key) || entries[i].bakedTexture == null ||
                    !result.TryAdd(key, entries[i].bakedTexture))
                {
                    throw new InvalidOperationException(
                        "V2 reflection BakeData has an invalid/duplicate '" + suffix + "' entry.");
                }
            }
            return result;
        }

        private static void CreateProfile(
            string path,
            CubemapPair pair,
            List<string> created)
        {
            DungeonPortalRoomReflectionProfile profile =
                ScriptableObject.CreateInstance<DungeonPortalRoomReflectionProfile>();
            profile.name = System.IO.Path.GetFileNameWithoutExtension(path);
            profile.Configure(pair.Power0, pair.Power100, 1f);
            if (!profile.TryValidate(out string failure))
            {
                UnityEngine.Object.DestroyImmediate(profile);
                throw new InvalidOperationException(
                    "V2 reflection profile validation failed for '" + path + "': " + failure);
            }
            AssetDatabase.CreateAsset(profile, path);
            created.Add(path);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        private static DungeonTileBakeData LoadBake(string path)
        {
            DungeonTileBakeData result = AssetDatabase.LoadAssetAtPath<DungeonTileBakeData>(path);
            if (result == null || EditorUtility.IsDirty(result))
                throw new InvalidOperationException("Missing or dirty V2 BakeData: '" + path + "'.");
            return result;
        }

        private static void RequireCleanEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || Lightmapping.isRunning)
            {
                throw new InvalidOperationException("Stable Edit Mode is required.");
            }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                    throw new InvalidOperationException("Loaded scene is dirty: '" + scene.path + "'.");
            }
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

        private readonly struct CubemapPair
        {
            internal readonly Cubemap Power0;
            internal readonly Cubemap Power100;

            internal CubemapPair(Cubemap power0, Cubemap power100)
            {
                Power0 = power0;
                Power100 = power100;
            }
        }
    }
}
