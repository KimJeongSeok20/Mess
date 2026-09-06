using UnityEngine;
using UnityEngine.Rendering;

namespace GrokDoorwayLighting
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(20000)]
    public sealed class GrokDoorwayEnvironment : MonoBehaviour
    {
        [System.Serializable]
        public struct LightingEnvironment
        {
            public bool fog;
            public Color fogColor;
            public FogMode fogMode;
            public float fogDensity;
            public float linearFogStart;
            public float linearFogEnd;
            public Color ambientSkyColor;
            public Color ambientEquatorColor;
            public Color ambientGroundColor;
            public float ambientIntensity;
            public AmbientMode ambientMode;
            public Color subtractiveShadowColor;
            public Material skybox;
            public DefaultReflectionMode defaultReflectionMode;
            public int defaultReflectionResolution;
            public int reflectionBounces;
            public float reflectionIntensity;

            public static LightingEnvironment Capture()
            {
                return new LightingEnvironment
                {
                    fog = RenderSettings.fog,
                    fogColor = RenderSettings.fogColor,
                    fogMode = RenderSettings.fogMode,
                    fogDensity = RenderSettings.fogDensity,
                    linearFogStart = RenderSettings.fogStartDistance,
                    linearFogEnd = RenderSettings.fogEndDistance,
                    ambientSkyColor = RenderSettings.ambientSkyColor,
                    ambientEquatorColor = RenderSettings.ambientEquatorColor,
                    ambientGroundColor = RenderSettings.ambientGroundColor,
                    ambientIntensity = RenderSettings.ambientIntensity,
                    ambientMode = RenderSettings.ambientMode,
                    subtractiveShadowColor = RenderSettings.subtractiveShadowColor,
                    skybox = RenderSettings.skybox,
                    defaultReflectionMode = RenderSettings.defaultReflectionMode,
                    defaultReflectionResolution = RenderSettings.defaultReflectionResolution,
                    reflectionBounces = RenderSettings.reflectionBounces,
                    reflectionIntensity = RenderSettings.reflectionIntensity
                };
            }

            public void Apply()
            {
                RenderSettings.fog = fog;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogStartDistance = linearFogStart;
                RenderSettings.fogEndDistance = linearFogEnd;
                RenderSettings.ambientSkyColor = ambientSkyColor;
                RenderSettings.ambientEquatorColor = ambientEquatorColor;
                RenderSettings.ambientGroundColor = ambientGroundColor;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.subtractiveShadowColor = subtractiveShadowColor;
                RenderSettings.skybox = skybox;
                RenderSettings.sun = null;
                RenderSettings.defaultReflectionMode = defaultReflectionMode;
                RenderSettings.defaultReflectionResolution = defaultReflectionResolution;
                RenderSettings.reflectionBounces = reflectionBounces;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.customReflection = null;
            }
        }

        [SerializeField] private DungeonZoneManager zoneManager;
        [SerializeField] private Volume dungeonPostProcessVolume;
        [SerializeField] private Light dungeonOnlyLight;
        [SerializeField] private LightingEnvironment startMapLighting;

        public void Configure(
            DungeonZoneManager startMapZoneManager,
            Volume postProcessVolume,
            Light playerDungeonOnlyLight,
            LightingEnvironment lighting)
        {
            zoneManager = startMapZoneManager;
            dungeonPostProcessVolume = postProcessVolume;
            dungeonOnlyLight = playerDungeonOnlyLight;
            startMapLighting = lighting;
        }

        private void Start()
        {
            startMapLighting.Apply();

            if (dungeonPostProcessVolume != null)
                dungeonPostProcessVolume.enabled = true;
            if (dungeonOnlyLight != null)
                dungeonOnlyLight.enabled = false;

            zoneManager?.EnterDungeon(transform);

            var start = GameObject.Find("StartRoom_R000");
            var admin = GameObject.Find("AdminstrativeSegregation_R000");
            var startLighting = start != null ? start.GetComponent<DungeonTileLightmapSwitcher>() : null;
            var adminLighting = admin != null ? admin.GetComponent<DungeonTileLightmapSwitcher>() : null;
            startLighting?.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P0);
            adminLighting?.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P100);
        }

        private void LateUpdate()
        {
            startMapLighting.Apply();
        }
    }
}
