using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonRoomLocalLightShare
{
    /// <summary>
    /// Applies the StartMap indoor dungeon contract so P0 is not lit by outdoor trilight/sky.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class RoomLocalEnvironment : MonoBehaviour
    {
        [SerializeField] private DungeonZoneManager zoneManager;
        [SerializeField] private Volume dungeonVolume;
        [SerializeField] private DungeonTileLightmapSwitcher startLighting;
        [SerializeField] private DungeonTileLightmapSwitcher administrativeLighting;

        public void Configure(
            DungeonZoneManager zone,
            Volume volume,
            DungeonTileLightmapSwitcher start,
            DungeonTileLightmapSwitcher admin)
        {
            zoneManager = zone;
            dungeonVolume = volume;
            startLighting = start;
            administrativeLighting = admin;
        }

        private void Start()
        {
            ApplyIndoorContract();
            if (dungeonVolume != null)
                dungeonVolume.enabled = true;
            if (zoneManager != null)
                zoneManager.EnterDungeon(transform);
            ApplyIndoorContract();
            if (startLighting != null)
                startLighting.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P0);
            if (administrativeLighting != null)
                administrativeLighting.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P100);
        }

        private void LateUpdate()
        {
            ApplyIndoorContract();
        }

        public static void ApplyIndoorContract()
        {
            RenderSettings.sun = null;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientSkyColor = Color.black;
            RenderSettings.ambientEquatorColor = Color.black;
            RenderSettings.ambientGroundColor = Color.black;
            RenderSettings.ambientIntensity = 0f;
            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflection = null;
            RenderSettings.fog = false;
        }
    }
}
