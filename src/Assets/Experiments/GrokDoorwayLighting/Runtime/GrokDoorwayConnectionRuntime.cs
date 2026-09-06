using System.Collections.Generic;
using DunGen;
using UnityEngine;

namespace GrokDoorwayLighting
{
    public sealed class GrokDoorwayConnectionRuntime
    {
        public readonly Doorway DoorA;
        public readonly Doorway DoorB;
        public readonly DungeonTileLightmapSwitcher LightingA;
        public readonly DungeonTileLightmapSwitcher LightingB;

        private readonly List<GrokDoorwaySpillReceiver> _receiversA = new List<GrokDoorwaySpillReceiver>();
        private readonly List<GrokDoorwaySpillReceiver> _receiversB = new List<GrokDoorwaySpillReceiver>();
        private DunGen.Door _connectionDoor;
        private bool _wired;
        private bool _hasLastApply;
        private bool _lastVisualization;
        private GrokDoorwayPortalLibrary _lastLibrary;
        private GrokDoorwayStartMapInstaller.Settings _lastSettings;

        public GrokDoorwayConnectionRuntime(
            Doorway doorA,
            Doorway doorB,
            DungeonTileLightmapSwitcher lightingA,
            DungeonTileLightmapSwitcher lightingB)
        {
            DoorA = doorA;
            DoorB = doorB;
            LightingA = lightingA;
            LightingB = lightingB;
        }

        public void Apply(
            bool visualizationEnabled,
            GrokDoorwayPortalLibrary library,
            in GrokDoorwayStartMapInstaller.Settings settings)
        {
            _lastVisualization = visualizationEnabled;
            _lastLibrary = library;
            _lastSettings = settings;
            _hasLastApply = true;

            WireIfNeeded(settings.receiverSearchPadding);

            if (!visualizationEnabled || !IsDoorOpen())
            {
                Hide(_receiversA);
                Hide(_receiversB);
                return;
            }

            ApplyRoom(
                _receiversA,
                DoorA != null ? DoorA.transform : null,
                LightingA,
                LightingB,
                library,
                DoorB,
                visualizationEnabled,
                settings);
            ApplyRoom(
                _receiversB,
                DoorB != null ? DoorB.transform : null,
                LightingB,
                LightingA,
                library,
                DoorA,
                visualizationEnabled,
                settings);
        }

        public void Teardown()
        {
            UnsubscribeDoor();
            DestroyReceivers(_receiversA);
            DestroyReceivers(_receiversB);
            _wired = false;
            _hasLastApply = false;
            _lastLibrary = null;
        }

        private void WireIfNeeded(float searchPadding)
        {
            if (_wired)
                return;

            Collect(LightingA != null ? LightingA.transform : null, DoorA != null ? DoorA.transform : null, searchPadding, _receiversA);
            Collect(LightingB != null ? LightingB.transform : null, DoorB != null ? DoorB.transform : null, searchPadding, _receiversB);
            SubscribeDoor();
            _wired = true;
        }

        private static void ApplyRoom(
            List<GrokDoorwaySpillReceiver> receivers,
            Transform localDoorway,
            DungeonTileLightmapSwitcher localLighting,
            DungeonTileLightmapSwitcher neighborLighting,
            GrokDoorwayPortalLibrary library,
            Doorway neighborDoor,
            bool visualizationEnabled,
            in GrokDoorwayStartMapInstaller.Settings settings)
        {
            if (localDoorway == null || localLighting == null)
            {
                Hide(receivers);
                return;
            }

            bool neighborTransfer = GrokDoorwayMath.IsTransferActive(
                visualizationEnabled,
                neighborLighting != null
                    ? neighborLighting.CurrentPowerLevel
                    : DungeonTileLightmapSwitcher.PowerLevel.P0,
                localLighting.CurrentPowerLevel);

            Texture portal = null;
            if (neighborTransfer && library != null && neighborLighting != null)
                portal = library.GetP100(neighborLighting, neighborDoor);

            if (portal == null)
            {
                Hide(receivers);
                return;
            }

            GrokDoorwayMath.DoorwayFrame frame = GrokDoorwayMath.CreateFrame(
                localDoorway,
                -1f,
                settings.blendDepth,
                settings.lateralFade,
                settings.verticalPadding,
                settings.jambAllowance);

            for (int i = 0; i < receivers.Count; i++)
            {
                if (receivers[i] != null)
                    receivers[i].Apply(
                        frame,
                        portal,
                        settings.spillScale,
                        settings.bounceReflectance,
                        settings.bounceRange,
                        settings.bounceScale,
                        0f,
                        settings.revealScale,
                        true);
            }
        }

        private static void Collect(
            Transform root,
            Transform doorway,
            float searchPadding,
            List<GrokDoorwaySpillReceiver> dest)
        {
            dest.Clear();
            if (root == null || doorway == null)
                return;

            Bounds search = new Bounds(doorway.position, Vector3.one * 0.1f);
            search.Expand(searchPadding * 2f);

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
                if (IsUnsafeCombinedMesh(filter.sharedMesh))
                    continue;
                if (!renderer.bounds.Intersects(search))
                    continue;

                GrokDoorwaySpillReceiver receiver = GrokDoorwaySpillReceiver.Create(renderer, filter);
                if (receiver != null)
                    dest.Add(receiver);
            }
        }

        private void SubscribeDoor()
        {
            UnsubscribeDoor();
            _connectionDoor = FindConnectionDoor(DoorA, DoorB);
            if (_connectionDoor == null)
                return;

            _connectionDoor.OnDoorStateChanged += OnDoorStateChanged;
        }

        private void UnsubscribeDoor()
        {
            if (_connectionDoor != null)
                _connectionDoor.OnDoorStateChanged -= OnDoorStateChanged;
            _connectionDoor = null;
        }

        private void OnDoorStateChanged(DunGen.Door door, bool isOpen)
        {
            if (!_hasLastApply)
                return;

            Apply(_lastVisualization, _lastLibrary, _lastSettings);
        }

        private bool IsDoorOpen()
        {
            return _connectionDoor == null || _connectionDoor.IsOpen;
        }

        private static DunGen.Door FindConnectionDoor(Doorway a, Doorway b)
        {
            DunGen.Door door = FindDoor(a);
            return door != null ? door : FindDoor(b);
        }

        private static DunGen.Door FindDoor(Doorway doorway)
        {
            if (doorway == null)
                return null;
            if (doorway.UsedDoorPrefabInstance != null)
            {
                DunGen.Door spawned = doorway.UsedDoorPrefabInstance.GetComponentInChildren<DunGen.Door>(true);
                if (spawned != null)
                    return spawned;
            }

            return doorway.GetComponentInChildren<DunGen.Door>(true);
        }

        private static bool IsUnsafeCombinedMesh(Mesh mesh)
        {
            return mesh != null &&
                   mesh.name.StartsWith("Combined Mesh") &&
                   mesh.vertexCount > 512;
        }

        private static void Hide(List<GrokDoorwaySpillReceiver> receivers)
        {
            for (int i = 0; i < receivers.Count; i++)
            {
                if (receivers[i] != null)
                    receivers[i].Hide();
            }
        }

        private static void DestroyReceivers(List<GrokDoorwaySpillReceiver> receivers)
        {
            for (int i = 0; i < receivers.Count; i++)
            {
                if (receivers[i] != null)
                    receivers[i].DestroyOverlay();
            }

            receivers.Clear();
        }
    }
}
