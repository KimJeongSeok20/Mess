using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    [DisallowMultipleComponent]
    public sealed class RoomLocalMatrixHud : MonoBehaviour
    {
        [SerializeField] private RoomLocalMatrixController controller;

        private void Awake()
        {
            if (controller == null)
                controller = GetComponent<RoomLocalMatrixController>();
        }

        private void OnGUI()
        {
            const int width = 760;
            GUI.Box(new Rect(12, 12, width, 450), GUIContent.none);
            GUILayout.BeginArea(new Rect(20, 20, width - 16, 434));
            GUILayout.Label("STARTMAP ROOM-LOCAL LIGHT MATRIX (MANUAL VISUAL REVIEW)");
            if (controller == null)
            {
                GUILayout.Label("FAIL: Matrix controller is missing.");
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< PREVIOUS  [", GUILayout.Width(150)))
                controller.PreviousCase();
            GUILayout.Label(
                "CASE " + (controller.CaseIndex + 1) + " / " + controller.CaseCount,
                GUILayout.Width(150));
            if (GUILayout.Button("NEXT ] >", GUILayout.Width(150)))
                controller.NextCase();
            GUILayout.EndHorizontal();

            GUILayout.Label(controller.CaseLabel);
            RoomLocalMatrixCatalog catalog = controller.Catalog;
            if (catalog != null)
            {
                GUILayout.Label(
                    "Observed by StartMap DunGen: " + catalog.Cases.Length +
                    " cases / " + catalog.CensusSeedCount + " fixed seeds" +
                    " (success " + catalog.CensusSuccessCount +
                    ", last new seed " + catalog.LastNewCaseSeed + ")");
            }
            GUILayout.Label("A: " + controller.RoomALabel);
            GUILayout.Label("B: " + controller.RoomBLabel);
            GUILayout.Label(
                "Outgoing=" + (controller.HasCompleteOutgoing ? "READY" : "MISSING") +
                "  Incoming Bounce=" + (controller.HasCompleteBounce ? "READY" : "NOT GENERATED") +
                "  Renderer AABB overlap=" + controller.OverlapVolume.ToString("0.000") +
                " (diagnostic only; DunGen placement already passed)");
            GUILayout.Label("Status: " + controller.Status);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Connection OFF"))
                controller.SetConnectionEnabled(false);
            if (GUILayout.Button("Connection ON"))
                controller.SetConnectionEnabled(true);
            if (GUILayout.Button("P100 / P100"))
                controller.SetPower(DungeonTileLightmapSwitcher.PowerLevel.P100, DungeonTileLightmapSwitcher.PowerLevel.P100);
            if (GUILayout.Button("P100 / P0"))
                controller.SetPower(DungeonTileLightmapSwitcher.PowerLevel.P100, DungeonTileLightmapSwitcher.PowerLevel.P0);
            if (GUILayout.Button("P0 / P100"))
                controller.SetPower(DungeonTileLightmapSwitcher.PowerLevel.P0, DungeonTileLightmapSwitcher.PowerLevel.P100);
            if (GUILayout.Button("P0 / P0"))
                controller.SetPower(DungeonTileLightmapSwitcher.PowerLevel.P0, DungeonTileLightmapSwitcher.PowerLevel.P0);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("CLOSE  (C)", GUILayout.Width(150)))
                controller.CloseDoor();
            if (GUILayout.Button("OPEN  (O)", GUILayout.Width(150)))
                controller.OpenDoor();
            if (GUILayout.Button("D000"))
                controller.SetDoor(0f);
            if (GUILayout.Button("D050"))
                controller.SetDoor(0.5f);
            if (GUILayout.Button("D100"))
                controller.SetDoor(1f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Door realtime", GUILayout.Width(120));
            if (GUILayout.Button(ModeLabel("Probe", DoorRealtimeMode.ProbeOnly)))
                controller.SetDoorRealtimeMode(DoorRealtimeMode.ProbeOnly);
            if (GUILayout.Button(ModeLabel("Cookie", DoorRealtimeMode.CookieReceive)))
                controller.SetDoorRealtimeMode(DoorRealtimeMode.CookieReceive);
            if (GUILayout.Button(ModeLabel("FaceDirect", DoorRealtimeMode.PerFaceDirect)))
                controller.SetDoorRealtimeMode(DoorRealtimeMode.PerFaceDirect);
            GUILayout.EndHorizontal();

            float intensity = DrawSlider(
                "Direct intensity",
                controller.DirectIntensity,
                0.25f,
                2f,
                "x0.00");
            float range = DrawSlider(
                "Direct range",
                controller.DirectRange,
                2.5f,
                5f,
                "0.00 m");
            if (!Mathf.Approximately(intensity, controller.DirectIntensity) ||
                !Mathf.Approximately(range, controller.DirectRange))
                controller.SetDirectTuning(intensity, range);

            RoomLocalConnection connection = controller.Connection;
            if (connection != null)
            {
                GUILayout.Label(
                    "Live: A=" + FormatPower(connection.StartLighting) +
                    " B=" + FormatPower(connection.AdministrativeLighting) +
                    " Door=" + (connection.DoorAngle != null
                        ? connection.DoorAngle.OpenFraction.ToString("0.00")
                        : "?") +
                    "  Ap=" + (connection.DoorAngle != null
                        ? connection.DoorAngle.ApertureFraction.ToString("0.00")
                        : "?") +
                    (controller.IsDoorAnimating
                        ? (controller.DoorAnimateTarget > 0.5f ? "  OPENING" : "  CLOSING")
                        : "") +
                    (connection.IsFaultLatched ? "  FAULT=" + connection.FaultReason : ""));
            }
            GUILayout.Label("Keys: [ previous, ] next, 1/2 connection, 3-6 power, 7-9 door, O open, C close");
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
            GUILayout.Label(label, GUILayout.Width(120));
            float next = GUILayout.HorizontalSlider(value, minimum, maximum, GUILayout.Width(430));
            GUILayout.Label(next.ToString(format), GUILayout.Width(75));
            GUILayout.EndHorizontal();
            return next;
        }

        private string ModeLabel(string label, DoorRealtimeMode mode)
        {
            return controller != null && controller.DoorRealtimeMode == mode
                ? "[" + label + "]"
                : label;
        }

        private static string FormatPower(DungeonTileLightmapSwitcher switcher)
        {
            return switcher != null ? switcher.CurrentPowerLevel.ToString() : "?";
        }
    }
}
