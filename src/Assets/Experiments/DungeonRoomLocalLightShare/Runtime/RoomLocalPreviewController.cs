using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    public sealed class RoomLocalPreviewController : MonoBehaviour
    {
        [SerializeField] private RoomLocalConnection connection;

        private void Awake()
        {
            if (connection == null)
                connection = GetComponent<RoomLocalConnection>();
        }

        private void Update()
        {
            if (connection == null)
                return;

            if (Input.GetKeyDown(KeyCode.Alpha1))
                connection.SetConnectionEnabled(false);
            if (Input.GetKeyDown(KeyCode.Alpha2))
                connection.SetConnectionEnabled(true);
            if (Input.GetKeyDown(KeyCode.Alpha3))
                SetPower(DungeonTileLightmapSwitcher.PowerLevel.P100, DungeonTileLightmapSwitcher.PowerLevel.P100);
            if (Input.GetKeyDown(KeyCode.Alpha4))
                SetPower(DungeonTileLightmapSwitcher.PowerLevel.P100, DungeonTileLightmapSwitcher.PowerLevel.P0);
            if (Input.GetKeyDown(KeyCode.Alpha5))
                SetPower(DungeonTileLightmapSwitcher.PowerLevel.P0, DungeonTileLightmapSwitcher.PowerLevel.P100);
            if (Input.GetKeyDown(KeyCode.Alpha6))
                SetPower(DungeonTileLightmapSwitcher.PowerLevel.P0, DungeonTileLightmapSwitcher.PowerLevel.P0);
            if (Input.GetKeyDown(KeyCode.Alpha7))
                SetDoor(0f);
            if (Input.GetKeyDown(KeyCode.Alpha8))
                SetDoor(0.5f);
            if (Input.GetKeyDown(KeyCode.Alpha9))
                SetDoor(1f);
        }

        private void SetPower(
            DungeonTileLightmapSwitcher.PowerLevel start,
            DungeonTileLightmapSwitcher.PowerLevel admin)
        {
            connection.SetPowerLevels(start, admin);
        }

        private void SetDoor(float openFraction)
        {
            if (connection.DoorAngle != null)
                connection.DoorAngle.ApplyOpenFraction(openFraction);
        }
    }
}
