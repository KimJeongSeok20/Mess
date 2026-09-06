using UnityEditor;
using UnityEngine;

namespace DungeonRoomLocalLightShare.Editor
{
    public static class RoomLocalLightShareMenus
    {
        public static string PreparePlayCli()
        {
            string hash = RoomLocalHashGuard.CaptureOrVerify();
            if (hash.StartsWith("FAIL"))
                return hash;
            string outgoing = RoomLocalOutgoingCapture.CaptureBothRooms();
            if (outgoing.StartsWith("FAIL"))
                return outgoing;
            string scene = RoomLocalSceneBuilder.BuildIsolatedScene();
            if (scene.StartsWith("FAIL"))
                return scene;
            string audit = RoomLocalParityAudit.AuditIsolatedScene();
            if (audit.StartsWith("FAIL"))
                return audit;
            return "PASS prepare-play\n" + hash.Split('\n')[0] + "\n" +
                   outgoing.Split('\n')[0] + "\n" + scene.Split('\n')[0] + "\n" +
                   audit.Split('\n')[0];
        }

        public static void CompileCheckCli()
        {
            if (EditorUtility.scriptCompilationFailed)
            {
                Debug.LogError("[RoomLocalLightShare] COMPILE_FAIL");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[RoomLocalLightShare] COMPILE_OK");
            EditorApplication.Exit(0);
        }

        private const string MenuRoot = "Tools/Dungeon/Lighting/Room Local Light Share/";

        [MenuItem(MenuRoot + "Verify Production Input Hashes", false, 0)]
        private static void VerifyHashes()
        {
            Log(RoomLocalHashGuard.CaptureOrVerify());
        }

        [MenuItem(MenuRoot + "Capture Outgoing Portal Maps", false, 20)]
        private static void CaptureOutgoing()
        {
            Log(RoomLocalOutgoingCapture.CaptureBothRooms());
        }

        [MenuItem(MenuRoot + "Capture Outgoing For Every StartMap Flow Room", false, 21)]
        private static void CaptureAllFlowOutgoing()
        {
            Log(RoomLocalMatrixBuilder.CaptureAllFlowOutgoing());
        }

        [MenuItem(MenuRoot + "Build Isolated Start-Admin Scene", false, 21)]
        private static void BuildScene()
        {
            Log(RoomLocalSceneBuilder.BuildIsolatedScene());
        }

        [MenuItem(MenuRoot + "Audit Isolated Scene Parity (OFF)", false, 22)]
        private static void AuditParity()
        {
            Log(RoomLocalParityAudit.AuditIsolatedScene());
        }

        [MenuItem(MenuRoot + "Bake Incoming Bounce/StartRoom_R000", false, 40)]
        private static void BakeStart()
        {
            Log(RoomLocalBounceBaker.BakeStartReceiver());
        }

        [MenuItem(MenuRoot + "Bake Incoming Bounce/AdminstrativeSegregation_R000", false, 41)]
        private static void BakeAdmin()
        {
            Log(RoomLocalBounceBaker.BakeAdministrativeReceiver());
        }

        [MenuItem(MenuRoot + "Bake Incoming Bounce/Both Receivers", false, 42)]
        private static void BakeBoth()
        {
            Log(RoomLocalBounceBaker.BakeBothReceivers());
        }

        [MenuItem(MenuRoot + "Capture Game View Evidence", false, 60)]
        private static void CaptureEvidence()
        {
            Log(RoomLocalSmokeCapture.CaptureGameView("Manual"));
        }

        [MenuItem(MenuRoot + "Capture Game View Evidence/P0P100_D100", false, 61)]
        private static void CapturePrimary()
        {
            Log(RoomLocalSmokeCapture.CaptureGameView("P0P100_D100"));
        }

        [MenuItem(MenuRoot + "Install Removable Hook Into StartMap", false, 80)]
        private static void InstallStartMapHook()
        {
            Log(RoomLocalStartMapHookInstaller.Install());
        }

        [MenuItem(MenuRoot + "Remove Hook From StartMap", false, 81)]
        private static void RemoveStartMapHook()
        {
            Log(RoomLocalStartMapHookInstaller.Remove());
        }

        private static void Log(string result)
        {
            if (string.IsNullOrEmpty(result))
                return;
            if (result.StartsWith("FAIL"))
                Debug.LogError("[RoomLocalLightShare] " + result);
            else
                Debug.Log("[RoomLocalLightShare] " + result);
        }
    }
}
