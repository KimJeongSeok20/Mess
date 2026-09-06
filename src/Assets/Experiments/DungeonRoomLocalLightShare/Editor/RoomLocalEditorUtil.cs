using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DungeonRoomLocalLightShare.Editor
{
    public static class RoomLocalEditorUtil
    {
        public static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder))
                return;

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string name = Path.GetFileName(assetFolder);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        public static T FindInScene<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T component = roots[i].GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }

            return null;
        }

        public static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == name)
                    return roots[i];
            }

            return null;
        }

        public static string RequireIdleEditor()
        {
            if (Application.isPlaying)
                return "FAIL: exit Play Mode first.";
            if (Lightmapping.isRunning)
                return "FAIL: a lightmap bake is already running.";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return "FAIL: Unity is compiling or updating.";
            return null;
        }

        public static void ApplyDungeonRenderingLayer(GameObject root)
        {
            int layerIndex = RenderingLayerMask.NameToRenderingLayer("Dungeon");
            if (layerIndex < 0)
                return;

            uint mask = RoomLocalRenderingLayers.EnvironmentMask;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].renderingLayerMask = mask;
        }
    }
}
