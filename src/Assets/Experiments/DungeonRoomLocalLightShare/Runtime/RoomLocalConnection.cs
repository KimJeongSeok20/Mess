using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class RoomLocalConnection : MonoBehaviour
    {
        [SerializeField] private Transform startRoot;
        [SerializeField] private Transform administrativeRoot;
        [SerializeField] private Transform startDoorway;
        [SerializeField] private Transform administrativeDoorway;
        [SerializeField] private DungeonTileLightmapSwitcher startLighting;
        [SerializeField] private DungeonTileLightmapSwitcher administrativeLighting;
        [SerializeField] private OutgoingPortalMap startOutgoing;
        [SerializeField] private OutgoingPortalMap administrativeOutgoing;
        [SerializeField] private IncomingBounceData startBounce;
        [SerializeField] private IncomingBounceData administrativeBounce;
        [SerializeField] private Shader composeShader;
        [SerializeField] private RoomLocalDoorAngleSource doorAngle;
        [SerializeField] private bool connectionEnabled = true;
        [SerializeField] private bool openPassage;
        [SerializeField, Min(0.05f)] private float startDirectResponseScale = 1f;
        [SerializeField, Min(0.05f)] private float administrativeDirectResponseScale = 1f;
        [SerializeField, Min(0.5f)] private float directRange = RoomLocalDoorwayFrame.DirectRange;
        [SerializeField, Min(0.05f)] private float directSourceStandOff = 1f;
        [SerializeField, Min(0f)] private float bounceResponseScale = 1f;

        private RoomLocalCookieSpot startIncomingSpot;
        private RoomLocalCookieSpot administrativeIncomingSpot;
        private RoomLocalBounceComposer startComposer;
        private RoomLocalBounceComposer administrativeComposer;
        private bool faultLatched;
        private string faultReason = string.Empty;
        private float directIntensityMultiplier = 1f;

        public bool ConnectionEnabled => connectionEnabled;
        public bool IsFaultLatched => faultLatched;
        public string FaultReason => faultReason;
        public RoomLocalDoorAngleSource DoorAngle => doorAngle;
        public DungeonTileLightmapSwitcher StartLighting => startLighting;
        public DungeonTileLightmapSwitcher AdministrativeLighting => administrativeLighting;
        public Transform StartDoorway => startDoorway;
        public Transform AdministrativeDoorway => administrativeDoorway;
        public float DirectIntensityMultiplier => directIntensityMultiplier;
        public float DirectRange => directRange;

        public RoomLocalTransferState EvaluateTransferState()
        {
            RoomLocalPortalBinding binding = RoomLocalPortalBinding.Unresolved;
            float open = 0f;
            float aperture = 0f;
            if (doorAngle != null && doorAngle.IsConfigured)
            {
                doorAngle.EvaluateNow();
                binding = RoomLocalPortalBinding.Door;
                open = doorAngle.OpenFraction;
                aperture = doorAngle.ApertureFraction;
            }
            else if (openPassage)
            {
                binding = RoomLocalPortalBinding.OpenPassage;
                open = aperture = 1f;
            }

            return new RoomLocalTransferState(
                isActiveAndEnabled && connectionEnabled && !faultLatched &&
                binding != RoomLocalPortalBinding.Unresolved,
                binding, open, aperture, Power01(startLighting), Power01(administrativeLighting));
        }

        public void SetOpenPassage(bool value)
        {
            openPassage = value;
        }

        public bool TrySampleDirectSH(DunGen.Tile receiverTile, Vector3 worldPosition,
            Transform ignoredGeometryRoot, out UnityEngine.Rendering.SphericalHarmonicsL2 contribution)
        {
            contribution = default;
            RoomLocalTransferState state = EvaluateTransferState();
            if (!state.Enabled || receiverTile == null || DungeonTileProbeRegistry.Active == null)
                return false;
            bool receivesInStart = startRoot != null && startRoot.IsChildOf(receiverTile.transform);
            bool receivesInAdministrative = administrativeRoot != null && administrativeRoot.IsChildOf(receiverTile.transform);
            if (receivesInStart == receivesInAdministrative)
                return false;
            RoomLocalCookieSpot spot = receivesInStart ? startIncomingSpot : administrativeIncomingSpot;
            float sourcePower = receivesInStart ? state.AdministrativePower01 : state.StartPower01;
            if (spot == null || !spot.TrySampleRadiance(worldPosition, sourcePower, state.ApertureFraction,
                    out Color radiance, out Vector3 portalEntry, out Vector3 directionToLight))
                return false;

            Vector3 segment = worldPosition - portalEntry;
            Vector3 start = portalEntry + segment.normalized * Mathf.Min(0.001f, segment.magnitude * 0.5f);
            if (!DungeonTileProbeRegistry.Active.IsPortalSegmentVisible(receiverTile, start, worldPosition, ignoredGeometryRoot))
                return false;
            contribution.AddDirectionalLight(directionToLight, radiance, 1f);
            return true;
        }

        public void Configure(
            Transform start,
            Transform admin,
            Transform startDoor,
            Transform adminDoor,
            OutgoingPortalMap startOut,
            OutgoingPortalMap adminOut,
            IncomingBounceData startIn,
            IncomingBounceData adminIn,
            Shader shader,
            RoomLocalDoorAngleSource angle)
        {
            startRoot = start;
            administrativeRoot = admin;
            startDoorway = startDoor;
            administrativeDoorway = adminDoor;
            startOutgoing = startOut;
            administrativeOutgoing = adminOut;
            startBounce = startIn;
            administrativeBounce = adminIn;
            composeShader = shader;
            doorAngle = angle;
            startLighting = start != null ? start.GetComponent<DungeonTileLightmapSwitcher>() : null;
            administrativeLighting = admin != null
                ? admin.GetComponent<DungeonTileLightmapSwitcher>()
                : null;
            BuildChildren();
        }

        public void SetConnectionEnabled(bool enabled)
        {
            connectionEnabled = enabled;
            if (!enabled)
                DisableTransport(true);
        }

        public void SetStartPower(DungeonTileLightmapSwitcher.PowerLevel power)
        {
            PrepareForPowerChange();
            if (startLighting != null)
                startLighting.SetPowerLevel(power);
        }

        public void SetAdministrativePower(DungeonTileLightmapSwitcher.PowerLevel power)
        {
            PrepareForPowerChange();
            if (administrativeLighting != null)
                administrativeLighting.SetPowerLevel(power);
        }

        public void SetPowerLevels(
            DungeonTileLightmapSwitcher.PowerLevel start,
            DungeonTileLightmapSwitcher.PowerLevel administrative)
        {
            PrepareForPowerChange();
            if (startLighting != null)
                startLighting.SetPowerLevel(start);
            if (administrativeLighting != null)
                administrativeLighting.SetPowerLevel(administrative);
        }

        public void SetRuntimeDirectTuning(float intensityMultiplier, float range)
        {
            directIntensityMultiplier = Mathf.Clamp(intensityMultiplier, 0.05f, 5f);
            directRange = Mathf.Clamp(range, 0.5f, 20f);
            ApplyDirectTuning();
        }

        private void Awake()
        {
            BuildChildren();
        }

        private void LateUpdate()
        {
            if (faultLatched)
                return;
            ApplyTransport();
        }

        private void ApplyTransport()
        {
            RoomLocalTransferState state = EvaluateTransferState();
            if (!state.Enabled)
            {
                DisableTransport(true);
                return;
            }

            float aperture = state.ApertureFraction;
            float openFraction = state.OpenFraction;
            float startPower01 = state.StartPower01;
            float adminPower01 = state.AdministrativePower01;

            if (startIncomingSpot != null &&
                !startIncomingSpot.TryApply(
                    adminPower01,
                    aperture,
                    true,
                    out string failure))
            {
                Latch(failure);
                return;
            }

            if (administrativeIncomingSpot != null &&
                !administrativeIncomingSpot.TryApply(
                    startPower01,
                    aperture,
                    true,
                    out failure))
            {
                Latch(failure);
                return;
            }

            Color startTransferRadiance = EvaluateTransferRadiance(
                administrativeOutgoing,
                adminPower01,
                aperture);
            Color adminTransferRadiance = EvaluateTransferRadiance(
                startOutgoing,
                startPower01,
                aperture);

            if (startComposer != null && startBounce != null)
            {
                if (!startComposer.TrySetContribution(
                        openFraction,
                        startTransferRadiance,
                        out failure))
                    Debug.LogWarning("[RoomLocalLightShare] Start bounce: " + failure, this);
            }

            if (administrativeComposer != null && administrativeBounce != null)
            {
                if (!administrativeComposer.TrySetContribution(
                        openFraction,
                        adminTransferRadiance,
                        out failure))
                    Debug.LogWarning("[RoomLocalLightShare] Admin bounce: " + failure, this);
            }
        }

        private void DisableTransport(bool restoreLightmaps)
        {
            if (startIncomingSpot != null)
                startIncomingSpot.DisableAll();
            if (administrativeIncomingSpot != null)
                administrativeIncomingSpot.DisableAll();
            if (startComposer != null)
                startComposer.TryDeactivate(restoreLightmaps, out _);
            if (administrativeComposer != null)
                administrativeComposer.TryDeactivate(restoreLightmaps, out _);
        }

        private void BuildChildren()
        {
            if (startIncomingSpot == null)
                startIncomingSpot = GetOrAddChild<RoomLocalCookieSpot>("StartIncomingSpot");
            startIncomingSpot.Configure(
                startDoorway,
                administrativeDoorway,
                administrativeOutgoing,
                startDirectResponseScale * directIntensityMultiplier,
                directRange,
                directSourceStandOff,
                RoomLocalLightShareContract.StartIncomingCookieLightingLayerMask);

            if (administrativeIncomingSpot == null)
                administrativeIncomingSpot = GetOrAddChild<RoomLocalCookieSpot>("AdminIncomingSpot");
            administrativeIncomingSpot.Configure(
                administrativeDoorway,
                startDoorway,
                startOutgoing,
                administrativeDirectResponseScale * directIntensityMultiplier,
                directRange,
                directSourceStandOff,
                RoomLocalLightShareContract.AdministrativeIncomingCookieLightingLayerMask);

            if (startBounce != null && startRoot != null)
            {
                if (startComposer == null)
                    startComposer = GetOrAddChild<RoomLocalBounceComposer>("StartBounce");
                startComposer.Configure(startBounce, startRoot, composeShader, bounceResponseScale);
            }

            if (administrativeBounce != null && administrativeRoot != null)
            {
                if (administrativeComposer == null)
                    administrativeComposer = GetOrAddChild<RoomLocalBounceComposer>("AdminBounce");
                administrativeComposer.Configure(
                    administrativeBounce,
                    administrativeRoot,
                    composeShader,
                    bounceResponseScale);
            }
        }

        private void OnDisable()
        {
            DisableTransport(true);
        }

        private void ApplyDirectTuning()
        {
            if (startIncomingSpot != null)
            {
                startIncomingSpot.SetRuntimeTuning(
                    startDirectResponseScale * directIntensityMultiplier,
                    directRange);
            }

            if (administrativeIncomingSpot != null)
            {
                administrativeIncomingSpot.SetRuntimeTuning(
                    administrativeDirectResponseScale * directIntensityMultiplier,
                    directRange);
            }
        }

        private void PrepareForPowerChange()
        {
            if (startComposer != null &&
                !startComposer.TryDeactivate(true, out string startFailure))
            {
                Debug.LogWarning(
                    "[RoomLocalLightShare] Could not release Start bounce before power change: " +
                    startFailure,
                    this);
            }

            if (administrativeComposer != null &&
                !administrativeComposer.TryDeactivate(true, out string administrativeFailure))
            {
                Debug.LogWarning(
                    "[RoomLocalLightShare] Could not release Admin bounce before power change: " +
                    administrativeFailure,
                    this);
            }
        }

        private T GetOrAddChild<T>(string childName) where T : Component
        {
            Transform child = transform.Find(childName);
            if (child == null)
            {
                var created = new GameObject(childName);
                created.transform.SetParent(transform, false);
                child = created.transform;
            }

            T component = child.GetComponent<T>();
            return component != null ? component : child.gameObject.AddComponent<T>();
        }

        private static float Power01(DungeonTileLightmapSwitcher switcher)
        {
            if (switcher == null)
                return 0f;
            return switcher.CurrentPowerLevel == DungeonTileLightmapSwitcher.PowerLevel.P100 ? 1f : 0f;
        }

        private static Color EvaluateTransferRadiance(
            OutgoingPortalMap outgoing,
            float sourcePower01,
            float aperture)
        {
            if (outgoing == null)
                return Color.black;

            return RoomLocalLightShareMath.ComposeTransferRadiance(
                outgoing.Power0Average,
                outgoing.Power100Average,
                sourcePower01,
                aperture);
        }

        private void Latch(string reason)
        {
            faultLatched = true;
            faultReason = reason ?? "Unknown connection fault.";
            DisableTransport(true);
            Debug.LogError("[RoomLocalLightShare] " + faultReason, this);
        }
    }
}
