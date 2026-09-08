using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DungeonSHSamplingTest.Editor
{
    // Room-local observations only. This does not execute the production Registry,
    // establish walkable connectivity, rebake data, or issue a lighting PASS verdict.
    public static class SHSamplingTestCoverage
    {
        private const float GridSpacing = 1f;
        private const float BodyHeight = 1.65f, BodyRadius = .22f, FloorClearance = .04f;
        private const int MaxValidCellsPerCase = 5000, MaxGridCellsPerCase = 25000;
        private const double TimeBudgetSeconds = 55;
        private static readonly string[] Rooms = { "StartRoom", "Maze", "CorridorB", "AdminstrativeSegregation" };
        private static readonly int[] Rotations = { 0, 90 };

        [Serializable] private sealed class Report
        {
            public string status = "OBSERVATIONS_ONLY", createdUtc, scope, error;
            public bool numericOnly = true, visualVerified, productionAcceptance;
            public bool originalSceneStatePreserved, originalLightmapsPreserved, originalDirty, timeBudgetReached;
            public string originalScene;
            public float gridSpacing = GridSpacing, bodyHeight = BodyHeight, bodyRadius = BodyRadius;
            public float floorClearance = FloorClearance, bodyAnchorHeight = 1f;
            public int maxValidCellsPerCase = MaxValidCellsPerCase, maxValidCellsPerRoom = MaxValidCellsPerCase * 2;
            public int maxGridCellsPerCase = MaxGridCellsPerCase;
            public double elapsedSeconds;
            public List<CaseReport> cases = new List<CaseReport>();
        }

        [Serializable] private sealed class CaseReport
        {
            public string room, prefabPath, bakeName, status, error;
            public int rotation, probeCount, colliders, floorColliders, structuralBlockers;
            public int gridCellsInspected, noFloor, floorNormalRejected, wallOrStructureCollision, furnitureCollision;
            public int validCells, baselineNoData, candidateNoData, candidateFallbacks, changedCells;
            public int candidateChecks, rejectedProbes, candidateRays, adjacentValidPairs;
            public int exhaustiveRecoveredCells, exhaustiveStillNoDataCells, exhaustiveChecks, exhaustiveRays;
            public bool validCellCapReached, gridCapReached, timeBudgetReached;
            public float maxAbsL0Change, maxAbsSHChange, maxBaselineL0Step, maxCandidateL0Step;
            public float maxBaselineSHStep, maxCandidateSHStep;
            public double elapsedSeconds;
            public List<CellReport> cells = new List<CellReport>();
            public List<CellReport> badCellDiagnostics = new List<CellReport>();
        }

        [Serializable] private sealed class CellReport
        {
            public int gridX, gridZ;
            public Vector3 footPosition, bodyAnchor, localBodyAnchor;
            public bool baselineSuccess, candidateSuccess, candidateFallback;
            public float baselineL0, candidateL0, absL0Change, maxAbsSHChange;
            public int candidateChecks, rejectedProbes, candidateRays;
            public int[] baselineProbeIds, candidateProbeIds;
            public bool exhaustiveAttempted, exhaustiveSuccess;
            public int exhaustiveChecks, exhaustiveRejectedProbes, exhaustiveRays;
            public int[] exhaustiveProbeIds;
            public float exhaustiveL0;
            public string failureDiagnosis;
            [NonSerialized] public SphericalHarmonicsL2 baselineSH, candidateSH;
        }

        private sealed class ColliderInfo
        {
            public Collider collider;
            public Bounds bounds;
            public bool floor, structure;
        }

        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Coverage requires idle Edit Mode.");

            Scene original = SceneManager.GetActiveScene();
            Scene[] originalScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            bool[] originalDirty = originalScenes.Select(scene => scene.isDirty).ToArray();
            int[][] originalRoots = originalScenes.Select(scene => scene.GetRootGameObjects().Select(root => root.GetInstanceID()).ToArray()).ToArray();
            LightmapData[] originalMaps = LightmapSettings.lightmaps;
            LightmapsMode originalMode = LightmapSettings.lightmapsMode;
            var report = new Report {
                createdUtc = DateTime.UtcNow.ToString("O"), originalScene = original.path, originalDirty = original.isDirty,
                scope = "P100 body-anchor distance versus visibility sampling in isolated V2 rooms; " +
                    "room-only floor rays and capsule clearance; local 1m grid, R000/R090. " +
                    "Static structural colliders block SH rays; all solid colliders reject occupied cells. " +
                    "Max-step compares adjacent valid grid cells, not a continuous gameplay trajectory. " +
                    "Failed 24-candidate samples are diagnosed separately with all room probes; " +
                    "that diagnostic does not replace the reported candidate output."
            };
            var watch = Stopwatch.StartNew();
            Scene temporary = default;
            try
            {
                temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(temporary);
                foreach (string roomName in Rooms)
                {
                    string path = "Assets/Prefabs/map_piece/NewPrison/test/V2_" + roomName + "/" + roomName + ".prefab";
                    if (watch.Elapsed.TotalSeconds >= TimeBudgetSeconds)
                    {
                        foreach (int rotation in Rotations) report.cases.Add(Skipped(roomName, path, rotation, "TIME_BUDGET_NOT_RUN"));
                        report.timeBudgetReached = true;
                        continue;
                    }
                    GameObject room = null;
                    GameObject capsuleObject = null;
                    try
                    {
                        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (prefab == null) throw new InvalidOperationException("Prefab not found: " + path);
                        room = PrefabUtility.InstantiatePrefab(prefab, temporary) as GameObject;
                        if (room == null) throw new InvalidOperationException("Prefab instantiation failed.");
                        PrefabUtility.UnpackPrefabInstance(room, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                        room.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        foreach (MonoBehaviour behaviour in room.GetComponentsInChildren<MonoBehaviour>(true))
                            if (behaviour != null && !(behaviour is DungeonTileRotationSelectorV2) &&
                                !(behaviour is DungeonTileLightmapSwitcher) && !(behaviour is DungeonTilePowerBakeSet) &&
                                !(behaviour is DungeonTileLightingPolicy)) behaviour.enabled = false;
                        foreach (Light light in room.GetComponentsInChildren<Light>(true)) light.enabled = false;
                        foreach (Camera camera in room.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                        foreach (AudioSource audio in room.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
                        foreach (Animator animator in room.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                        foreach (Rigidbody body in room.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
                        var selector = room.GetComponent<DungeonTileRotationSelectorV2>();
                        var switcher = room.GetComponent<DungeonTileLightmapSwitcher>();
                        if (selector == null || switcher == null) throw new InvalidOperationException("Missing V2 selector or switcher.");
                        capsuleObject = new GameObject("SHCoverage_ClearanceCapsule");
                        var capsule = capsuleObject.AddComponent<CapsuleCollider>();
                        capsule.isTrigger = true;
                        capsule.direction = 1;
                        capsule.radius = BodyRadius;
                        capsule.height = BodyHeight;
                        capsule.center = Vector3.up * (FloorClearance + BodyHeight * .5f);
                        foreach (int rotation in Rotations)
                        {
                            if (watch.Elapsed.TotalSeconds >= TimeBudgetSeconds)
                            {
                                report.cases.Add(Skipped(roomName, path, rotation, "TIME_BUDGET_NOT_RUN"));
                                report.timeBudgetReached = true;
                                continue;
                            }
                            var entry = new CaseReport { room = roomName, prefabPath = path, rotation = rotation };
                            report.cases.Add(entry);
                            double started = watch.Elapsed.TotalSeconds;
                            try
                            {
                                room.transform.rotation = Quaternion.Euler(0f, rotation, 0f);
                                if (!selector.ApplyForCurrentRotation(false)) throw new InvalidOperationException("V2 rotation selection failed.");
                                // Do not call SetPowerLevel in Edit Mode: its Awake-only renderer
                                // cache is not populated. The assigned P100 data is the sampling input.
                                DungeonTileBakeData bake = switcher.GetBakeData(DungeonTileLightmapSwitcher.PowerLevel.P100);
                                if (bake == null || bake.lightProbeEntries == null || bake.lightProbeEntries.Length == 0)
                                {
                                    entry.status = "NO_P100_PROBE_DATA";
                                    continue;
                                }
                                entry.bakeName = bake.name;
                                entry.probeCount = bake.lightProbeEntries.Length;
                                Physics.SyncTransforms();
                                ObserveRoom(room, capsule, bake, entry, watch);
                                report.timeBudgetReached |= entry.timeBudgetReached;
                            }
                            catch (Exception exception) { entry.status = "CASE_ERROR"; entry.error = exception.ToString(); }
                            finally { entry.elapsedSeconds = watch.Elapsed.TotalSeconds - started; }
                        }
                    }
                    catch (Exception exception)
                    {
                        foreach (int rotation in Rotations)
                            if (!report.cases.Any(entry => entry.room == roomName && entry.rotation == rotation))
                            {
                                CaseReport entry = Skipped(roomName, path, rotation, "ROOM_SETUP_ERROR");
                                entry.error = exception.ToString(); report.cases.Add(entry);
                            }
                    }
                    finally
                    {
                        if (capsuleObject != null) Object.DestroyImmediate(capsuleObject);
                        if (room != null) Object.DestroyImmediate(room);
                    }
                }
            }
            catch (Exception exception) { report.error = exception.ToString(); report.status = "COVERAGE_ERROR"; }
            finally
            {
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.CloseScene(temporary, true);
                LightmapSettings.lightmapsMode = originalMode;
                LightmapSettings.lightmaps = originalMaps;
                LightmapData[] restoredMaps = LightmapSettings.lightmaps;
                report.originalLightmapsPreserved = LightmapSettings.lightmapsMode == originalMode && restoredMaps.Length == originalMaps.Length;
                for (int i = 0; report.originalLightmapsPreserved && i < originalMaps.Length; i++)
                {
                    LightmapData before = originalMaps[i], after = restoredMaps[i];
                    report.originalLightmapsPreserved = before == null || after == null ? before == after :
                        after.lightmapColor == before.lightmapColor && after.lightmapDir == before.lightmapDir &&
                        after.shadowMask == before.shadowMask;
                }
                report.originalSceneStatePreserved = SceneManager.GetActiveScene() == original && SceneManager.sceneCount == originalScenes.Length;
                for (int i = 0; i < originalScenes.Length; i++)
                    report.originalSceneStatePreserved &= originalScenes[i].IsValid() && originalScenes[i].isLoaded &&
                        originalScenes[i].isDirty == originalDirty[i] &&
                        originalRoots[i].SequenceEqual(originalScenes[i].GetRootGameObjects().Select(root => root.GetInstanceID()));
                if (!report.originalSceneStatePreserved || !report.originalLightmapsPreserved) report.status = "ORIGINAL_SCENE_STATE_CHANGED";
                report.elapsedSeconds = watch.Elapsed.TotalSeconds;
            }
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory("Artifacts/SHSamplingTest");
            File.WriteAllText("Artifacts/SHSamplingTest/coverage.json", json);
            return json;
        }

        private static CaseReport Skipped(string room, string path, int rotation, string status)
        {
            return new CaseReport { room = room, prefabPath = path, rotation = rotation, status = status };
        }

        private static void ObserveRoom(GameObject room, CapsuleCollider capsule, DungeonTileBakeData bake, CaseReport report, Stopwatch watch)
        {
            int floorLayer = LayerMask.NameToLayer("Floor");
            ColliderInfo[] colliders = room.GetComponentsInChildren<Collider>(true)
                .Where(collider => collider != null && collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy)
                .Select(collider => new ColliderInfo { collider = collider, bounds = collider.bounds,
                    floor = collider.gameObject.layer == floorLayer || HasAncestor(collider.transform, room.transform, "Floor", "Floors", "Ground"),
                    structure = HasStructuralAncestor(collider.transform, room.transform) }).ToArray();
            ColliderInfo[] floors = colliders.Where(info => info.floor).ToArray();
            Collider[] blockers = colliders.Where(info => info.structure && !info.floor).Select(info => info.collider).ToArray();
            report.colliders = colliders.Length; report.floorColliders = floors.Length; report.structuralBlockers = blockers.Length;
            if (floors.Length == 0) { report.status = "NO_ACTIVE_FLOOR_COLLIDERS"; return; }
            Bounds worldFloorBounds = floors[0].bounds;
            foreach (ColliderInfo floor in floors) worldFloorBounds.Encapsulate(floor.bounds);
            Bounds localGrid = ToLocalBounds(worldFloorBounds, room.transform);
            var probes = new SHSamplingTestSampler.Probe[bake.lightProbeEntries.Length];
            for (int i = 0; i < probes.Length; i++)
                probes[i] = new SHSamplingTestSampler.Probe { Id = i,
                    Position = room.transform.TransformPoint(bake.lightProbeEntries[i].localPosition),
                    SH = bake.lightProbeEntries[i].ToSphericalHarmonics(), Occlusion = bake.lightProbeEntries[i].occlusion };
            int countX = Mathf.CeilToInt(localGrid.size.x / GridSpacing), countZ = Mathf.CeilToInt(localGrid.size.z / GridSpacing);
            var observed = new Dictionary<Vector2Int, CellReport>();
            for (int x = 0; x < countX; x++)
            {
                for (int z = 0; z < countZ; z++)
                {
                    if (watch.Elapsed.TotalSeconds >= TimeBudgetSeconds) { report.timeBudgetReached = true; break; }
                    if (report.validCells >= MaxValidCellsPerCase) { report.validCellCapReached = true; break; }
                    if (report.gridCellsInspected >= MaxGridCellsPerCase) { report.gridCapReached = true; break; }
                    report.gridCellsInspected++;
                    Vector3 gridPoint = room.transform.TransformPoint(new Vector3(localGrid.min.x + (x + .5f) * GridSpacing,
                        0f, localGrid.min.z + (z + .5f) * GridSpacing));
                    Vector3 origin = new Vector3(gridPoint.x, worldFloorBounds.max.y + 2f, gridPoint.z);
                    if (!TryFloor(floors, origin, worldFloorBounds.size.y + 4f, out RaycastHit hit)) { report.noFloor++; continue; }
                    if (Vector3.Dot(hit.normal, Vector3.up) < .8f) { report.floorNormalRejected++; continue; }
                    int collisionKind = ClearanceCollision(colliders, capsule, hit.point);
                    if (collisionKind == 1) { report.wallOrStructureCollision++; continue; }
                    if (collisionKind == 2) { report.furnitureCollision++; continue; }
                    Vector3 sample = hit.point + Vector3.up;
                    var baseline = SHSamplingTestSampler.Sample(probes, sample, false, blockers);
                    var candidate = SHSamplingTestSampler.Sample(probes, sample, true, blockers);
                    var cell = new CellReport { gridX = x, gridZ = z, footPosition = hit.point, bodyAnchor = sample,
                        localBodyAnchor = room.transform.InverseTransformPoint(sample),
                        baselineSuccess = baseline.Success, candidateSuccess = candidate.Success, candidateFallback = candidate.UsedFallback,
                        baselineL0 = baseline.Luminance, candidateL0 = candidate.Luminance,
                        baselineProbeIds = baseline.ProbeIds, candidateProbeIds = candidate.ProbeIds,
                        baselineSH = baseline.SH, candidateSH = candidate.SH,
                        candidateChecks = candidate.CandidateCount, rejectedProbes = candidate.RejectedCount, candidateRays = candidate.RayCount };
                    report.validCells++;
                    if (!baseline.Success) report.baselineNoData++;
                    if (!candidate.Success)
                    {
                        report.candidateNoData++;
                        var exhaustive = SHSamplingTestSampler.Sample(probes, sample, true, blockers, probes.Length);
                        cell.exhaustiveAttempted = true;
                        cell.exhaustiveSuccess = exhaustive.Success;
                        cell.exhaustiveChecks = exhaustive.CandidateCount;
                        cell.exhaustiveRejectedProbes = exhaustive.RejectedCount;
                        cell.exhaustiveRays = exhaustive.RayCount;
                        cell.exhaustiveProbeIds = exhaustive.ProbeIds;
                        cell.exhaustiveL0 = exhaustive.Luminance;
                        cell.failureDiagnosis = exhaustive.Success ? "VISIBLE_PROBES_BEYOND_NEAREST_24" :
                            "NO_VISIBLE_PROBE_WITH_CURRENT_ROOM_DATA_AND_BLOCKERS";
                        if (exhaustive.Success) report.exhaustiveRecoveredCells++;
                        else report.exhaustiveStillNoDataCells++;
                        report.exhaustiveChecks += exhaustive.CandidateCount;
                        report.exhaustiveRays += exhaustive.RayCount;
                        report.badCellDiagnostics.Add(cell);
                    }
                    if (candidate.UsedFallback) report.candidateFallbacks++;
                    report.candidateChecks += candidate.CandidateCount; report.rejectedProbes += candidate.RejectedCount; report.candidateRays += candidate.RayCount;
                    if (baseline.Success && candidate.Success)
                    {
                        cell.absL0Change = Mathf.Abs(baseline.Luminance - candidate.Luminance);
                        cell.maxAbsSHChange = SHDifference(baseline.SH, candidate.SH);
                        if (cell.maxAbsSHChange > .00001f) report.changedCells++;
                        report.maxAbsL0Change = Mathf.Max(report.maxAbsL0Change, cell.absL0Change);
                        report.maxAbsSHChange = Mathf.Max(report.maxAbsSHChange, cell.maxAbsSHChange);
                    }
                    if (observed.TryGetValue(new Vector2Int(x - 1, z), out CellReport previousX)) CompareStep(previousX, cell, report);
                    if (observed.TryGetValue(new Vector2Int(x, z - 1), out CellReport previousZ)) CompareStep(previousZ, cell, report);
                    observed[new Vector2Int(x, z)] = cell; report.cells.Add(cell);
                }
                if (report.timeBudgetReached || report.validCellCapReached || report.gridCapReached) break;
            }
            report.status = report.timeBudgetReached ? "PARTIAL_TIME_BUDGET" : report.validCellCapReached || report.gridCapReached
                ? "CAPPED_OBSERVATIONS" : report.validCells == 0 ? "NO_CLEAR_FLOOR_CELLS" : "OBSERVATIONS_COMPLETE";
        }

        private static bool TryFloor(ColliderInfo[] floors, Vector3 origin, float distance, out RaycastHit best)
        {
            best = default; bool found = false; float nearest = distance;
            var ray = new Ray(origin, Vector3.down);
            foreach (ColliderInfo floor in floors)
            {
                if (origin.x < floor.bounds.min.x || origin.x > floor.bounds.max.x ||
                    origin.z < floor.bounds.min.z || origin.z > floor.bounds.max.z) continue;
                if (floor.collider.Raycast(ray, out RaycastHit hit, nearest)) { best = hit; nearest = hit.distance; found = true; }
            }
            return found;
        }

        private static int ClearanceCollision(ColliderInfo[] colliders, CapsuleCollider capsule, Vector3 foot)
        {
            Bounds body = new Bounds(foot + capsule.center, new Vector3(BodyRadius * 2f, BodyHeight, BodyRadius * 2f));
            int result = 0;
            foreach (ColliderInfo info in colliders)
            {
                if (!body.Intersects(info.bounds)) continue;
                if (!Physics.ComputePenetration(capsule, foot, Quaternion.identity, info.collider,
                    info.collider.transform.position, info.collider.transform.rotation, out _, out float depth) || depth <= .0001f) continue;
                if (info.structure || info.floor) return 1;
                result = 2;
            }
            return result;
        }

        private static void CompareStep(CellReport a, CellReport b, CaseReport report)
        {
            if (Mathf.Abs(a.footPosition.y - b.footPosition.y) > .3f || !a.baselineSuccess || !b.baselineSuccess ||
                !a.candidateSuccess || !b.candidateSuccess) return;
            report.adjacentValidPairs++;
            report.maxBaselineL0Step = Mathf.Max(report.maxBaselineL0Step, Mathf.Abs(a.baselineL0 - b.baselineL0));
            report.maxCandidateL0Step = Mathf.Max(report.maxCandidateL0Step, Mathf.Abs(a.candidateL0 - b.candidateL0));
            report.maxBaselineSHStep = Mathf.Max(report.maxBaselineSHStep, SHDifference(a.baselineSH, b.baselineSH));
            report.maxCandidateSHStep = Mathf.Max(report.maxCandidateSHStep, SHDifference(a.candidateSH, b.candidateSH));
        }

        private static float SHDifference(SphericalHarmonicsL2 a, SphericalHarmonicsL2 b)
        {
            float maximum = 0f;
            for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    maximum = Mathf.Max(maximum, Mathf.Abs(a[channel, coefficient] - b[channel, coefficient]));
            return maximum;
        }

        private static Bounds ToLocalBounds(Bounds world, Transform root)
        {
            var local = new Bounds(root.InverseTransformPoint(world.min), Vector3.zero);
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                local.Encapsulate(root.InverseTransformPoint(new Vector3(x == 0 ? world.min.x : world.max.x,
                    y == 0 ? world.min.y : world.max.y, z == 0 ? world.min.z : world.max.z)));
            return local;
        }

        private static bool HasStructuralAncestor(Transform target, Transform root)
        {
            for (Transform current = target; current != null && current != root; current = current.parent)
                if (new[] { "Wall", "Door", "Pillar", "Reception", "Partition", "Ceiling" }
                    .Any(term => current.name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            return false;
        }

        private static bool HasAncestor(Transform target, Transform root, params string[] names)
        {
            for (Transform current = target; current != null && current != root; current = current.parent)
                foreach (string name in names)
                    if (current.name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                        current.name.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase) ||
                        current.name.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
