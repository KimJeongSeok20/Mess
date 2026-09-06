using System.Collections.Generic;
using UnityEngine;

namespace GrokDoorwayLighting
{
    [DisallowMultipleComponent]
    public sealed class GrokDoorwayLightingBridge : MonoBehaviour
    {
        [SerializeField] private Transform startRoot;
        [SerializeField] private Transform adminRoot;
        [SerializeField] private Transform startDoorway;
        [SerializeField] private Transform adminDoorway;
        [SerializeField] private DungeonTileLightmapSwitcher startLighting;
        [SerializeField] private DungeonTileLightmapSwitcher adminLighting;
        [SerializeField] private GrokDoorwayPortalMap startPortalMap;
        [SerializeField] private GrokDoorwayPortalMap adminPortalMap;
        [SerializeField] private bool visualizationEnabled = true;
        [SerializeField, Min(0.25f)] private float blendDepth = 2.5f;
        [SerializeField, Min(0f)] private float lateralFade = 0.12f;
        [SerializeField, Min(0f)] private float verticalPadding = 0.18f;
        [SerializeField, Min(0f)] private float jambAllowance = 0.15f;
        [SerializeField, Min(0.05f)] private float receiverSearchPadding = 2.75f;
        [SerializeField, Min(0.05f)] private float spillScale = 1f;
        [SerializeField, Range(0.05f, 0.8f)] private float bounceReflectance = 0.20f;
        [SerializeField, Min(0.5f)] private float bounceRange = 2.5f;
        [SerializeField, Min(0f)] private float bounceScale = 1.15f;
        [SerializeField, Min(0f)] private float revealScale = 0f;

        private readonly List<GrokDoorwaySpillReceiver> _startReceivers = new List<GrokDoorwaySpillReceiver>();
        private readonly List<GrokDoorwaySpillReceiver> _adminReceivers = new List<GrokDoorwaySpillReceiver>();
        private bool _wired;

        public bool VisualizationEnabled => visualizationEnabled;
        public DungeonTileLightmapSwitcher.PowerLevel StartPower =>
            startLighting != null ? startLighting.CurrentPowerLevel : DungeonTileLightmapSwitcher.PowerLevel.P100;
        public DungeonTileLightmapSwitcher.PowerLevel AdminPower =>
            adminLighting != null ? adminLighting.CurrentPowerLevel : DungeonTileLightmapSwitcher.PowerLevel.P100;
        public int StartReceiverCount => _startReceivers.Count;
        public int AdminReceiverCount => _adminReceivers.Count;

        public void Configure(
            Transform start,
            Transform admin,
            Transform startDoor,
            Transform adminDoor,
            GrokDoorwayPortalMap startMap,
            GrokDoorwayPortalMap adminMap)
        {
            startRoot = start;
            adminRoot = admin;
            startDoorway = startDoor;
            adminDoorway = adminDoor;
            startPortalMap = startMap;
            adminPortalMap = adminMap;
            startLighting = start != null ? start.GetComponent<DungeonTileLightmapSwitcher>() : null;
            adminLighting = admin != null ? admin.GetComponent<DungeonTileLightmapSwitcher>() : null;
        }

        public void SetVisualization(bool enabled)
        {
            visualizationEnabled = enabled;
            Apply();
        }

        public void SetPower(
            DungeonTileLightmapSwitcher.PowerLevel start,
            DungeonTileLightmapSwitcher.PowerLevel admin)
        {
            if (startLighting != null)
                startLighting.SetPowerLevel(start);
            if (adminLighting != null)
                adminLighting.SetPowerLevel(admin);
            Apply();
        }

        private void OnEnable()
        {
            Subscribe();
            WireReceiversIfNeeded();
            SetPower(
                DungeonTileLightmapSwitcher.PowerLevel.P0,
                DungeonTileLightmapSwitcher.PowerLevel.P100);
            SetVisualization(true);
        }

        private void OnDisable()
        {
            Unsubscribe();
            HideAll();
        }

