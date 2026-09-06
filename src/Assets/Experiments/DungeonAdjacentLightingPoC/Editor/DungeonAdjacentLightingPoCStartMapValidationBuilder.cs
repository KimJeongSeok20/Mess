using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonAdjacentLightingPoC.Editor
{
    public static class DungeonAdjacentLightingPoCStartMapValidationBuilder
    {
        private const string StartMapScenePath =
            "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
        private const string PreviewScenePath =
            "Assets/Experiments/DungeonAdjacentLightingPoC/Scenes/Start_Admin_WiredRuntimePreview.unity";
        private const string ValidationScenePath =
            "Assets/Experiments/DungeonAdjacentLightingPoC/Scenes/Start_Admin_StartMapRuntimeValidation.unity";
        private const string ReportPath =
            "Assets/Experiments/DungeonAdjacentLightingPoC/Generated/StartMapRuntimeValidationReport.txt";
        private const string StartRoomName = "StartRoom_AdjacentLightmapPreview";
        private const string AdminRoomName = "AdminstrativeSegregation_AdjacentLightmapPreview";
        private const string StartDoorwayPath = "Doorways/Door_SM_A/DoorWayPoint";

        [MenuItem("Tools/Dungeon/Lighting/Adjacent Lightmap PoC/Open Actual StartMap Runtime Validation")]
        public static void OpenFromMenu()
        {
            Debug.Log(BuildAndOpenForValidation());
        }

        public static string BuildAndOpenForValidation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "FAIL: stop Play Mode before building the StartMap validation scene.";

            Scene startMapScene = SceneManager.GetSceneByPath(StartMapScenePath);
            if (!startMapScene.IsValid() || !startMapScene.isLoaded)
                return "FAIL: the existing StartMap scene must already be loaded; it will not be reopened automatically.";

            bool startMapWasDirty = startMapScene.isDirty;
            Scene validationScene = default;
            try
            {
                string previewReport = DungeonAdjacentLightingPoCSceneBuilder.BuildWiredRuntimePreview();
                if (previewReport.StartsWith("FAIL", StringComparison.Ordinal))
                    return previewReport;

                Scene loadedValidation = SceneManager.GetSceneByPath(ValidationScenePath);
                if (loadedValidation.IsValid() && loadedValidation.isLoaded &&
                    !EditorSceneManager.CloseScene(loadedValidation, true))
                {
                    return $"FAIL: unable to close existing generated validation scene '{ValidationScenePath}'.";
                }

                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ValidationScenePath) != null &&
                    !AssetDatabase.DeleteAsset(ValidationScenePath))
                {
                    return $"FAIL: unable to replace generated validation scene '{ValidationScenePath}'.";
                }

                if (!AssetDatabase.CopyAsset(PreviewScenePath, ValidationScenePath))
                    return $"FAIL: unable to copy '{PreviewScenePath}' to the validation scene.";

                AssetDatabase.ImportAsset(ValidationScenePath, ImportAssetOptions.ForceSynchronousImport);
                validationScene = EditorSceneManager.OpenScene(ValidationScenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(validationScene);

                RemovePreviewOnlyObjects(validationScene);

                GameObject startRoom = FindRoot(validationScene, StartRoomName);
                GameObject adminRoom = FindRoot(validationScene, AdminRoomName);
                if (startRoom == null || adminRoom == null)
                    return "FAIL: copied validation scene is missing the Start/Admin room roots.";

                GameObject pairRoot = new GameObject("StartMap_AdjacentLightingValidationPair");
                SceneManager.MoveGameObjectToScene(pairRoot, validationScene);
                startRoom.transform.SetParent(pairRoot.transform, true);
                adminRoom.transform.SetParent(pairRoot.transform, true);

                Transform startDoorway = startRoom.transform.Find(StartDoorwayPath);
                DungeonTileLightmapSwitcher startLighting =
                    startRoom.GetComponentInChildren<DungeonTileLightmapSwitcher>(true);
                DungeonTileLightmapSwitcher adminLighting =
                    adminRoom.GetComponentInChildren<DungeonTileLightmapSwitcher>(true);
                if (startDoorway == null || startLighting == null || adminLighting == null)
                    return "FAIL: validation room doorway or lightmap switcher is missing.";

                GameObject driverObject = new GameObject("StartMap_AdjacentLightingValidationRuntime");
                SceneManager.MoveGameObjectToScene(driverObject, validationScene);
                DungeonAdjacentLightingPoCPreviewController previewController =
                    driverObject.AddComponent<DungeonAdjacentLightingPoCPreviewController>();
                previewController.Configure(startLighting, adminLighting);
                DungeonAdjacentLightingPoCStartMapRuntimeValidationController runtimeController =
                    driverObject.AddComponent<DungeonAdjacentLightingPoCStartMapRuntimeValidationController>();
                runtimeController.Configure(
                    pairRoot,
                    startDoorway,
                    startLighting,
                    adminLighting,
                    previewController);
                previewController.ConfigureStartMapRuntimeValidation(runtimeController);

                pairRoot.SetActive(false);
                EditorSceneManager.MarkSceneDirty(validationScene);
                if (!EditorSceneManager.SaveScene(validationScene, ValidationScenePath, true))
                    return $"FAIL: unable to save '{ValidationScenePath}'.";

                SceneManager.SetActiveScene(startMapScene);
                AssetDatabase.SaveAssets();

                string report =
                    "PASS actual StartMap runtime validation scene\n" +
                    $"startMap={StartMapScenePath}\n" +
                    $"startMapDirtyBefore={startMapWasDirty}\n" +
                    $"startMapDirtyAfter={startMapScene.isDirty}\n" +
                    $"validationScene={ValidationScenePath}\n" +
                    "environmentSource=actual-StartMap-runtime\n" +
                    "playerSource=actual-StartMap-network-player\n" +
                    "dungeonGeneration=DebugRemoteControl.GenerateDungeon\n" +
                    "dungeonEntry=DebugRemoteControl.GoToRandomDungeonArea+DungeonZoneManager.EnterDungeon\n" +
                    "validationPair=experimental-StartRoom-R000+Admin-R000\n" +
                    "playerAttachedLights=disabled-in-Play-only\n" +
                    "initialState=StartP0/AdminP100/AdjacentON\n";
                File.WriteAllText(ReportPath, report);
                AssetDatabase.ImportAsset(ReportPath);
                return report;
            }
            catch (Exception exception)
            {
                return $"FAIL: StartMap runtime validation build threw {exception}";
            }
            finally
            {
                if (startMapScene.IsValid() && startMapScene.isLoaded)
                    SceneManager.SetActiveScene(startMapScene);
            }
        }

        private static void RemovePreviewOnlyObjects(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                    continue;

                bool ownsPreviewPlayer =
                    root.GetComponentInChildren<DungeonAdjacentLightingPoCPreviewController>(true) != null ||
                    root.GetComponentInChildren<DungeonAdjacentLightingPoCStartMapEnvironmentController>(true) != null;
                bool ownsCopiedZone = root.GetComponentInChildren<DungeonZoneManager>(true) != null;
                if (ownsPreviewPlayer || ownsCopiedZone)
                    UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root != null && root.name == name)
                    return root;
            }

            return null;
        }
    }
}
