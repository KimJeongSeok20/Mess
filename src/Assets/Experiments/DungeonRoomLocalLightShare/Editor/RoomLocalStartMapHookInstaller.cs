using System;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonRoomLocalLightShare.Editor
{
    public static class RoomLocalStartMapHookInstaller
    {
        public static string Install()
        {
            string idle = RoomLocalEditorUtil.RequireIdleEditor();
            if (idle != null)
                return idle;

            Scene startMap = GetOrOpenStartMap(out bool openedAdditive);
            if (!startMap.IsValid())
                return "FAIL: could not open StartMap.";

            try
            {
                RuntimeDungeon dungeon = RoomLocalEditorUtil.FindInScene<RuntimeDungeon>(startMap);
                if (dungeon == null)
                    return "FAIL: StartMap has no RuntimeDungeon.";

                RoomLocalMatrixCatalog catalog =
                    AssetDatabase.LoadAssetAtPath<RoomLocalMatrixCatalog>(
                        RoomLocalLightShareContract.MatrixCatalogPath);
                if (catalog == null)
                    return "FAIL: matrix catalog is missing at " +
                           RoomLocalLightShareContract.MatrixCatalogPath;

                SuppressGrokHook(startMap);

                GameObject existing = FindHook(startMap);
                if (existing != null)
                    UnityEngine.Object.DestroyImmediate(existing);

                GameObject prefab = CreateOrUpdateHookPrefab(catalog);
                GameObject hook = (GameObject)PrefabUtility.InstantiatePrefab(prefab, startMap);
                hook.name = RoomLocalLightShareContract.StartMapHookObjectName;
                hook.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var installer = hook.GetComponent<RoomLocalStartMapInstaller>();
                installer.Configure(dungeon, catalog);
                EditorUtility.SetDirty(hook);
                EditorSceneManager.MarkSceneDirty(startMap);
                if (!EditorSceneManager.SaveScene(startMap))
                    return "FAIL: StartMap save failed after adding the hook.";

                return
                    "PASS installed removable StartMap hook\n" +
                    "object=" + RoomLocalLightShareContract.StartMapHookObjectName + "\n" +
                    "prefab=" + RoomLocalLightShareContract.StartMapHookPrefabPath + "\n" +
                    "catalog=" + RoomLocalLightShareContract.MatrixCatalogPath + "\n" +
                    "grokHook=disabled\n" +
                    "bounce=off (cookie + door probe only)\n" +
                    "pairBake=false\n" +
                    "productionPrefabsModified=false\n" +
                    "remove=Hierarchy에서 '" + RoomLocalLightShareContract.StartMapHookObjectName +
                    "' 를 지우면 됨";
            }
            catch (Exception exception)
            {
                return "FAIL: " + exception;
            }
            finally
            {
                if (openedAdditive && startMap.IsValid() && startMap.isLoaded)
                {
                    // Keep StartMap open so the user can see the hook. Do not close the last scene.
                }
            }
        }

        public static string Remove()
        {
            string idle = RoomLocalEditorUtil.RequireIdleEditor();
            if (idle != null)
                return idle;

            Scene startMap = GetOrOpenStartMap(out _);
            if (!startMap.IsValid())
                return "FAIL: could not open StartMap.";

            GameObject existing = FindHook(startMap);
            if (existing == null)
                return "PASS StartMap hook was already absent.";

            UnityEngine.Object.DestroyImmediate(existing);
            EditorSceneManager.MarkSceneDirty(startMap);
            if (!EditorSceneManager.SaveScene(startMap))
                return "FAIL: StartMap save failed after removing the hook.";

            return "PASS removed " + RoomLocalLightShareContract.StartMapHookObjectName +
                   " from StartMap.";
        }

        private static GameObject CreateOrUpdateHookPrefab(RoomLocalMatrixCatalog catalog)
        {
            RoomLocalEditorUtil.EnsureFolder(RoomLocalLightShareContract.OwnedRoot);
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(
                RoomLocalLightShareContract.StartMapHookPrefabPath);
            GameObject contents = existing != null
                ? PrefabUtility.LoadPrefabContents(RoomLocalLightShareContract.StartMapHookPrefabPath)
                : new GameObject(RoomLocalLightShareContract.StartMapHookObjectName);

            contents.name = RoomLocalLightShareContract.StartMapHookObjectName;
            var installer = contents.GetComponent<RoomLocalStartMapInstaller>();
            if (installer == null)
                installer = contents.AddComponent<RoomLocalStartMapInstaller>();
            installer.Configure(null, catalog);

            GameObject prefab;
            if (existing != null)
            {
                PrefabUtility.SaveAsPrefabAsset(
                    contents,
                    RoomLocalLightShareContract.StartMapHookPrefabPath);
                PrefabUtility.UnloadPrefabContents(contents);
                prefab = existing;
            }
            else
            {
                prefab = PrefabUtility.SaveAsPrefabAsset(
                    contents,
                    RoomLocalLightShareContract.StartMapHookPrefabPath);
                UnityEngine.Object.DestroyImmediate(contents);
            }

            return prefab;
        }

        private static void SuppressGrokHook(Scene scene)
        {
            GameObject grok = FindNamed(scene, RoomLocalLightShareContract.GrokDoorwayStartMapHookObjectName);
            if (grok == null || !grok.activeSelf)
                return;

            grok.SetActive(false);
            EditorUtility.SetDirty(grok);
        }

        private static Scene GetOrOpenStartMap(out bool openedAdditive)
        {
            openedAdditive = false;
            Scene loaded = SceneManager.GetSceneByPath(RoomLocalLightShareContract.StartMapScenePath);
            if (loaded.IsValid() && loaded.isLoaded)
                return loaded;

            openedAdditive = true;
            return EditorSceneManager.OpenScene(
                RoomLocalLightShareContract.StartMapScenePath,
                OpenSceneMode.Additive);
        }

        private static GameObject FindHook(Scene scene)
        {
            return FindNamed(scene, RoomLocalLightShareContract.StartMapHookObjectName);
        }

        private static GameObject FindNamed(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].name == name)
                    return roots[i];
                if (roots[i] == null)
                    continue;
                Transform child = roots[i].transform.Find(name);
                if (child != null)
                    return child.gameObject;
            }

            return null;
        }
    }
}
