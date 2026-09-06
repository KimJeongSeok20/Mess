using System;
using System.Collections.Generic;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrokDoorwayLighting.Editor
{
    public static class GrokDoorwayStartMapHookInstaller
    {
        public const string StartMapScenePath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
        public const string HookPrefabPath =
            "Assets/Experiments/GrokDoorwayLighting/GrokDoorwayLighting_StartMapHook.prefab";

        public static string Install()
        {
            if (Application.isPlaying)
                return "FAIL: stop Play Mode before installing the StartMap hook.";

            Scene startMap = GetOrOpenStartMap(out _);
            if (!startMap.IsValid())
                return "FAIL: could not open StartMap.";

            try
            {
                GameObject existing = FindHook(startMap);
                if (existing != null)
                    UnityEngine.Object.DestroyImmediate(existing);

                RuntimeDungeon dungeon = FindInScene<RuntimeDungeon>(startMap);
                if (dungeon == null)
                    return "FAIL: StartMap has no RuntimeDungeon.";

                GameObject prefab = CreateOrUpdateHookPrefab();
                GameObject hook = (GameObject)PrefabUtility.InstantiatePrefab(prefab, startMap);
                hook.name = GrokDoorwayStartMapInstaller.HookObjectName;
                hook.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var installer = hook.GetComponent<GrokDoorwayStartMapInstaller>();
                installer.Configure(dungeon, LoadPreauthoredMaps());
                EditorUtility.SetDirty(hook);
                EditorSceneManager.MarkSceneDirty(startMap);
                if (!EditorSceneManager.SaveScene(startMap))
                    return "FAIL: StartMap save failed after adding the hook.";

                return
                    "PASS installed removable StartMap hook\n" +
                    "object=" + GrokDoorwayStartMapInstaller.HookObjectName + "\n" +
                    "prefab=" + HookPrefabPath + "\n" +
                    "bake=not required (uses existing P100/P0 tile lightmaps)\n" +
                    "remove=Hierarchy에서 그 오브젝트를 지우면 됨";
            }
            catch (Exception exception)
            {
                return "FAIL: " + exception;
            }
        }

        private static GameObject CreateOrUpdateHookPrefab()
        {
            GrokDoorwaySceneBuilder.EnsureFolder("Assets/Experiments/GrokDoorwayLighting");
            List<GrokDoorwayPortalMap> maps = LoadPreauthoredMaps();

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(HookPrefabPath);
            GameObject contents = existing != null
                ? PrefabUtility.LoadPrefabContents(HookPrefabPath)
                : new GameObject(GrokDoorwayStartMapInstaller.HookObjectName);

            contents.name = GrokDoorwayStartMapInstaller.HookObjectName;
            var installer = contents.GetComponent<GrokDoorwayStartMapInstaller>();
            if (installer == null)
                installer = contents.AddComponent<GrokDoorwayStartMapInstaller>();
            installer.Configure(null, maps);

            GameObject prefab;
            if (existing != null)
            {
                PrefabUtility.SaveAsPrefabAsset(contents, HookPrefabPath);
                PrefabUtility.UnloadPrefabContents(contents);
                prefab = existing;
            }
            else
            {
                prefab = PrefabUtility.SaveAsPrefabAsset(contents, HookPrefabPath);
                UnityEngine.Object.DestroyImmediate(contents);
            }

            return prefab;
        }

        public static string Remove()
        {
            if (Application.isPlaying)
                return "FAIL: stop Play Mode before removing the StartMap hook.";

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

            return "PASS removed " + GrokDoorwayStartMapInstaller.HookObjectName + " from StartMap.";
        }

        private static List<GrokDoorwayPortalMap> LoadPreauthoredMaps()
        {
            return new List<GrokDoorwayPortalMap>
            {
                AssetDatabase.LoadAssetAtPath<GrokDoorwayPortalMap>(GrokDoorwayPortalMapBaker.StartPortalPath),
                AssetDatabase.LoadAssetAtPath<GrokDoorwayPortalMap>(GrokDoorwayPortalMapBaker.AdminPortalPath)
            };
        }

        private static Scene GetOrOpenStartMap(out bool openedAdditive)
        {
            openedAdditive = false;
            Scene loaded = SceneManager.GetSceneByPath(StartMapScenePath);
            if (loaded.IsValid() && loaded.isLoaded)
                return loaded;

            openedAdditive = true;
            return EditorSceneManager.OpenScene(StartMapScenePath, OpenSceneMode.Additive);
        }

        private static GameObject FindHook(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].name == GrokDoorwayStartMapInstaller.HookObjectName)
                    return roots[i];

                if (roots[i] == null)
                    continue;
                Transform child = roots[i].transform.Find(GrokDoorwayStartMapInstaller.HookObjectName);
                if (child != null)
                    return child.gameObject;
            }

            return null;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                    continue;
                T found = roots[i].GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
