using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonAdjacentLightingPoC.Editor
{
    internal readonly struct DungeonAdjacentLightingPoCEditorState
    {
        private readonly Scene activeScene;
        private readonly bool activeSceneWasDirty;
        private readonly LightingDataAsset lightingDataAsset;
        private readonly LightingSettings lightingSettings;
        private readonly LightmapData[] lightmaps;
        private readonly LightmapsMode lightmapsMode;

        private DungeonAdjacentLightingPoCEditorState(
            Scene activeScene,
            bool activeSceneWasDirty,
            LightingDataAsset lightingDataAsset,
            LightingSettings lightingSettings,
            LightmapData[] lightmaps,
            LightmapsMode lightmapsMode)
        {
            this.activeScene = activeScene;
            this.activeSceneWasDirty = activeSceneWasDirty;
            this.lightingDataAsset = lightingDataAsset;
            this.lightingSettings = lightingSettings;
            this.lightmaps = lightmaps;
            this.lightmapsMode = lightmapsMode;
        }

        public static DungeonAdjacentLightingPoCEditorState Capture()
        {
            Scene scene = SceneManager.GetActiveScene();
            return new DungeonAdjacentLightingPoCEditorState(
                scene,
                scene.IsValid() && scene.isDirty,
                Lightmapping.lightingDataAsset,
                Lightmapping.lightingSettings,
                LightmapSettings.lightmaps,
                LightmapSettings.lightmapsMode);
        }

        public void Restore()
        {
            if (activeScene.IsValid() && activeScene.isLoaded)
                SceneManager.SetActiveScene(activeScene);

            if (Lightmapping.lightingSettings != lightingSettings)
                Lightmapping.lightingSettings = lightingSettings;
            if (Lightmapping.lightingDataAsset != lightingDataAsset)
                Lightmapping.lightingDataAsset = lightingDataAsset;
            LightmapSettings.lightmapsMode = lightmapsMode;
            LightmapSettings.lightmaps = lightmaps;

            if (!activeScene.IsValid() || !activeScene.isLoaded)
                return;

            if (activeScene.isDirty != activeSceneWasDirty)
            {
                Debug.LogError(
                    $"[DungeonAdjacentLightmapPoC] Active scene dirty state changed unexpectedly: " +
                    $"before={activeSceneWasDirty}, after={activeScene.isDirty}");
            }
        }
    }
}
