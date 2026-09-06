using System;
using System.Collections.Generic;
using DunGen;
using UnityEngine;

namespace GrokDoorwayLighting
{
    /// <summary>
    /// Removable StartMap hook. Adds doorway GI overlays after DunGen builds a
    /// dungeon. Does not change tile prefabs, bake data, MapList, or Flow.
    /// Delete this GameObject to remove the experiment.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class GrokDoorwayStartMapInstaller : MonoBehaviour
    {
        public const string HookObjectName = "GrokDoorwayLighting_StartMapHook";

        [Serializable]
        public struct Settings
        {
            public float blendDepth;
            public float lateralFade;
            public float verticalPadding;
            public float jambAllowance;
            public float receiverSearchPadding;
            public float spillScale;
            public float bounceReflectance;
            public float bounceRange;
            public float bounceScale;
            public float revealScale;

            public static Settings Default()
            {
                return new Settings
                {
                    blendDepth = 3.5f,
                    lateralFade = 0.85f,
                    verticalPadding = 0.25f,
                    jambAllowance = 0.12f,
                    receiverSearchPadding = 2.75f,
                    spillScale = 1.0f,
                    bounceReflectance = 0.20f,
                    bounceRange = 2.5f,
                    bounceScale = 1.15f,
                    revealScale = 0f
                };
            }
        }

        [SerializeField] private RuntimeDungeon runtimeDungeon;
        [SerializeField] private bool visualizationEnabled = true;
        [SerializeField] private bool captureMissingPortalsAtRuntime = true;
        [SerializeField] private bool logSummary = true;
        [SerializeField] private int postProcessPriority = 95;
        [SerializeField] private List<GrokDoorwayPortalMap> preauthoredPortalMaps = new List<GrokDoorwayPortalMap>();
        [SerializeField] private Settings settings = Settings.Default();

        private readonly List<GrokDoorwayConnectionRuntime> _connections = new List<GrokDoorwayConnectionRuntime>();
        private readonly Dictionary<DungeonTileLightmapSwitcher, Action<DungeonTileLightmapSwitcher.PowerLevel>> _powerHandlers =
            new Dictionary<DungeonTileLightmapSwitcher, Action<DungeonTileLightmapSwitcher.PowerLevel>>();

        private GrokDoorwayPortalLibrary _library;
        private bool _postProcessRegistered;
        private int _lastConnectionCount;
        private int _lastApplyCount;

        public bool VisualizationEnabled => visualizationEnabled;
        public int ConnectionCount => _connections.Count;
        public int LastApplyCount => _lastApplyCount;

        public void Configure(RuntimeDungeon dungeon, IList<GrokDoorwayPortalMap> maps)
        {
            runtimeDungeon = dungeon;
            preauthoredPortalMaps.Clear();
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    if (maps[i] != null)
                        preauthoredPortalMaps.Add(maps[i]);
                }
            }
        }

        public void SetVisualization(bool enabled)
        {
            visualizationEnabled = enabled;
            ApplyAll();
        }

        private void Awake()
        {
            if (runtimeDungeon == null)
                runtimeDungeon = FindFirstObjectByType<RuntimeDungeon>();
            TryRegisterPostProcess();
        }

        private void OnEnable()
        {
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

        private void TryRegisterPostProcess()
        {
            if (_postProcessRegistered || runtimeDungeon == null || runtimeDungeon.Generator == null)
                return;

            runtimeDungeon.Generator.RegisterPostProcessStep(
                OnDungeonPostProcess,
                postProcessPriority,
                DunGen.PostProcessPhase.AfterBuiltIn);
            _postProcessRegistered = true;
        }

        private void UnregisterPostProcess()
        {
            if (!_postProcessRegistered || runtimeDungeon == null || runtimeDungeon.Generator == null)
                return;

            runtimeDungeon.Generator.UnregisterPostProcessStep(OnDungeonPostProcess);
            _postProcessRegistered = false;
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
            _library = new GrokDoorwayPortalLibrary(
                preauthoredPortalMaps,
                captureMissingPortalsAtRuntime,
                logSummary);

            Dungeon dungeon = generator != null ? generator.CurrentDungeon : null;
            if (dungeon == null || dungeon.AllTiles == null)
                return;

            var seen = new HashSet<Doorway>();
            for (int i = 0; i < dungeon.AllTiles.Count; i++)
            {
                Tile tile = dungeon.AllTiles[i];
                if (tile == null || tile.UsedDoorways == null)
                    continue;

                for (int d = 0; d < tile.UsedDoorways.Count; d++)
                {
                    Doorway door = tile.UsedDoorways[d];
                    if (door == null || door.ConnectedDoorway == null)
                        continue;
                    if (!seen.Add(door) || !seen.Add(door.ConnectedDoorway))
                        continue;

                    DungeonTileLightmapSwitcher lightingA = FindSwitcher(door.Tile);
                    DungeonTileLightmapSwitcher lightingB = FindSwitcher(door.ConnectedDoorway.Tile);
                    if (lightingA == null || lightingB == null)
                        continue;

                    var connection = new GrokDoorwayConnectionRuntime(
                        door,
                        door.ConnectedDoorway,
                        lightingA,
                        lightingB);
                    _connections.Add(connection);
                    Subscribe(lightingA);
                    Subscribe(lightingB);
                }
            }

            _lastConnectionCount = _connections.Count;
            ApplyAll();

            if (logSummary)
            {
                Debug.Log(
                    "[GrokDoorway] StartMap hook connections=" + _connections.Count +
                    " visualization=" + visualizationEnabled +
                    " (delete '" + HookObjectName + "' to remove)",
                    this);
            }
        }

        private void ApplyAll()
        {
            _lastApplyCount = 0;
            for (int i = 0; i < _connections.Count; i++)
            {
                if (_connections[i] == null)
                    continue;
                _connections[i].Apply(visualizationEnabled, _library, settings);
                _lastApplyCount++;
            }
        }

        private void Subscribe(DungeonTileLightmapSwitcher lighting)
        {
            if (lighting == null || _powerHandlers.ContainsKey(lighting))
                return;

            Action<DungeonTileLightmapSwitcher.PowerLevel> handler = _ => ApplyAll();
            lighting.PowerLevelApplied += handler;
            _powerHandlers.Add(lighting, handler);
        }

        private void Teardown()
        {
            foreach (KeyValuePair<DungeonTileLightmapSwitcher, Action<DungeonTileLightmapSwitcher.PowerLevel>> pair in _powerHandlers)
            {
                if (pair.Key != null)
                    pair.Key.PowerLevelApplied -= pair.Value;
            }

            _powerHandlers.Clear();

            for (int i = 0; i < _connections.Count; i++)
            {
                if (_connections[i] != null)
                    _connections[i].Teardown();
            }

            _connections.Clear();
            if (_library != null)
            {
                _library.DisposeRuntimeCaptures();
                _library = null;
            }
        }

        private static DungeonTileLightmapSwitcher FindSwitcher(Tile tile)
        {
            if (tile == null)
                return null;
            var onRoot = tile.GetComponent<DungeonTileLightmapSwitcher>();
            return onRoot != null ? onRoot : tile.GetComponentInChildren<DungeonTileLightmapSwitcher>(true);
        }
    }
}
