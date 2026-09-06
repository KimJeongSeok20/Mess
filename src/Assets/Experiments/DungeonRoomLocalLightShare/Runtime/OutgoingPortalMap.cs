using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    [CreateAssetMenu(
        fileName = "OutgoingPortalMap",
        menuName = "Dungeon/Room Local Light Share/Outgoing Portal Map")]
    public sealed class OutgoingPortalMap : ScriptableObject
    {
        [SerializeField] private string roomId;
        [SerializeField] private string doorwayPath;
        [SerializeField] private Texture2D power0Hdr;
        [SerializeField] private Texture2D power100Hdr;
        [SerializeField] private Texture2D power0Cookie;
        [SerializeField] private Texture2D power100Cookie;
        [SerializeField] private Color power0Average = Color.black;
        [SerializeField] private Color power100Average = Color.black;
        [SerializeField, Min(0f)] private float power0Peak;
        [SerializeField, Min(0f)] private float power100Peak;
        [SerializeField, Min(0f)] private float power100DirectPeak;

        public string RoomId => roomId;
        public string DoorwayPath => doorwayPath;
        public Texture2D Power0Hdr => power0Hdr;
        public Texture2D Power100Hdr => power100Hdr;
        public Texture2D Power0CookieTexture => power0Cookie;
        public Texture2D Power100CookieTexture => power100Cookie;
        public Color Power0Average => power0Average;
        public Color Power100Average => power100Average;
        public float Power0Intensity => RoomLocalLightShareMath.Luminance(power0Average);
        public float Power100Intensity => RoomLocalLightShareMath.Luminance(power100Average);
        public float Power0Peak => power0Peak;
        public float Power100Peak => power100Peak;
        public float Power100DirectPeak => power100DirectPeak > 0f
            ? power100DirectPeak
            : power100Peak;
        public bool HasDirectPeakCalibration =>
            Power100DirectPeak > power100Peak + 1e-5f;

        public void ConfigureAuthoring(
            string stableRoomId,
            string stableDoorwayPath,
            Texture2D hdr0,
            Texture2D hdr100,
            Texture2D cookie0,
            Texture2D cookie100,
            Color average0,
            Color average100,
            float peak0,
            float peak100)
        {
            roomId = stableRoomId ?? string.Empty;
            doorwayPath = stableDoorwayPath ?? string.Empty;
            power0Hdr = hdr0;
            power100Hdr = hdr100;
            power0Cookie = cookie0;
            power100Cookie = cookie100;
            power0Average = average0;
            power100Average = average100;
            power0Peak = Mathf.Max(0f, peak0);
            power100Peak = Mathf.Max(0f, peak100);
            power100DirectPeak = power100Peak;
        }

        public void ConfigureDirectPeakCalibration(float calibratedPoweredPeak)
        {
            power100DirectPeak = Mathf.Max(power100Peak, calibratedPoweredPeak);
        }

        public bool TryGetRuntimeCookie(
            float power01,
            out Texture cookie,
            out float peakRadiance,
            out string failure)
        {
            cookie = null;
            peakRadiance = 0f;
            Texture2D selected = power01 >= 0.5f ? power100Cookie : power0Cookie;
            if (selected == null)
            {
                failure = "Outgoing portal cookie is missing. A cookie-spot never falls back to an unmasked light.";
                return false;
            }

            cookie = selected;
            peakRadiance = Mathf.Lerp(
                power0Peak,
                Power100DirectPeak,
                Mathf.Clamp01(power01));
            if (peakRadiance <= 1e-5f)
            {
                failure = "Outgoing portal peak radiance is missing. Recapture the portal map.";
                return false;
            }
            failure = null;
            return true;
        }
    }
}
