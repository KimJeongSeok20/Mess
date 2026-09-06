using UnityEngine;

namespace GrokDoorwayLighting
{
    [DisallowMultipleComponent]
    public sealed class GrokDoorwayPreviewHud : MonoBehaviour
    {
        [SerializeField] private GrokDoorwayLightingBridge bridge;

        private void Reset()
        {
            bridge = GetComponent<GrokDoorwayLightingBridge>();
        }

        private void Update()
        {
            if (bridge == null)
                bridge = FindFirstObjectByType<GrokDoorwayLightingBridge>();
            if (bridge == null)
                return;

            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
                bridge.SetVisualization(false);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
                bridge.SetVisualization(true);
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
                bridge.SetPower(
                    DungeonTileLightmapSwitcher.PowerLevel.P100,
                    DungeonTileLightmapSwitcher.PowerLevel.P100);
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
                bridge.SetPower(
                    DungeonTileLightmapSwitcher.PowerLevel.P100,
                    DungeonTileLightmapSwitcher.PowerLevel.P0);
            if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
                bridge.SetPower(
                    DungeonTileLightmapSwitcher.PowerLevel.P0,
                    DungeonTileLightmapSwitcher.PowerLevel.P100);
            if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6))
                bridge.SetPower(
                    DungeonTileLightmapSwitcher.PowerLevel.P0,
                    DungeonTileLightmapSwitcher.PowerLevel.P0);
        }

        private void OnGUI()
        {
            if (bridge == null)
                return;

            const int width = 420;
            GUILayout.BeginArea(new Rect(16f, 16f, width, 220f), GUI.skin.box);
            GUILayout.Label("GROK DOORWAY LIGHTING");
            GUILayout.Label("ENV = StartMap Lighting Environment + dungeon post");
            GUILayout.Label(bridge.VisualizationEnabled ? "ADJACENT: ON" : "ADJACENT: OFF");
            GUILayout.Label($"POWER  Start={bridge.StartPower}  Admin={bridge.AdminPower}");
            GUILayout.Label($"Receivers  Start={bridge.StartReceiverCount}  Admin={bridge.AdminReceiverCount}");
            GUILayout.Label("1=OFF  2=ON  3=P100/P100  4=P100/P0  5=P0/P100  6=P0/P0");
            GUILayout.Label("1st: door->surface  2nd: floor->nearby walls. P100 rooms stay untouched.");
            GUILayout.EndArea();
        }
    }
}
