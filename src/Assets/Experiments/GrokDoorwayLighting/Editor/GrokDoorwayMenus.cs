using UnityEditor;
using UnityEngine;

namespace GrokDoorwayLighting.Editor
{
    public static class GrokDoorwayMenus
    {
        [MenuItem("Tools/Grok Doorway Lighting/Build Isolated Start-Admin Scene")]
        public static void BuildScene()
        {
            Debug.Log(GrokDoorwaySceneBuilder.BuildIsolatedPreviewScene());
        }

        [MenuItem("Tools/Grok Doorway Lighting/Capture Portal Maps From Existing Bakes")]
        public static void CapturePortals()
        {
            Debug.Log(GrokDoorwayPortalMapBaker.CaptureBothRooms());
        }

        [MenuItem("Tools/Grok Doorway Lighting/Install Removable Hook Into StartMap")]
        public static void InstallStartMapHook()
        {
            Debug.Log(InstallStartMapHookCli());
        }

        [MenuItem("Tools/Grok Doorway Lighting/Remove Hook From StartMap")]
        public static void RemoveStartMapHook()
        {
            Debug.Log(RemoveStartMapHookCli());
        }

        public static string BuildSceneCli()
        {
            return GrokDoorwaySceneBuilder.BuildIsolatedPreviewScene();
        }

        public static string CapturePortalsCli()
        {
            return GrokDoorwayPortalMapBaker.CaptureBothRooms();
        }

        public static string InstallStartMapHookCli()
        {
            return GrokDoorwayStartMapHookInstaller.Install();
        }

        public static string RemoveStartMapHookCli()
        {
            return GrokDoorwayStartMapHookInstaller.Remove();
        }
    }
}
