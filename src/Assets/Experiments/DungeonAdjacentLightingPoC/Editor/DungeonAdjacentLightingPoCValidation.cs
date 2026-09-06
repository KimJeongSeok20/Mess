using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonAdjacentLightingPoC.Editor
{
    public static class DungeonAdjacentLightingPoCValidation
    {
        private const string ShaderName = "StillWorking/Experiments/Dungeon Adjacent Lightmap Lit";

        private static readonly KeyValuePair<string, string>[] ProductionBaselines =
        {
            Pair("Assets/Scripts/Dungeon 1/Lighting/DungeonTileBakeData.cs", "7E85B79CB34BFCC84A5BC4D40C83D9B1D1518B7ACDA0E7FF751851CEB4A31A6F"),
            Pair("Assets/Scripts/Dungeon 1/Lighting/DungeonTileLightmapSwitcher.cs", "6249FCD633781A91B9276A23935B06FECE4CA61AA663C6681D567482B32729D8"),
            Pair("Assets/Scripts/Dungeon 1/Lighting/DungeonTileProbeRegistry.cs", "A04E5621786258623B1A7452384A6D0A5934D62D71546C98EFF2BA632ADCFAAA"),
            Pair("Assets/Scripts/Dungeon 1/Lighting/DungeonDynamicProbeReceiver.cs", "3C54AED352679F5A27EA930E2093C90DDF0613C3636BA37E21B21CE92E117CF9"),
            Pair("Assets/Scripts/Dungeon 1/DungeonMapList.asset", "17E431A7C5BBD1389C1539F67A5DBF914A89322E13A563FEA94F9D6FB954B069"),
            Pair("Assets/Prefabs/map_piece/NewPrison/New_Prison_Flow.asset", "04BD1DDC9AEECB37DA22A211CCD29DE8A6846DE654BCA7550041245438761718")
        };

        [MenuItem("Tools/Dungeon/Lighting/Adjacent Lightmap PoC/Validate Import And Isolation")]
        public static void ValidateFromMenu()
        {
            string report = Report();
            if (report.StartsWith("PASS"))
                Debug.Log(report);
            else
                Debug.LogError(report);
        }

        public static string Report()
        {
            var failures = new List<string>();

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                failures.Add($"Shader '{ShaderName}' was not found.");
            }
            else
            {
                ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
                for (int i = 0; i < messages.Length; i++)
                {
                    if (messages[i].severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        failures.Add($"Shader error: {messages[i].message}");
                }
            }

            for (int i = 0; i < ProductionBaselines.Length; i++)
            {
                KeyValuePair<string, string> baseline = ProductionBaselines[i];
                string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", baseline.Key));
                if (!File.Exists(absolutePath))
                {
                    failures.Add($"Missing production baseline file: {baseline.Key}");
                    continue;
                }

                string actualHash = ComputeSha256(absolutePath);
                if (!string.Equals(actualHash, baseline.Value, System.StringComparison.OrdinalIgnoreCase))
                    failures.Add($"Production drift: {baseline.Key} expected={baseline.Value} actual={actualHash}");
            }

            return failures.Count == 0
                ? $"PASS DungeonAdjacentLightingPoC shader={ShaderName} productionBaselines={ProductionBaselines.Length}"
                : "FAIL DungeonAdjacentLightingPoC\n" + string.Join("\n", failures);
        }

        // Narrow repair for a Unity Test Runner side effect observed during this PoC:
        // SaveModifiedSceneTask can save the active dirty scene before EditMode tests.
        // Refuse every state except the exact known StartMap -> PoC Admin reference.
        public static string RestoreStartMapLightingDataReference()
        {
            const string startMapScenePath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
            const string expectedPath = "Assets/SceneTemplateAssets/Scenes/StartMap/LightingData.asset";
            const string accidentalPath =
                "Assets/Experiments/DungeonAdjacentLightingPoC/Scenes/" +
                "Admin_Doorways_Door_SM_A_DoorWayPoint_ExtensionBake/LightingData.asset";

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.path != startMapScenePath)
                return $"FAIL: active scene is '{activeScene.path}', expected '{startMapScenePath}'.";
            if (activeScene.isDirty)
                return "FAIL: StartMap is dirty; refusing to save over unsaved work.";

            string currentPath = AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset);
            if (currentPath == expectedPath)
                return $"PASS StartMap lighting data already restored path={expectedPath}";
            if (currentPath != accidentalPath)
                return $"FAIL: unexpected current LightingData path '{currentPath}'.";

            LightingDataAsset expected = AssetDatabase.LoadAssetAtPath<LightingDataAsset>(expectedPath);
            if (expected == null)
                return $"FAIL: expected StartMap LightingData is missing: {expectedPath}";

            Lightmapping.lightingDataAsset = expected;
            EditorSceneManager.MarkSceneDirty(activeScene);
            if (!EditorSceneManager.SaveScene(activeScene))
                return "FAIL: Unity did not save repaired StartMap.";

            string restoredPath = AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset);
            string absoluteScenePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", startMapScenePath));
            string sceneText = File.ReadAllText(absoluteScenePath);
            if (restoredPath != expectedPath ||
                !sceneText.Contains("166de4165d2644842a9b763b3d027f87") ||
                sceneText.Contains("8047f4e31ba1e9840b61ce0420391674"))
            {
                return $"FAIL: StartMap save did not serialize the expected LightingData. path={restoredPath}";
            }

            return
                "PASS restored StartMap LightingData reference\n" +
                $"path={expectedPath}\n" +
                $"sceneSha256={ComputeSha256(absoluteScenePath)}\n" +
                $"dirty={activeScene.isDirty}";
        }

        public static string CreateStartMapRecoverySnapshot()
        {
            const string startMapScenePath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
            const string expectedLightingPath =
                "Assets/SceneTemplateAssets/Scenes/StartMap/LightingData.asset";
            const string snapshotPath =
                "Docs/Recovery/Snapshots/DungeonAdjacentLightingPoC_20260813/StartMap.unity.bak";

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.path != startMapScenePath || activeScene.isDirty)
                return $"FAIL: StartMap must be active and clean. path={activeScene.path} dirty={activeScene.isDirty}";
            if (AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset) != expectedLightingPath)
                return "FAIL: StartMap does not reference its expected LightingData asset.";

            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", startMapScenePath));
            string destination = Path.GetFullPath(Path.Combine(Application.dataPath, "..", snapshotPath));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(source, destination, true);

            string sourceHash = ComputeSha256(source);
            string snapshotHash = ComputeSha256(destination);
            if (!string.Equals(sourceHash, snapshotHash, System.StringComparison.OrdinalIgnoreCase))
                return $"FAIL: recovery snapshot hash mismatch source={sourceHash} snapshot={snapshotHash}";

            return
                "PASS StartMap recovery snapshot\n" +
                $"source={startMapScenePath}\n" +
                $"snapshot={snapshotPath}\n" +
                $"sha256={sourceHash}";
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(stream);
                return System.BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private static KeyValuePair<string, string> Pair(string path, string hash)
        {
            return new KeyValuePair<string, string>(path, hash);
        }
    }
}
