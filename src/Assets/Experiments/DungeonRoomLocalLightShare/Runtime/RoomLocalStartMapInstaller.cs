using System.Collections.Generic;
using System.Text;
using DunGen;
using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    /// <summary>
    /// Removable StartMap hook. Wires cookie-spot connections after DunGen builds.
    /// Does not edit tiles, bake data, MapList, Flow, or V2 prefabs.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class RoomLocalStartMapInstaller : MonoBehaviour
    {
        public const string HookObjectName = RoomLocalLightShareContract.StartMapHookObjectName;

        [SerializeField] private RuntimeDungeon runtimeDungeon;
        [SerializeField] private RoomLocalMatrixCatalog catalog;
        [SerializeField] private bool connectionEnabled = true;
        [SerializeField] private bool enableBounce;
        [SerializeField] private bool enableDoorProbe = true;
        [SerializeField] private bool disableGrokDoorwayHook = true;
        [SerializeField] private bool logSummary = true;
        // DunGen invokes AfterBuiltIn highest-to-lowest. MyCustomPostProcessor is 1 and
        // rewrites every tile renderer to Dungeon-only, so this must run after it.
        [SerializeField] private int postProcessPriority;
        [SerializeField] private DoorRealtimeMode doorRealtimeMode = DoorRealtimeMode.CookieReceive;

        private readonly List<LiveConnection> connections = new List<LiveConnection>();
        private readonly HashSet<Doorway> seenDoorways = new HashSet<Doorway>();
        private readonly Dictionary<string, OutgoingPortalMap> liveOutgoing =
            new Dictionary<string, OutgoingPortalMap>();
        private readonly Dictionary<Transform, ClosedDoorPose> closedDoorPoses =
            new Dictionary<Transform, ClosedDoorPose>();
        private readonly List<Transform> expiredDoorPoses = new List<Transform>();
        private bool postProcessRegistered;
        private NetworkDungeonController mapController;
        private int lastDungeonConnectionCount;
        private int lastWiredCount;
        private int lastSkippedMissingOutgoing;
        private int lastLiveCapturedOutgoing;
        private int lastSkippedMissingLighting;
        private int lastDoorLeafCount;
        private int lastProbeCount;
        private string lastSummary = "not built";
        private float nextDoorSearchTime;
        private Dungeon lastDungeon;
        private int cookieEnvironmentRetryFrames;

        public int WiredCount => lastWiredCount;
        public int DoorLeafCount => lastDoorLeafCount;
        public string LastSummary => lastSummary;

        public void Configure(RuntimeDungeon dungeon, RoomLocalMatrixCatalog matrixCatalog)
        {
            runtimeDungeon = dungeon;
            catalog = matrixCatalog;
        }

        public void SetConnectionEnabled(bool enabled)
        {
            connectionEnabled = enabled;
            for (int i = 0; i < connections.Count; i++)
            {
                if (connections[i].Connection != null)
                    connections[i].Connection.SetConnectionEnabled(enabled);
            }
            DungeonTileProbeRegistry.Active?.ForceRefreshReceivers();
        }

        private void Awake()
        {
            SuppressGrokDoorwayHook();
            if (runtimeDungeon == null)
                runtimeDungeon = FindFirstObjectByType<RuntimeDungeon>();
            mapController = runtimeDungeon != null ? runtimeDungeon.GetComponent<NetworkDungeonController>() : null;
            TryRegisterPostProcess();
        }

        private void OnEnable()
        {
            SuppressGrokDoorwayHook();
            if (runtimeDungeon == null)
                runtimeDungeon = FindFirstObjectByType<RuntimeDungeon>();
            TryRegisterPostProcess();
            TryRebuildExistingDungeon();
        }

        private void OnDisable()
        {
            UnregisterPostProcess();
            Teardown();
        }

        private void LateUpdate()
        {
            if (cookieEnvironmentRetryFrames > 0)
            {
                StampCookieEnvironment(lastDungeon);
                cookieEnvironmentRetryFrames--;
            }

            if (Time.unscaledTime < nextDoorSearchTime)
                return;

            for (int i = connections.Count - 1; i >= 0; i--)
            {
                LiveConnection live = connections[i];

                // The dungeon was cleared (day end / run restart): the doorways are destroyed
                // Unity objects. Drop the connection instead of touching them every frame.
                if (live == null || (live.DoorwayA == null && live.DoorwayB == null))
                {
                    UnregisterProbeVisibility(live);
                    RestoreProductionReceiver(live);
                    if (live != null && live.Host != null)
                        Destroy(live.Host);
                    connections.RemoveAt(i);
                    continue;
                }

                if (live.Leaf != null)
                {
                    RegisterProbeVisibility(live);
                    continue;
                }
                TryBindDoor(live);
            }

            nextDoorSearchTime = Time.unscaledTime + 0.25f;
        }

        private void TryRegisterPostProcess()
        {
            if (postProcessRegistered || runtimeDungeon == null || runtimeDungeon.Generator == null)
                return;

            runtimeDungeon.Generator.RegisterPostProcessStep(
                OnDungeonPostProcess,
                postProcessPriority,
                DunGen.PostProcessPhase.AfterBuiltIn);
            runtimeDungeon.Generator.Cleared += OnDungeonCleared;
            postProcessRegistered = true;
        }

        private void UnregisterPostProcess()
        {
            if (!postProcessRegistered || runtimeDungeon == null || runtimeDungeon.Generator == null)
                return;

            runtimeDungeon.Generator.UnregisterPostProcessStep(OnDungeonPostProcess);
            runtimeDungeon.Generator.Cleared -= OnDungeonCleared;
            postProcessRegistered = false;
        }

        private void OnDungeonCleared()
        {
            Teardown();
            lastDungeon = null;
            cookieEnvironmentRetryFrames = 0;
            closedDoorPoses.Clear();
        }

        private void OnDungeonPostProcess(DunGen.DungeonGenerator generator)
        {
            Rebuild(generator);
        }

        private void TryRebuildExistingDungeon()
        {
            if (runtimeDungeon == null || runtimeDungeon.Generator == null)
                return;
            if (runtimeDungeon.Generator.CurrentDungeon == null)
                return;
            Rebuild(runtimeDungeon.Generator);
        }

        public void Rebuild(DunGen.DungeonGenerator generator)
        {
            Teardown();
            lastDungeonConnectionCount = 0;
            lastWiredCount = 0;
            lastSkippedMissingOutgoing = 0;
            lastLiveCapturedOutgoing = 0;
            lastSkippedMissingLighting = 0;
            lastDoorLeafCount = 0;
            lastProbeCount = 0;
            lastDungeon = null;
            cookieEnvironmentRetryFrames = 0;

            Dungeon dungeon = generator != null ? generator.CurrentDungeon : null;
            if (dungeon == null || dungeon.Connections == null)
            {
                lastSummary = "FAIL: dungeon is missing.";
                if (logSummary)
                    Debug.LogWarning("[RoomLocalLightShare] " + lastSummary, this);
                return;
            }

            IReadOnlyList<DoorwayConnection> dungeonConnections = dungeon.Connections;
            lastDungeonConnectionCount = dungeonConnections.Count;
            seenDoorways.Clear();

            for (int i = 0; i < dungeonConnections.Count; i++)
            {
                DoorwayConnection pair = dungeonConnections[i];
                Doorway doorwayA = pair.A;
                Doorway doorwayB = pair.B;
                if (doorwayA == null || doorwayB == null)
                    continue;
                if (!seenDoorways.Add(doorwayA) || !seenDoorways.Add(doorwayB))
                    continue;

                DungeonTileLightmapSwitcher lightingA = FindSwitcher(doorwayA.Tile);
                DungeonTileLightmapSwitcher lightingB = FindSwitcher(doorwayB.Tile);
                if (lightingA == null || lightingB == null)
                {
                    lastSkippedMissingLighting++;
                    continue;
                }

                if (!TryResolveOutgoing(doorwayA, out RoomLocalMatrixCatalog.DoorwayEntry entryA) ||
                    !TryResolveOutgoing(doorwayB, out RoomLocalMatrixCatalog.DoorwayEntry entryB) ||
                    entryA.Outgoing == null ||
                    entryB.Outgoing == null)
                {
                    lastSkippedMissingOutgoing++;
                    continue;
                }

                LiveConnection live = CreateConnection(
                    doorwayA,
                    doorwayB,
                    lightingA,
                    lightingB,
                    entryA,
                    entryB);
                connections.Add(live);
                lastWiredCount++;
            }

            lastDungeon = dungeon;
            StampCookieEnvironment(dungeon);
            cookieEnvironmentRetryFrames = 8;

            lastSummary = BuildSummary();
            if (logSummary)
                Debug.Log("[RoomLocalLightShare] " + lastSummary, this);
        }

        public string BuildSummary()
        {
            var text = new StringBuilder();
            text.Append("StartMap hook connections dungeon=");
            text.Append(lastDungeonConnectionCount);
            text.Append(" wired=");
            text.Append(lastWiredCount);
            text.Append(" skippedOutgoing=");
            text.Append(lastSkippedMissingOutgoing);
            text.Append(" liveOutgoing=");
            text.Append(lastLiveCapturedOutgoing);
            text.Append(" skippedLighting=");
            text.Append(lastSkippedMissingLighting);
            text.Append(" doorLeaf=");
            text.Append(lastDoorLeafCount);
            text.Append(" probe=");
            text.Append(lastProbeCount);
            text.Append(" bounce=");
            text.Append(enableBounce ? "on" : "off");
            text.Append(" cookie=");
            text.Append(connectionEnabled ? "on" : "off");
            text.Append(" grokHook=");
            text.Append(disableGrokDoorwayHook ? "suppressed" : "left");
            text.Append(" mode=");
            text.Append(doorRealtimeMode);
            return text.ToString();
        }

        public string DumpStatus()
        {
            var text = new StringBuilder();
            text.AppendLine(lastSummary);
            for (int i = 0; i < connections.Count; i++)
            {
                LiveConnection live = connections[i];
                text.Append(i);
                text.Append(": ");
                text.Append(DescribeDoorway(live.DoorwayA));
                text.Append(" <-> ");
                text.Append(DescribeDoorway(live.DoorwayB));
                text.Append(" leaf=");
                text.Append(live.Leaf != null ? live.Leaf.name : "none");
                text.Append(" probe=");
                text.Append(live.Probe != null && live.Probe.IsApplied);
                if (live.Connection != null && live.Connection.IsFaultLatched)
                {
                    text.Append(" FAULT=");
                    text.Append(live.Connection.FaultReason);
                }

                text.AppendLine();
            }

            return text.ToString();
        }

        public string ApplyContrastAndOpenFirst(float openFraction)
        {
            if (connections.Count == 0)
                return "FAIL: no wired connections.";

            LiveConnection live = connections[0];
            if (live.LightingA != null)
                live.LightingA.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P0);
            if (live.LightingB != null)
                live.LightingB.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P100);
            if (live.Angle != null && live.Leaf != null)
                live.Angle.ApplyOpenFraction(Mathf.Clamp01(openFraction));
            else if (live.Leaf != null)
            {
                live.Leaf.localRotation = RoomLocalLightShareMath.DoorLocalRotationForOpenFraction(
                    Quaternion.identity,
                    Vector3.up,
                    90f,
                    Mathf.Clamp01(openFraction));
            }

            return
                "PASS contrast A=P0 B=P100 open=" + openFraction.ToString("0.00") + "\n" +
                DescribeDoorway(live.DoorwayA) + " <-> " + DescribeDoorway(live.DoorwayB) +
                " leaf=" + (live.Leaf != null ? live.Leaf.name : "none");
        }

        public static RoomLocalStartMapInstaller FindActive()
        {
            return FindFirstObjectByType<RoomLocalStartMapInstaller>();
        }

        public static string DumpActiveStatus()
        {
            RoomLocalStartMapInstaller installer = FindActive();
            if (installer == null)
                return "FAIL: RoomLocalLightShare StartMap hook is not in the loaded scenes.";
            return installer.DumpStatus();
        }

        public static string ApplyActiveContrastAndOpen(float openFraction)
        {
            RoomLocalStartMapInstaller installer = FindActive();
            if (installer == null)
                return "FAIL: RoomLocalLightShare StartMap hook is not in the loaded scenes.";
            return installer.ApplyContrastAndOpenFirst(openFraction);
        }

        private LiveConnection CreateConnection(
            Doorway doorwayA,
            Doorway doorwayB,
            DungeonTileLightmapSwitcher lightingA,
            DungeonTileLightmapSwitcher lightingB,
            RoomLocalMatrixCatalog.DoorwayEntry entryA,
            RoomLocalMatrixCatalog.DoorwayEntry entryB)
        {
            var host = new GameObject(
                "RoomLocalConnection_" +
                RoomName(doorwayA) + "_" +
                RoomName(doorwayB));
            host.transform.SetParent(transform, false);
            host.SetActive(false);

            var live = new LiveConnection
            {
                Host = host,
                DoorwayA = doorwayA,
                DoorwayB = doorwayB,
                LightingA = lightingA,
                LightingB = lightingB,
                EntryA = entryA,
                EntryB = entryB
            };

            live.Angle = host.AddComponent<RoomLocalDoorAngleSource>();
            live.Connection = host.AddComponent<RoomLocalConnection>();
            live.Connection.Configure(
                lightingA.transform,
                lightingB.transform,
                doorwayA.transform,
                doorwayB.transform,
                entryA.Outgoing,
                entryB.Outgoing,
                enableBounce ? entryA.IncomingBounce : null,
                enableBounce ? entryB.IncomingBounce : null,
                null,
                live.Angle);
            live.Connection.SetConnectionEnabled(connectionEnabled);

            TryBindDoor(live);
            host.SetActive(true);
            return live;
        }

        private void TryBindDoor(LiveConnection live)
        {
            if (live == null || live.Host == null || live.Connection == null)
                return;
            if (live.Leaf != null)
                return;
            if (live.DoorwayA == null && live.DoorwayB == null)
                return; // doorways destroyed with the dungeon

            Transform leaf = FindDoorLeaf(live.DoorwayA, live.DoorwayB);
            if (leaf == null)
            {
                live.Connection.SetOpenPassage(IsExplicitOpenPassage(live.DoorwayA, live.DoorwayB));
                RegisterProbeVisibility(live);
                return;
            }

            live.Leaf = leaf;
            live.Connection.SetOpenPassage(false);
            ClosedDoorPose pose = ResolveClosedDoorPose(leaf);
            live.Angle.Configure(leaf, pose.Rotation, pose.Axis, pose.OpenAngle);
            lastDoorLeafCount++;
            RegisterProbeVisibility(live);

            if (!enableDoorProbe || live.Probe != null)
                return;

            var productionReceiver = leaf.GetComponent<DungeonDoorDualSideProbeReceiver>();
            if (productionReceiver != null && productionReceiver.enabled)
            {
                live.DisabledProductionReceiver = productionReceiver;
                productionReceiver.enabled = false;
            }

            live.Probe = live.Host.AddComponent<RoomLocalDoorProbeDriver>();
            live.Probe.Configure(
                leaf,
                live.LightingA.transform,
                live.LightingB.transform,
                live.DoorwayA.transform,
                live.DoorwayB.transform,
                live.LightingA,
                live.LightingB,
                live.EntryA.Outgoing,
                live.EntryB.Outgoing);
            live.Probe.SetRealtimeMode(doorRealtimeMode);
            lastProbeCount++;
        }

        private ClosedDoorPose ResolveClosedDoorPose(Transform leaf)
        {
            global::Door door = leaf.GetComponent<global::Door>();
            if (door != null)
            {
                // Door captures this before motion. Its current pose and IsOpen target are
                // not reliable closed references during a rebuild or a late network bind.
                Quaternion closed = door.ClosedLocalRotation;
                Vector3 openEuler = closed.eulerAngles;
                switch (door.rotationOrientation)
                {
                    case global::Door.rotOrient.Y_Axis_Up:
                        openEuler.y += door.doorOpenAngle;
                        break;
                    case global::Door.rotOrient.Z_Axis_Up:
                        openEuler.z += door.doorOpenAngle;
                        break;
                    default:
                        if (!door.applyRotationFix)
                            openEuler.x += door.doorOpenAngle;
                        else
                            openEuler = door.rotationAxisFix == global::Door.rotFixAxis.Y
                                ? new Vector3(openEuler.x + 90f, 90f, 270f)
                                : new Vector3(openEuler.x + 90f, 270f, 90f);
                        break;
                }
                Quaternion delta = Quaternion.Inverse(closed) * Quaternion.Euler(openEuler);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) { angle = 360f - angle; axis = -axis; }
                if (float.IsNaN(axis.x) || axis.sqrMagnitude < 0.000001f)
                    axis = Vector3.up;
                var pose = new ClosedDoorPose { Rotation = closed, Axis = axis.normalized, OpenAngle = angle };
                closedDoorPoses[leaf] = pose;
                return pose;
            }
            if (closedDoorPoses.TryGetValue(leaf, out ClosedDoorPose cached))
                return cached;
            // Non-gameplay preview leaves have no Door metadata. Capture once, then preserve
            // that reference across Teardown/Rebuild instead of recapturing an opened pose.
            var initial = new ClosedDoorPose { Rotation = leaf.localRotation, Axis = Vector3.up, OpenAngle = 90f };
            closedDoorPoses.Add(leaf, initial);
            return initial;
        }

        private static void RegisterProbeVisibility(LiveConnection live)
        {
            if (live.Connection == null)
            {
                UnregisterProbeVisibility(live);
                return;
            }
            DungeonTileProbeRegistry registry = DungeonTileProbeRegistry.Active;
            if (live.VisibilityRegistry != registry)
            {
                UnregisterProbeVisibility(live);
                live.VisibilityRegistry = registry;
            }
            if (registry != null && live.Angle != null)
                registry.RegisterDoorwayVisibility(live.DoorwayA, live.DoorwayB, live.Angle, live.Connection);
        }

        private static void UnregisterProbeVisibility(LiveConnection live)
        {
            if (live == null)
                return;
            if (live.VisibilityRegistry != null)
                live.VisibilityRegistry.UnregisterDoorwayVisibility(live.Angle, live.Connection);
            live.VisibilityRegistry = null;
        }

        private bool TryResolveOutgoing(
            Doorway doorway,
            out RoomLocalMatrixCatalog.DoorwayEntry entry)
        {
            entry = null;
            if (doorway == null || doorway.Tile == null)
                return false;

            string roomId = ResolveRoomId(doorway.Tile);
            if (!TryRelativePath(doorway.Tile.transform, doorway.transform, out string path))
                return false;
            // A new map may reuse room names. Never apply the previous map's captured portal lighting to it.
            bool matchesMap = mapController == null || (mapController.MapList != null &&
                catalog != null && catalog.SourceFlowPath == mapController.MapList.GetFlowAssetPath(mapController.CurrentFlowIndex));
            if (catalog != null && matchesMap &&
                catalog.TryFindDoorway(roomId, path, out _, out entry) &&
                entry != null &&
                entry.Outgoing != null)
                return true;

            return TryCaptureLiveOutgoing(doorway, roomId, path, out entry);
        }

        private bool TryCaptureLiveOutgoing(
            Doorway doorway,
            string roomId,
            string path,
            out RoomLocalMatrixCatalog.DoorwayEntry entry)
        {
            entry = null;
            string key = roomId + "\n" + path;
            if (!liveOutgoing.TryGetValue(key, out OutgoingPortalMap map) || map == null)
            {
                DungeonTileLightmapSwitcher switcher = FindSwitcher(doorway.Tile);
                if (switcher == null)
                    return false;

                DungeonTileBakeData power0 = switcher.GetBakeData(
                    DungeonTileLightmapSwitcher.PowerLevel.P0);
                DungeonTileBakeData power100 = switcher.GetBakeData(
                    DungeonTileLightmapSwitcher.PowerLevel.P100);
                map = RoomLocalOutgoingFactory.CreateRuntime(
                    roomId,
                    path,
                    switcher.transform,
                    doorway.transform,
                    power0,
                    power100);
                if (map == null)
                    return false;
                liveOutgoing[key] = map;
                lastLiveCapturedOutgoing++;
            }

            string kind = path.IndexOf("Door_LG", System.StringComparison.OrdinalIgnoreCase) >= 0
                ? "LG"
                : "SM";
            entry = new RoomLocalMatrixCatalog.DoorwayEntry(path, kind, map, null);
            return entry.Outgoing != null;
        }

        private static bool TryRelativePath(Transform root, Transform target, out string path)
        {
            path = string.Empty;
            if (root == null || target == null)
                return false;
            try
            {
                path = RoomLocalRendererKeys.GetBareRelativePath(root, target);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveRoomId(Tile tile)
        {
            if (tile == null)
                return string.Empty;
            if (tile.Prefab != null && !string.IsNullOrEmpty(tile.Prefab.name))
                return tile.Prefab.name;
            return tile.gameObject != null ? tile.gameObject.name : string.Empty;
        }

        private static string RoomName(Doorway doorway)
        {
            return RoomLocalEndpointKeys.StripRotationSuffix(ResolveRoomId(doorway != null ? doorway.Tile : null));
        }

        private static string DescribeDoorway(Doorway doorway)
        {
            if (doorway == null || doorway.Tile == null)
                return "null";
            if (!TryRelativePath(doorway.Tile.transform, doorway.transform, out string path))
                path = doorway.name;
            return RoomName(doorway) + "/" + path;
        }

        private static Transform FindDoorLeaf(Doorway first, Doorway second)
        {
            if (first == null || second == null)
                return null;
            Transform leaf = FindLeafOnDoorway(first);
            if (leaf != null)
                return leaf;
            leaf = FindLeafOnDoorway(second);
            if (leaf != null)
                return leaf;

            DunGen.Door[] doors = FindObjectsByType<DunGen.Door>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < doors.Length; i++)
            {
                DunGen.Door candidate = doors[i];
                if (candidate == null)
                    continue;
                if ((candidate.DoorwayA == first && candidate.DoorwayB == second) ||
                    (candidate.DoorwayA == second && candidate.DoorwayB == first))
                {
                    Transform named = FindNamedLeaf(candidate.transform);
                    if (named != null)
                        return named;
                }
            }

            return null;
        }

        private static bool IsExplicitOpenPassage(Doorway first, Doorway second)
        {
            if (first == null || second == null || first.ConnectedDoorway != second ||
                second.ConnectedDoorway != first || first.IsLocked || second.IsLocked)
                return false;
            return HasNoAuthoredConnector(first) && HasNoAuthoredConnector(second);
        }

        private static bool HasNoAuthoredConnector(Doorway doorway)
        {
            if (doorway.UsedDoorPrefabInstance != null || doorway.DoorComponent != null ||
                doorway.ConnectorPrefabWeights.HasAnyViableEntries())
                return false;
            // DunGen skips SpawnDoorPrefab only when neither endpoint has viable connectors.
            // A missing network-spawned leaf is unresolved while its authoring still expects one.
            if (doorway.ConnectorSceneObjects != null)
                for (int i = 0; i < doorway.ConnectorSceneObjects.Count; i++)
                    if (doorway.ConnectorSceneObjects[i] != null)
                        return false;
            return true;
        }

        private static Transform FindLeafOnDoorway(Doorway doorway)
        {
            if (doorway == null)
                return null;
            if (doorway.UsedDoorPrefabInstance != null)
            {
                Transform named = FindNamedLeaf(doorway.UsedDoorPrefabInstance.transform);
                if (named != null)
                    return named;
            }

            if (doorway.DoorComponent != null)
                return FindNamedLeaf(doorway.DoorComponent.transform);
            return null;
        }

        private static Transform FindNamedLeaf(Transform root)
        {
            if (root == null)
                return null;
            if (root.name == RoomLocalLightShareContract.DoorLeafPath)
                return root;
            Transform child = root.Find(RoomLocalLightShareContract.DoorLeafPath);
            if (child != null)
                return child;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null &&
                    children[i].name == RoomLocalLightShareContract.DoorLeafPath)
                    return children[i];
            }

            Renderer renderer = root.GetComponent<Renderer>();
            return renderer != null ? root : null;
        }

        private static DungeonTileLightmapSwitcher FindSwitcher(Tile tile)
        {
            if (tile == null)
                return null;
            var onRoot = tile.GetComponent<DungeonTileLightmapSwitcher>();
            return onRoot != null
                ? onRoot
                : tile.GetComponentInChildren<DungeonTileLightmapSwitcher>(true);
        }

        private static void StampCookieEnvironment(Dungeon dungeon)
        {
            if (dungeon == null || dungeon.AllTiles == null)
                return;

            for (int tileIndex = 0; tileIndex < dungeon.AllTiles.Count; tileIndex++)
            {
                Tile tile = dungeon.AllTiles[tileIndex];
                if (tile != null)
                    RoomLocalRenderingLayers.AddCookieEnvironment(tile.transform);
            }
        }

        private void SuppressGrokDoorwayHook()
        {
            if (!disableGrokDoorwayHook)
                return;

            GameObject grok = GameObject.Find(RoomLocalLightShareContract.GrokDoorwayStartMapHookObjectName);
            if (grok == null)
            {
                GameObject[] roots = gameObject.scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (roots[i] != null &&
                        roots[i].name == RoomLocalLightShareContract.GrokDoorwayStartMapHookObjectName)
                    {
                        grok = roots[i];
                        break;
                    }
                }
            }

            if (grok != null && grok.activeSelf)
                grok.SetActive(false);
        }

        private void Teardown()
        {
            for (int i = 0; i < connections.Count; i++)
            {
                LiveConnection live = connections[i];
                if (live.Leaf != null && live.Angle != null && live.Angle.IsConfigured)
                    closedDoorPoses[live.Leaf] = new ClosedDoorPose {
                        Rotation = live.Angle.ClosedLocalRotation,
                        Axis = live.Angle.LocalHingeAxis,
                        OpenAngle = live.Angle.OpenAngleDegrees };
                UnregisterProbeVisibility(live);
                RestoreProductionReceiver(live);
                if (live.Host == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(live.Host);
                else
                    DestroyImmediate(live.Host);
            }

            connections.Clear();
            foreach (KeyValuePair<string, OutgoingPortalMap> pair in liveOutgoing)
            {
                OutgoingPortalMap map = pair.Value;
                if (map == null)
                    continue;
                RoomLocalPortalSampler.DestroyTexture(map.Power0Hdr);
                RoomLocalPortalSampler.DestroyTexture(map.Power100Hdr);
                RoomLocalPortalSampler.DestroyTexture(map.Power0CookieTexture);
                RoomLocalPortalSampler.DestroyTexture(map.Power100CookieTexture);
                if (Application.isPlaying)
                    Destroy(map);
                else
                    DestroyImmediate(map);
            }

            liveOutgoing.Clear();
            expiredDoorPoses.Clear();
            foreach (var pair in closedDoorPoses)
                if (pair.Key == null)
                    expiredDoorPoses.Add(pair.Key);
            for (int i = 0; i < expiredDoorPoses.Count; i++)
                closedDoorPoses.Remove(expiredDoorPoses[i]);
        }

        private static void RestoreProductionReceiver(LiveConnection live)
        {
            if (live == null)
                return;
            // Stop the temporary driver and restore its captured renderer state first.
            if (live.Host != null)
                live.Host.SetActive(false);
            DungeonDoorDualSideProbeReceiver receiver = live.DisabledProductionReceiver;
            live.DisabledProductionReceiver = null;
            if (receiver != null)
                receiver.enabled = true;
        }

        private struct ClosedDoorPose
        {
            public Quaternion Rotation;
            public Vector3 Axis;
            public float OpenAngle;
        }

        private sealed class LiveConnection
        {
            public GameObject Host;
            public Doorway DoorwayA;
            public Doorway DoorwayB;
            public DungeonTileLightmapSwitcher LightingA;
            public DungeonTileLightmapSwitcher LightingB;
            public RoomLocalMatrixCatalog.DoorwayEntry EntryA;
            public RoomLocalMatrixCatalog.DoorwayEntry EntryB;
            public RoomLocalConnection Connection;
            public RoomLocalDoorAngleSource Angle;
            public RoomLocalDoorProbeDriver Probe;
            public Transform Leaf;
            public DungeonTileProbeRegistry VisibilityRegistry;
            public DungeonDoorDualSideProbeReceiver DisabledProductionReceiver;
        }
    }
}