        private void Subscribe()
        {
            if (startLighting != null)
                startLighting.PowerLevelApplied += OnPowerChanged;
            if (adminLighting != null)
                adminLighting.PowerLevelApplied += OnPowerChanged;
        }

        private void Unsubscribe()
        {
            if (startLighting != null)
                startLighting.PowerLevelApplied -= OnPowerChanged;
            if (adminLighting != null)
                adminLighting.PowerLevelApplied -= OnPowerChanged;
        }

        private void OnPowerChanged(DungeonTileLightmapSwitcher.PowerLevel _)
        {
            Apply();
        }

        private void WireReceiversIfNeeded()
        {
            if (_wired)
                return;

            CollectReceivers(startRoot, startDoorway, _startReceivers);
            CollectReceivers(adminRoot, adminDoorway, _adminReceivers);
            _wired = true;
        }

        private void CollectReceivers(
            Transform root,
            Transform doorway,
            List<GrokDoorwaySpillReceiver> dest)
        {
            dest.Clear();
            if (root == null || doorway == null)
                return;

            Bounds search = new Bounds(doorway.position, Vector3.one * 0.1f);
            search.Expand(receiverSearchPadding * 2f);

            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer == null || renderer.GetComponent<GrokDoorwaySpillReceiver>() != null)
                    continue;
                if (renderer.gameObject.name == GrokDoorwayMaterialUtility.OverlayName)
                    continue;
                if (GrokDoorwayMath.IsDoorOccluder(renderer.transform))
                    continue;

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;
                if (!renderer.bounds.Intersects(search))
                    continue;

                GrokDoorwaySpillReceiver receiver = GrokDoorwaySpillReceiver.Create(renderer, filter);
                if (receiver != null)
                    dest.Add(receiver);
            }
        }

        public void Apply()
        {
            WireReceiversIfNeeded();

            ApplyRoom(
                _startReceivers,
                startDoorway,
                startLighting,
                adminLighting,
                adminPortalMap);

            ApplyRoom(
                _adminReceivers,
                adminDoorway,
                adminLighting,
                startLighting,
                startPortalMap);
        }

        private void ApplyRoom(
            List<GrokDoorwaySpillReceiver> receivers,
            Transform localDoorway,
            DungeonTileLightmapSwitcher localLighting,
            DungeonTileLightmapSwitcher neighborLighting,
            GrokDoorwayPortalMap neighborPortal)
        {
            if (localDoorway == null || localLighting == null)
            {
                Hide(receivers);
                return;
            }

            bool neighborTransfer = visualizationEnabled &&
                neighborLighting != null &&
                GrokDoorwayMath.IsTransferActive(
                    visualizationEnabled,
                    neighborLighting.CurrentPowerLevel,
                    localLighting.CurrentPowerLevel);

            Texture portal = null;
            float transferMode = 0f;
            if (neighborTransfer)
            {
                portal = neighborPortal != null
                    ? neighborPortal.GetTexture(neighborLighting.CurrentPowerLevel)
                    : null;
            }

            if (portal == null)
            {
                Hide(receivers);
                return;
            }

            GrokDoorwayMath.DoorwayFrame frame = GrokDoorwayMath.CreateFrame(
                localDoorway,
                -1f,
                blendDepth,
                lateralFade,
                verticalPadding,
                jambAllowance);

            for (int i = 0; i < receivers.Count; i++)
            {
                if (receivers[i] != null)
                    receivers[i].Apply(
                        frame,
                        portal,
                        spillScale,
                        bounceReflectance,
                        bounceRange,
                        bounceScale,
                        transferMode,
                        revealScale,
                        true);
            }
        }

        private void HideAll()
        {
            Hide(_startReceivers);
            Hide(_adminReceivers);
        }

        private static void Hide(List<GrokDoorwaySpillReceiver> receivers)
        {
            for (int i = 0; i < receivers.Count; i++)
            {
                if (receivers[i] != null)
                    receivers[i].Hide();
            }
        }
    }
}
