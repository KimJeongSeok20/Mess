using UnityEngine;

namespace DungeonRoomLocalLightShare.Editor
{
    public static class RoomLocalStateCapture
    {
        public static string SetPower(string start, string admin)
        {
            var connection = Object.FindFirstObjectByType<RoomLocalConnection>();
            if (connection == null)
                return "FAIL no connection";
            connection.SetConnectionEnabled(true);
            connection.SetPowerLevels(Parse(start), Parse(admin));
            return "PASS start=" + start + " admin=" + admin +
                   " actualStart=" + Format(connection.StartLighting) +
                   " actualAdmin=" + Format(connection.AdministrativeLighting);
        }

        public static string SetDoor(float open)
        {
            var connection = Object.FindFirstObjectByType<RoomLocalConnection>();
            if (connection == null || connection.DoorAngle == null)
                return "FAIL no door";
            connection.DoorAngle.ApplyOpenFraction(open);
            return "PASS door=" + connection.DoorAngle.OpenFraction.ToString("0.00");
        }

        public static string SetConnection(bool enabled)
        {
            var connection = Object.FindFirstObjectByType<RoomLocalConnection>();
            if (connection == null)
                return "FAIL no connection";
            connection.SetConnectionEnabled(enabled);
            return "PASS connection=" + connection.ConnectionEnabled;
        }

        public static string AimDoorway()
        {
            Camera camera = Camera.main;
            Transform door = GameObject.Find("StartRoom_R000") != null
                ? GameObject.Find("StartRoom_R000").transform.Find("Doorways/Door_SM_A/DoorWayPoint")
                : null;
            if (camera == null || door == null)
                return "FAIL camera or doorway missing";
            camera.transform.position = door.position - door.forward * 3.2f + door.up * 1.62f;
            Vector3 look = door.position + door.up * 1.1f;
            camera.transform.rotation = Quaternion.LookRotation(look - camera.transform.position, Vector3.up);
            return "PASS doorway " + camera.transform.position;
        }

        public static string AimInterior()
        {
            Camera camera = Camera.main;
            if (camera == null)
                return "FAIL no camera";
            camera.transform.position = new Vector3(7.2f, 1.6f, 10.5f);
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(-0.15f, -0.12f, -1f), Vector3.up);
            return "PASS interior " + camera.transform.position;
        }

        public static string AimDoorCloseup()
        {
            Camera camera = Camera.main;
            var connection = Object.FindFirstObjectByType<RoomLocalConnection>();
            Transform leaf = connection != null && connection.DoorAngle != null
                ? connection.DoorAngle.DoorLeaf
                : null;
            if (camera == null || leaf == null)
                return "FAIL camera or leaf missing";
            camera.transform.position = leaf.position - leaf.right * 1.6f + Vector3.up * 1.35f + leaf.forward * 0.4f;
            camera.transform.rotation = Quaternion.LookRotation(
                (leaf.position + Vector3.up * 1.1f) - camera.transform.position,
                Vector3.up);
            return "PASS doorcloseup " + camera.transform.position;
        }

        private static DungeonTileLightmapSwitcher.PowerLevel Parse(string value)
        {
            return value == "P0"
                ? DungeonTileLightmapSwitcher.PowerLevel.P0
                : DungeonTileLightmapSwitcher.PowerLevel.P100;
        }

        private static string Format(DungeonTileLightmapSwitcher switcher)
        {
            return switcher != null ? switcher.CurrentPowerLevel.ToString() : "?";
        }
    }
}
