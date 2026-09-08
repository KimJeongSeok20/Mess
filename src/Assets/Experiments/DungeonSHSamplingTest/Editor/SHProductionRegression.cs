using System;
using System.Collections.Generic;
using System.Reflection;
using DunGen;
using DungeonRoomLocalLightShare;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DungeonSHSamplingTest.Editor
{
    public static class SHProductionRegression
    {
        [Serializable] private sealed class CheckResult
        {
            public string name, detail;
            public bool passed;
        }
        [Serializable] private sealed class SweepResult
        {
            public float angleDegrees, spacing = 0.05f, maximumStep, allowedStep;
            public float[] l0;
        }
        [Serializable] private sealed class Report
        {
            public bool numericOnly = true, visualVerified = false;
            public string subject = "Production DungeonTileProbeRegistry.TrySample / TrySampleForTile";
            public List<CheckResult> checks = new List<CheckResult>();
            public List<SweepResult> continuity = new List<SweepResult>();
            public List<BenchmarkResult> warmBenchmarks = new List<BenchmarkResult>();
        }
        [Serializable] private sealed class BenchmarkResult
        {
            public string scenario;
            public int warmupQueries = 64, measuredQueries = 1000, successfulQueries, visibilityRayCount;
            public double milliseconds, microsecondsPerQuery, managedBytesPerQuery;
            public long currentThreadManagedAllocatedBytes;
            public float l0Checksum;
            public bool performanceThresholdApplied = false;
        }
        private sealed class Room
        {
            public Tile tile;
            public DungeonTileLightmapSwitcher switcher;
            public DungeonTilePowerBakeSet power;
        }
        private struct Sample
        {
            public bool success;
            public SphericalHarmonicsL2 sh;
            public Vector4 occlusion;
            public DungeonTileProbeRegistry.SampleInfo info;
        }

        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Production SH regression requires Edit Mode.");
            Scene original = SceneManager.GetActiveScene();
            bool originalDirty = original.isDirty;
            int originalSceneCount = SceneManager.sceneCount;
            LightmapData[] originalMaps = LightmapSettings.lightmaps;
            LightmapsMode originalMode = LightmapSettings.lightmapsMode;
            DungeonTileProbeRegistry originalRegistry = DungeonTileProbeRegistry.Active;
            var assets = new List<Object>();
            var report = new Report();
            Scene temporary = default;
            DungeonTileProbeRegistry registry = null;
            try
            {
                temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(temporary);
                var registryObject = new GameObject("Regression Registry - inactive, public queries only");
                registryObject.SetActive(false);
                registry = registryObject.AddComponent<DungeonTileProbeRegistry>();
                SetField(registry, "registerPostProcessStep", false);
                SetField(registry, "visibilityFilteringEnabled", true); // Never refresh original receivers globally.
                Room a = MakeRoom("Regression A", new Bounds(new Vector3(0f, 1.5f, -2f), new Vector3(8f, 3f, 4f)));
                BoxCollider wall = MakeBox("Wall_RegressionPartition", a.tile.transform,
                    new Vector3(0f, 1f, -2f), new Vector3(0.2f, 4f, 8f));
                Vector3 position = new Vector3(-1f, 1f, -2f);
                var candidates = new[] { Entry(0.5f, -2f, 100f), Entry(-3f, -2f, 2f),
                    Entry(-3.2f, -2f, 2f), Entry(-3.4f, -2f, 2f), Entry(-3.6f, -2f, 2f) };
                SetBakes(a, Bake(assets, candidates), null);
                Physics.SyncTransforms();
                Register(registry, a);
                Check(registry.VisibilityFilteringEnabled && registry.TileSetCount == 1,
                    "production_tile_registered", report);
                Sample selected = Query(registry, position, a.tile);
                Check(selected.success && EqualL0(selected.sh, 2f) && selected.info.blendedProbeCount == 4 &&
                    selected.info.rejectedProbeCount == 1 && selected.info.candidateProbeCount == 5 &&
                    selected.info.visibilityRayCount > 0, "blocked_nearest_replaced_by_four_visible_own_probes", report);
                Sample global = Query(registry, position);
                Check(global.success && Equal(global.sh, selected.sh) && global.info.hasTileData,
                    "public_global_query_matches_explicit_tile", report);
                report.warmBenchmarks.Add(Benchmark(registry, position, a.tile, "Own tile: 5 candidates, nearest blocked"));
                SetField(registry, "visibilityFilteringEnabled", false);
                Sample legacy = Query(registry, position, a.tile);
                // Independent reference: 100/1.55 and 2/2.05, 2/2.25, 2/2.45, normalized.
                Check(!registry.VisibilityFilteringEnabled && legacy.success && EqualL0(legacy.sh, 33.8425857f) &&
                    legacy.info.blendedProbeCount == 4 && legacy.info.visibilityRayCount == 0,
                    "filter_off_preserves_original_distance_weighting", report);
                SetField(registry, "visibilityFilteringEnabled", true);
                SetBakes(a, Bake(assets, Entry(0.5f, -2f, 100f), Entry(1f, -2f, 100f)), null);
                Register(registry, a);
                Sample blocked = Query(registry, position, a.tile);
                Check(!blocked.success && blocked.info.hasTileData && blocked.info.noVisibleProbes &&
                    blocked.info.blendedProbeCount == 0 && blocked.info.rejectedProbeCount == 2 && Finite(blocked),
                    "all_blocked_preserves_failure_reason_without_nan", report);
                Sample blockedGlobal = Query(registry, position);
                Check(!blockedGlobal.success && blockedGlobal.info.hasTileData && blockedGlobal.info.noVisibleProbes,
                    "global_query_preserves_all_blocked_reason", report);
                wall.isTrigger = true;
                Physics.SyncTransforms();
                Sample trigger = Query(registry, position, a.tile);
                Check(trigger.success && EqualL0(trigger.sh, 100f) && trigger.info.rejectedProbeCount == 0,
                    "production_trigger_is_not_occlusion", report);
                wall.isTrigger = false;
                wall.enabled = false;
                Physics.SyncTransforms();
                Sample disabled = Query(registry, position, a.tile);
                Check(disabled.success && EqualL0(disabled.sh, 100f), "disabled_wall_is_not_occlusion", report);
                wall.enabled = true;
                Physics.SyncTransforms();
                Sample inside = Query(registry, new Vector3(0f, 1f, -2f), a.tile);
                Check(!inside.success && inside.info.hasTileData && inside.info.noVisibleProbes && Finite(inside),
                    "inside_solid_blocker_is_invalid", report);
                wall.enabled = false;

                SetBakes(a, Bake(assets, Entry(0f, -0.8f, 2f)), null);
                Register(registry, a);
                Room b = MakeRoom("Regression B", new Bounds(new Vector3(0f, 1.5f, 2f), new Vector3(8f, 3f, 4f)));
                SetBakes(b, Bake(assets, Entry(0f, 0.8f, 10f)), Bake(assets, Entry(0f, 0.8f, 0f)));
                Register(registry, b);
                DoorwaySocket socket = ScriptableObject.CreateInstance<DoorwaySocket>();
                assets.Add(socket);
                SetField(socket, "size", new Vector2(2f, 2f));
                Doorway first = MakeDoorway(a, socket, Vector3.zero, false);
                Doorway second = MakeDoorway(b, socket, Vector3.zero, true);
                Transform leaf = new GameObject("Regression moving leaf").transform;
                leaf.position = new Vector3(-1f, 0f, 0f);
                MakeLeafGeometry(leaf, assets);
                var angle = leaf.gameObject.AddComponent<RoomLocalDoorAngleSource>();
                angle.Configure(leaf, Quaternion.identity, Vector3.up, 90f);
                Physics.SyncTransforms();
                registry.RegisterDoorwayVisibility(first, second, angle);
                registry.RegisterDoorwayVisibility(first, second, angle);
                registry.RegisterDoorwayVisibility(second, first, angle);
                Check(registry.DoorwayBlendZoneCount == 1, "doorway_registration_is_idempotent", report);
                Vector3 nearA = new Vector3(0f, 1f, -0.1f);
                Sample closedOn = Query(registry, nearA);
                b.switcher.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P0);
                Sample closedOff = Query(registry, nearA);
                Sample ownBOff = Query(registry, new Vector3(0f, 1f, 0.5f), b.tile);
                b.switcher.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P100);
                Sample closedAgain = Query(registry, nearA);
                Sample ownBOn = Query(registry, new Vector3(0f, 1f, 0.5f), b.tile);
                Check(ownBOff.success && ownBOn.success && EqualL0(ownBOff.sh, 0f) && EqualL0(ownBOn.sh, 10f),
                    "power_event_refreshes_registered_production_probe_data", report);
                Check(closedOn.success && closedOff.success && closedAgain.success && EqualL0(closedOn.sh, 2f) &&
                    Equal(closedOn.sh, closedOff.sh) && Equal(closedOn.sh, closedAgain.sh) &&
                    !closedOn.info.spatialBlendActive && Near(closedOn.info.tileBWeight, 0f),
                    "closed_door_neighbour_power_has_zero_influence", report);
                Sample throughClosed = Query(registry, nearA, b.tile);
                Check(!throughClosed.success && throughClosed.info.noVisibleProbes,
                    "closed_leaf_blocks_explicit_neighbour_probe_query", report);
                Sweep(registry, angle, 90f, 0.55f, report);
                report.warmBenchmarks.Add(Benchmark(registry, Vector3.up, null, "Open portal: two tile probes blended"));
                Sweep(registry, angle, 60f, 1.1f, report);
                angle.ApplyOpenFraction(1f);
                Physics.SyncTransforms();
                Sample outsideSide = Query(registry, new Vector3(1.1f, 1f, -0.05f));
                Sample above = Query(registry, new Vector3(0f, 2.1f, -0.05f));
                Sample below = Query(registry, new Vector3(0f, -0.1f, -0.05f));
                Check(outsideSide.success && EqualL0(outsideSide.sh, 2f) && !outsideSide.info.spatialBlendActive,
                    "outside_aperture_width_does_not_mix", report);
                Check(above.success && below.success && EqualL0(above.sh, 2f) && EqualL0(below.sh, 2f) &&
                    !above.info.spatialBlendActive && !below.info.spatialBlendActive,
                    "outside_aperture_height_does_not_mix", report);
                angle.ApplyOpenFraction(0f);
                Physics.SyncTransforms();
                registry.UnregisterDoorwayVisibility(angle);
                Sample unregistered = Query(registry, nearA, b.tile);
                Check(unregistered.success && EqualL0(unregistered.sh, 10f) && unregistered.info.doorBlockerCount == 0,
                    "unregister_removes_bound_leaf_visibility", report);
                registry.RegisterDoorwayVisibility(first, second, angle);
                Check(registry.DoorwayBlendZoneCount == 1 && !Query(registry, nearA, b.tile).success,
                    "reregister_rebinds_same_zone_and_closed_leaf", report);
                TestInternalDoor(registry, assets, report);
                TestLockerDoor(registry, assets, report);

                DoorwaySocket zeroSocket = ScriptableObject.CreateInstance<DoorwaySocket>();
                assets.Add(zeroSocket);
                SetField(zeroSocket, "size", new Vector2(0f, 2f));
                Vector3 remotePortal = new Vector3(20f, 0f, 0f);
                Doorway zeroFirst = MakeDoorway(a, zeroSocket, remotePortal, false);
                Doorway zeroSecond = MakeDoorway(b, zeroSocket, remotePortal, true);
                Transform emptyLeaf = new GameObject("Zero-width leaf without geometry").transform;
                emptyLeaf.position = remotePortal;
                var zeroAngle = emptyLeaf.gameObject.AddComponent<RoomLocalDoorAngleSource>();
                zeroAngle.Configure(emptyLeaf, Quaternion.identity, Vector3.up, 90f);
                zeroAngle.ApplyOpenFraction(1f);
                registry.RegisterDoorwayVisibility(zeroFirst, zeroSecond, zeroAngle);
                Sample zeroWidth = Query(registry, remotePortal + new Vector3(0f, 1f, -0.01f));
                Check(zeroWidth.success && EqualL0(zeroWidth.sh, 2f) && !zeroWidth.info.spatialBlendActive,
                    "zero_width_socket_without_leaf_geometry_rejects_mixing", report);
                registry.RebuildFromGenerator(null);
                Sample cleared = Query(registry, nearA);
                b.switcher.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P0);
                Check(registry.TileSetCount == 0 && registry.DoorwayBlendZoneCount == 0 &&
                    !cleared.success && !cleared.info.hasTileData && !cleared.info.noVisibleProbes &&
                    !HasPowerSubscribers(a.switcher) && !HasPowerSubscribers(b.switcher),
                    "registry_clear_removes_sets_zones_and_power_subscriptions", report);
            }
            finally
            {
                if (registry != null) registry.RebuildFromGenerator(null);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.CloseScene(temporary, true);
                for (int i = assets.Count - 1; i >= 0; i--) if (assets[i] != null) Object.DestroyImmediate(assets[i]);
                LightmapSettings.lightmapsMode = originalMode;
                LightmapSettings.lightmaps = originalMaps;
            }
            Check(SceneManager.GetActiveScene() == original && original.isDirty == originalDirty &&
                SceneManager.sceneCount == originalSceneCount && DungeonTileProbeRegistry.Active == originalRegistry &&
                LightmapSettings.lightmapsMode == originalMode && SameMaps(originalMaps, LightmapSettings.lightmaps),
                "original_scene_registry_and_global_lightmaps_preserved", report);
            return JsonUtility.ToJson(report, true);
        }

        private static void Sweep(DungeonTileProbeRegistry registry, RoomLocalDoorAngleSource angle,
            float degrees, float limit, Report report)
        {
            angle.ApplyOpenFraction(degrees / 90f);
            Physics.SyncTransforms();
            var sweep = new SweepResult { angleDegrees = degrees, allowedStep = limit, l0 = new float[27] };
            report.continuity.Add(sweep);
            bool valid = true, monotonic = true;
            for (int i = 0; i < sweep.l0.Length; i++)
            {
                Sample sample = Query(registry, new Vector3(0f, 1f, (i - 13) * 0.05f));
                valid &= sample.success && Finite(sample);
                sweep.l0[i] = sample.sh[0, 0];
                if (i == 0) continue;
                float delta = sweep.l0[i] - sweep.l0[i - 1];
                sweep.maximumStep = Mathf.Max(sweep.maximumStep, Mathf.Abs(delta));
                monotonic &= delta >= -0.0001f;
            }
            Check(valid && monotonic && Near(sweep.l0[0], 2f) && Near(sweep.l0[26], 10f) &&
                Near(sweep.l0[13], 6f) && sweep.maximumStep <= limit,
                "door_" + degrees + "deg_5cm_sweep_continuity", report, "maxStep=" + sweep.maximumStep + "; limit=" + limit);
            Sample left = Query(registry, new Vector3(0f, 1f, -0.0001f));
            Sample right = Query(registry, new Vector3(0f, 1f, 0.0001f));
            Check(left.success && right.success && Mathf.Abs(left.sh[0, 0] - right.sh[0, 0]) < 0.01f,
                "door_" + degrees + "deg_no_ownership_step_at_center", report);
        }

        private static void TestInternalDoor(DungeonTileProbeRegistry registry, List<Object> assets, Report report)
        {
            Room room = MakeRoom("Regression internal door tile",
                new Bounds(new Vector3(50f, 1.5f, 0f), new Vector3(8f, 3f, 4f)));
            var owner = new GameObject("Internal Door Container");
            owner.transform.SetParent(room.tile.transform, false);
            owner.transform.localPosition = new Vector3(49f, 0f, 0f);
            // The actual global Door requires a Collider. Its interaction trigger must not
            // obscure whether the separate child slab is collected as structural geometry.
            BoxCollider interaction = owner.AddComponent<BoxCollider>();
            interaction.isTrigger = true;
            owner.AddComponent<global::Door>();
            BoxCollider slab = MakeBox("Closed Slab", owner.transform,
                new Vector3(1f, 1f, 0f), new Vector3(2f, 2f, 0.08f));
            SetBakes(room, Bake(assets, Entry(50f, 1f, 4f)), null);
            Physics.SyncTransforms();
            Register(registry, room);
            Vector3 position = new Vector3(50f, 1f, -1f);
            Sample closed = Query(registry, position, room.tile);
            Check(!closed.success && closed.info.hasTileData && closed.info.noVisibleProbes &&
                closed.info.rejectedProbeCount == 1 && closed.info.visibilityBlockerCount > 0,
                "global_door_ancestor_child_slab_blocks_when_closed", report);
            owner.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            Physics.SyncTransforms();
            Sample opened = Query(registry, position, room.tile);
            Check(opened.success && EqualL0(opened.sh, 4f),
                "global_door_child_slab_allows_probe_when_rotated_open", report);
            owner.transform.localRotation = Quaternion.identity;
            slab.enabled = false;
            Physics.SyncTransforms();
            Sample disabled = Query(registry, position, room.tile);
            Check(disabled.success && EqualL0(disabled.sh, 4f),
                "global_door_disabled_child_slab_allows_probe", report);
            slab.enabled = true;
            Physics.SyncTransforms();
            Sample reclosed = Query(registry, position, room.tile);
            Check(!reclosed.success && reclosed.info.noVisibleProbes,
                "global_door_child_slab_reclosing_invalidates_visibility", report);
        }

        private static void TestLockerDoor(DungeonTileProbeRegistry registry, List<Object> assets, Report report)
        {
            Room room = MakeRoom("Regression locker tile",
                new Bounds(new Vector3(70f, 1.5f, 0f), new Vector3(8f, 3f, 4f)));
            var locker = new GameObject("Locker_02_C");
            locker.transform.SetParent(room.tile.transform, false);
            var owner = new GameObject("Locker Door Leaf");
            owner.transform.SetParent(locker.transform, false);
            owner.transform.localPosition = new Vector3(69f, 0f, 0f);
            BoxCollider interaction = owner.AddComponent<BoxCollider>();
            interaction.isTrigger = true;
            owner.AddComponent<global::Door>();
            BoxCollider slab = MakeBox("Closed Slab", owner.transform,
                new Vector3(1f, 1f, 0f), new Vector3(2f, 2f, 0.08f));
            SetBakes(room, Bake(assets, Entry(70f, 1f, 4f)), null);
            Physics.SyncTransforms();
            Register(registry, room);
            Vector3 position = new Vector3(70f, 1f, -1f);
            Sample sample = Query(registry, position, room.tile);
            Check(slab.Raycast(new Ray(position, Vector3.forward), out _, 2f) &&
                sample.success && EqualL0(sample.sh, 4f) && sample.info.hasTileData &&
                sample.info.rejectedProbeCount == 0 && sample.info.visibilityBlockerCount == 0,
                "locker_ancestor_global_door_slab_is_not_room_occlusion", report);
        }

        private static BenchmarkResult Benchmark(DungeonTileProbeRegistry registry, Vector3 position, Tile tile, string scenario)
        {
            var result = new BenchmarkResult { scenario = scenario };
            for (int i = 0; i < result.warmupQueries; i++) Query(registry, position, tile);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < result.measuredQueries; i++)
            {
                Sample sample = Query(registry, position, tile);
                if (sample.success) result.successfulQueries++;
                result.visibilityRayCount += sample.info.visibilityRayCount;
                result.l0Checksum += sample.sh[0, 0];
            }
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;
            result.currentThreadManagedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            result.milliseconds = elapsed * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            result.microsecondsPerQuery = result.milliseconds * 1000.0 / result.measuredQueries;
            result.managedBytesPerQuery = (double)result.currentThreadManagedAllocatedBytes / result.measuredQueries;
            return result;
        }

        private static Room MakeRoom(string name, Bounds bounds)
        {
            var go = new GameObject(name);
            var room = new Room { tile = go.AddComponent<Tile>(), power = go.AddComponent<DungeonTilePowerBakeSet>(),
                switcher = go.AddComponent<DungeonTileLightmapSwitcher>() };
            room.tile.OverrideAutomaticTileBounds = true;
            room.tile.TileBoundsOverride = bounds;
            room.tile.RecalculateBounds();
            room.switcher.ConfigurePowerBakeSet(room.power);
            room.switcher.ConfigureLightingMode(DungeonTileLightmapSwitcher.LightingMode.PowerToggle);
            return room;
        }
        private static BoxCollider MakeBox(string name, Transform parent, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }
        private static void MakeLeafGeometry(Transform leaf, List<Object> assets)
        {
            BoxCollider box = MakeBox("Synthetic leaf slab", leaf, new Vector3(1f, 1f, 0f), new Vector3(2f, 2f, 0.08f));
            var mesh = new Mesh { name = "Regression leaf bounds mesh" };
            assets.Add(mesh);
            var vertices = new Vector3[8];
            for (int i = 0; i < 8; i++) vertices[i] = new Vector3((i & 1) == 0 ? -1f : 1f,
                (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -0.04f : 0.04f);
            mesh.vertices = vertices;
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6 };
            mesh.RecalculateBounds();
            box.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            box.gameObject.AddComponent<MeshRenderer>();
        }
        private static Doorway MakeDoorway(Room room, DoorwaySocket socket, Vector3 position, bool reverse)
        {
            var go = new GameObject("Doorway Regression");
            go.transform.SetParent(room.tile.transform, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, reverse ? 180f : 0f, 0f);
            Doorway door = go.AddComponent<Doorway>();
            SetField(door, "tile", room.tile);
            SetField(door, "socket", socket);
            return door;
        }
        private static DungeonTileBakeData.LightProbeBakeEntry Entry(float x, float z, float l0)
        {
            return new DungeonTileBakeData.LightProbeBakeEntry { localPosition = new Vector3(x, 1f, z),
                coefficient0 = Vector3.one * l0, occlusion = Vector4.one };
        }
        private static DungeonTileBakeData Bake(List<Object> assets, params DungeonTileBakeData.LightProbeBakeEntry[] entries)
        {
            var bake = ScriptableObject.CreateInstance<DungeonTileBakeData>();
            bake.name = "Regression transient SH only";
            bake.lightProbeEntries = entries;
            assets.Add(bake);
            return bake;
        }
        private static void SetBakes(Room room, DungeonTileBakeData on, DungeonTileBakeData off)
            => room.power.ConfigureRuntimeBakeData(on, off != null ? off : on);
        private static void Register(DungeonTileProbeRegistry registry, Room room)
        {
            MethodInfo method = typeof(DungeonTileProbeRegistry).GetMethod("RebuildSwitcherSet", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null || !(bool)method.Invoke(registry, new object[] { room.tile, room.switcher }))
                throw new InvalidOperationException("Synthetic production tile registration failed.");
        }
        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            field.SetValue(target, value);
        }
        private static Sample Query(DungeonTileProbeRegistry registry, Vector3 position, Tile tile = null)
        {
            var result = new Sample();
            result.success = tile != null
                ? registry.TrySampleForTile(tile, position, out result.sh, out result.occlusion, out result.info)
                : registry.TrySample(position, out result.sh, out result.occlusion, out result.info);
            return result;
        }
        private static bool Near(float a, float b) => !float.IsNaN(a) && !float.IsInfinity(a) && Mathf.Abs(a - b) < 0.0001f;
        private static bool EqualL0(SphericalHarmonicsL2 sh, float l0)
        {
            for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++)
                if (!Near(sh[c, k], k == 0 ? l0 : 0f)) return false;
            return true;
        }
        private static bool Equal(SphericalHarmonicsL2 a, SphericalHarmonicsL2 b)
        {
            for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++) if (!Near(a[c, k], b[c, k])) return false;
            return true;
        }
        private static bool Finite(Sample sample)
        {
            for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++)
                if (float.IsNaN(sample.sh[c, k]) || float.IsInfinity(sample.sh[c, k])) return false;
            for (int i = 0; i < 4; i++) if (float.IsNaN(sample.occlusion[i]) || float.IsInfinity(sample.occlusion[i])) return false;
            return true;
        }
        private static bool SameMaps(LightmapData[] a, LightmapData[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].lightmapColor != b[i].lightmapColor || a[i].lightmapDir != b[i].lightmapDir ||
                    a[i].shadowMask != b[i].shadowMask) return false;
            return true;
        }
        private static bool HasPowerSubscribers(DungeonTileLightmapSwitcher switcher)
        {
            FieldInfo field = typeof(DungeonTileLightmapSwitcher).GetField("PowerLevelApplied", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new MissingFieldException("PowerLevelApplied event backing field");
            return field.GetValue(switcher) is Delegate handler && handler.GetInvocationList().Length != 0;
        }
        private static void Check(bool passed, string name, Report report, string detail = null)
        {
            report.checks.Add(new CheckResult { name = name, passed = passed, detail = detail });
            if (!passed) throw new InvalidOperationException("Production SH regression failed: " + name + "\n" + JsonUtility.ToJson(report, true));
        }
    }
}
