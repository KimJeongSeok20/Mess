using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    public sealed class RoomLocalPreviewHud : MonoBehaviour
    {
        [SerializeField] private RoomLocalConnection connection;
        [SerializeField] private RoomLocalPreviewController controller;

        private const float MinIntensityMultiplier = 0.25f;
        private const float MaxIntensityMultiplier = 2f;
        private const float MinDirectRange = 2.5f;
        private const float MaxDirectRange = 5f;

        private void Awake()
        {
            if (connection == null)
                connection = GetComponent<RoomLocalConnection>();
            if (controller == null)
                controller = GetComponent<RoomLocalPreviewController>();
        }

        private void OnGUI()
        {
            const int width = 430;
            GUI.Box(new Rect(12, 12, width, 240), GUIContent.none);
            GUILayout.BeginArea(new Rect(20, 20, width - 16, 224));
            GUILayout.Label("ROOM LOCAL LIGHT SHARE");
            if (connection != null)
            {
                GUILayout.Label(
                    "Connection: " + (connection.ConnectionEnabled ? "ON" : "OFF") +
                    (connection.IsFaultLatched ? "  FAULT " + connection.FaultReason : ""));
                DungeonTileLightmapSwitcher start = connection.StartLighting;
                DungeonTileLightmapSwitcher admin = connection.AdministrativeLighting;
                GUILayout.Label(
                    "Power Start=" + FormatPower(start) +
                    "  Admin=" + FormatPower(admin));
                RoomLocalDoorAngleSource door = connection.DoorAngle;
                if (door != null)
                {
                    GUILayout.Label(
                        "Door open=" + door.OpenFraction.ToString("0.00") +
                        "  aperture=" + door.ApertureFraction.ToString("0.00"));
                }


                float nextIntensity = DrawSlider(
                    "Direct intensity",
                    connection.DirectIntensityMultiplier,
                    MinIntensityMultiplier,
                    MaxIntensityMultiplier,
                    "x0.00");
                float nextRange = DrawSlider(
                    "Direct range",
                    connection.DirectRange,
                    MinDirectRange,
                    MaxDirectRange,
                    "0.00 m");
                if (!Mathf.Approximately(nextIntensity, connection.DirectIntensityMultiplier) ||
                    !Mathf.Approximately(nextRange, connection.DirectRange))
                {
                    connection.SetRuntimeDirectTuning(
                        nextIntensity,
                        nextRange);
                }
            }

            GUILayout.Label("1=OFF  2=ON  3=P100/P100  4=P100/P0  5=P0/P100  6=P0/P0");
            GUILayout.Label("7=D000  8=D050  9=D100");
            GUILayout.EndArea();
        }

        private static float DrawSlider(
            string label,
            float value,
            float minimum,
            float maximum,
            string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(112));
            float next = GUILayout.HorizontalSlider(value, minimum, maximum, GUILayout.Width(220));
            GUILayout.Label(next.ToString(format), GUILayout.Width(66));
            GUILayout.EndHorizontal();
            return next;
        }

        private static string FormatPower(DungeonTileLightmapSwitcher switcher)
        {
            if (switcher == null)
                return "?";
            return switcher.CurrentPowerLevel == DungeonTileLightmapSwitcher.PowerLevel.P100
                ? "P100"
                : "P0";
        }
    }
}
