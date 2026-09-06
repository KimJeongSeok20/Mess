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
            if (!connectionEnabled)
            {
                DisableTransport(true);
                return;
            }

            float aperture = 1f;
            float openFraction = 1f;
            if (doorAngle != null && doorAngle.IsConfigured)
            {
                doorAngle.EvaluateNow();
                aperture = doorAngle.ApertureFraction;
                openFraction = doorAngle.OpenFraction;
                UpdateCookieDoorShadows();
            }
            else
            {
                SetCookieShadowsDungeonOnly();
            }
            float startPower01 = Power01(startLighting);
            float adminPower01 = Power01(administrativeLighting);

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

        private void SetCookieShadowsDungeonOnly()
        {
            int dungeon = RoomLocalLightShareContract.DungeonRenderingLayerMask;
            if (startIncomingSpot != null)
                startIncomingSpot.SetShadowRenderingLayers(dungeon);
            if (administrativeIncomingSpot != null)
                administrativeIncomingSpot.SetShadowRenderingLayers(dungeon);
        }

        private void UpdateCookieDoorShadows()
        {
            int dungeon = RoomLocalLightShareContract.DungeonRenderingLayerMask;
            int doorLeaf = RoomLocalLightShareContract.DoorShadowRenderingLayerMask;
            bool doorInStart = false;
            if (doorAngle != null && doorAngle.DoorLeaf != null && startDoorway != null)
            {
                Vector3 doorPoint = doorAngle.DoorLeaf.position;
                Renderer leafRenderer = doorAngle.DoorLeaf.GetComponent<Renderer>();
                if (leafRenderer != null)
                    doorPoint = leafRenderer.bounds.center;
                doorInStart = Vector3.Dot(
                    doorPoint - startDoorway.position,
                    -startDoorway.forward) > 0f;
            }

            // 3D leaf shadows only the cookie lighting the room the door is actually in.
            // The other cookie sits in that room and fires through the portal; if the
            // inward leaf also shadowed it, D50 would occult the whole opening.
            if (startIncomingSpot != null)
                startIncomingSpot.SetShadowRenderingLayers(
                    doorInStart ? dungeon | doorLeaf : dungeon);
            if (administrativeIncomingSpot != null)
                administrativeIncomingSpot.SetShadowRenderingLayers(
                    doorInStart ? dungeon : dungeon | doorLeaf);
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
