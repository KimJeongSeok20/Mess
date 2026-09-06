using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DungeonAdjacentLightingPoC
{
    [DefaultExecutionOrder(-32000)]
    public sealed class DungeonAdjacentLightingPoCStartMapEnvironmentController : MonoBehaviour
    {
        [SerializeField] private DungeonZoneManager zoneManager;
        [SerializeField] private Volume dungeonPostProcessVolume;
        [SerializeField] private Light dungeonOnlyLight;

        public bool IsApplied { get; private set; }
        public string VolumeProfileName =>
            dungeonPostProcessVolume != null && dungeonPostProcessVolume.sharedProfile != null
                ? dungeonPostProcessVolume.sharedProfile.name
                : "MISSING";
        public bool VolumeEnabled =>
            dungeonPostProcessVolume != null && dungeonPostProcessVolume.enabled;
        public bool DungeonOnlyLightEnabled =>
            dungeonOnlyLight != null && dungeonOnlyLight.enabled;

        public void Configure(
            DungeonZoneManager startMapZoneManager,
            Volume postProcessVolume,
            Light playerDungeonOnlyLight)
        {
            zoneManager = startMapZoneManager;
            dungeonPostProcessVolume = postProcessVolume;
            dungeonOnlyLight = playerDungeonOnlyLight;
        }

        private void Awake()
        {
            // The generated validation scene copies StartMap's dungeon rendering
            // environment. When StartMap is also loaded in the editor, keep its
            // unrelated gameplay systems from starting inside this isolated proof.
            // These active-state changes exist only for the current Play session.
            Scene validationScene = gameObject.scene;
            if (validationScene.name != "Start_Admin_WiredRuntimePreview")
                return;

            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene loadedScene = SceneManager.GetSceneAt(sceneIndex);
                if (!loadedScene.IsValid() || !loadedScene.isLoaded || loadedScene == validationScene)
                    continue;

                GameObject[] roots = loadedScene.GetRootGameObjects();
                for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                    roots[rootIndex].SetActive(false);
            }
        }

        private void Start()
        {
            if (dungeonPostProcessVolume != null)
                dungeonPostProcessVolume.enabled = true;
            if (dungeonOnlyLight != null)
                dungeonOnlyLight.enabled = false;

            zoneManager?.EnterDungeon(transform);
            IsApplied = zoneManager != null;
        }
    }
}
