using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonSHSamplingTest.Editor
{
    public static class SHSamplingTestValidation
    {
        [Serializable] private sealed class Report
        {
            public bool samplerPassed = true, numericOnly = true, visualVerified = false;
            public string[] checks;
            public LayoutDiagnostic[] bakeLayouts;
        }

        [Serializable] private sealed class LayoutDiagnostic
        {
            public string assetPath, status;
            public int total, heightLevels, lowerCount, upperCount;
            public float lowerY, upperY;
            public bool knownUpperLayerStarvation;
        }

        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("SH sampling validation requires Edit Mode.");
            Scene original = SceneManager.GetActiveScene();
            bool originalDirty = original.isDirty;
            int originalSceneCount = SceneManager.sceneCount;
            Scene temporary = default;
            Mesh singleSidedWallMesh = null;
            var checks = new List<string>();
            try
            {
                temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(temporary);
                var wallObject = new GameObject("SHValidation_TemporaryWall");
                wallObject.transform.position = new Vector3(0f, 1f, 0f);
                BoxCollider wall = wallObject.AddComponent<BoxCollider>();
                wall.size = new Vector3(0.2f, 4f, 10f);
                Physics.SyncTransforms();
                var blockers = new Collider[] { wall };
                Vector3 sample = new Vector3(-1f, 1f, 0f);
                Require(wall.Raycast(new Ray(sample, Vector3.right), out RaycastHit hit, 2f) &&
                    Near(hit.distance, 0.9f), "real_box_collider_raycast", checks);

                var baselineProbes = new[] { Probe(1, 1f, 1f), Probe(2, 2f, 2f),
                    Probe(3, 3f, 4f), Probe(4, 4f, 8f), Probe(5, 10f, 100f) };
                var baseline = SHSamplingTestSampler.Sample(baselineProbes, Vector3.up, false, blockers);
                // Closed-form reference for production weights 1 / (distance + 0.05), nearest four.
                Require(baseline.Success && baseline.ProbeIds.Length == 4 &&
                    CoefficientsEqual(baseline, 2.58801831f) &&
                    Near(baseline.Occlusion.x, 0.258801831f) &&
                    Near(baseline.Occlusion.y, 0.258801831f) &&
                    Near(baseline.Occlusion.z, 0.258801831f) &&
                    Near(baseline.Occlusion.w, 0.258801831f) && baseline.RayCount == 0,
                    "baseline_production_weighted_sum", checks);

                var oppositeSides = new[] { Probe(10, 0.5f, 100f), Probe(11, -3f, 2f) };
                var visible = SHSamplingTestSampler.Sample(oppositeSides, sample, true, blockers, 2, 1);
                Require(visible.Success && visible.ProbeIds[0] == 11 && visible.RejectedCount == 1 &&
                    visible.CandidateCount == 2 && CoefficientsEqual(visible, 2f) && !visible.UsedFallback,
                    "blocked_nearest_replaced_by_farther_visible", checks);

                var blocked = SHSamplingTestSampler.Sample(new[] { Probe(10, 0.5f, 100f),
                    Probe(12, 1f, 50f) }, sample, true, blockers);
                Require(!blocked.Success && blocked.ProbeIds.Length == 0 && blocked.RejectedCount == 2 &&
                    !blocked.UsedFallback && Finite(blocked), "all_blocked_fails_without_fallback_or_nan", checks);

                wall.isTrigger = true;
                Physics.SyncTransforms();
                var trigger = SHSamplingTestSampler.Sample(oppositeSides, sample, true, blockers, 2, 1);
                Require(trigger.Success && trigger.ProbeIds[0] == 10 && trigger.RayCount == 0,
                    "trigger_ignored", checks);
                wall.isTrigger = false;
                wall.enabled = false;
                Physics.SyncTransforms();
                var disabled = SHSamplingTestSampler.Sample(oppositeSides, sample, true, blockers, 2, 1);
                Require(disabled.Success && disabled.ProbeIds[0] == 10 && disabled.RayCount == 0,
                    "disabled_collider_ignored", checks);
                wall.enabled = true;
                Physics.SyncTransforms();
                var inside = SHSamplingTestSampler.Sample(new[] { Probe(11, -3f, 2f) },
                    Vector3.up, true, blockers);
                Require(!inside.Success && inside.RejectedCount == 1 && !inside.UsedFallback && Finite(inside),
                    "start_inside_blocker_invalid", checks);

                var ties = new[] { Probe(9, -3f, 9f), Probe(2, -1f, 2f) };
                Vector3 tiePosition = new Vector3(-2f, 1f, 0f);
                var tieA = SHSamplingTestSampler.Sample(ties, tiePosition, true, blockers, 2, 1);
                Array.Reverse(ties);
                var tieB = SHSamplingTestSampler.Sample(ties, tiePosition, true, blockers, 2, 1);
                Require(tieA.Success && tieB.Success && tieA.ProbeIds[0] == 2 && tieB.ProbeIds[0] == 2 &&
                    CoefficientsEqual(tieA, 2f) && CoefficientsEqual(tieB, 2f),
                    "distance_ties_stable_by_probe_id", checks);

                // A 2 cm inset formerly moved the segment past this entire 1 cm wall.
                wall.size = new Vector3(0.01f, 4f, 10f);
                Physics.SyncTransforms();
                Vector3 nearThinWall = new Vector3(-0.01f, 1f, 0f);
                Vector3 farAcrossWall = new Vector3(1f, 1f, 0f);
                Require(wall.Raycast(new Ray(nearThinWall, Vector3.right), out RaycastHit thinHit, 1.01f) &&
                    Near(thinHit.distance, 0.005f), "real_thin_box_near_endpoint_raycast", checks);
                var nearStart = SHSamplingTestSampler.Sample(new[] { Probe(30, 1f, 100f) },
                    nearThinWall, true, blockers);
                var nearEnd = SHSamplingTestSampler.Sample(new[] { Probe(31, -0.01f, 100f) },
                    farAcrossWall, true, blockers);
                Require(!nearStart.Success && nearStart.RejectedCount == 1 && nearStart.RayCount > 0 &&
                    Finite(nearStart), "thin_box_near_sample_blocks_full_segment", checks);
                Require(!nearEnd.Success && nearEnd.RejectedCount == 1 && nearEnd.RayCount > 0 &&
                    Finite(nearEnd), "thin_box_near_probe_blocks_full_segment", checks);

                // One quad, normal +X: the opposite side requires the reverse-ray path
                // when Physics.queriesHitBackfaces is false. Do not change that global flag.
                var meshWallObject = new GameObject("SHValidation_SingleSidedMeshWall");
                singleSidedWallMesh = new Mesh { name = "SHValidation_SingleSidedQuad" };
                singleSidedWallMesh.vertices = new[] {
                    new Vector3(0f, 0f, -1f), new Vector3(0f, 2f, -1f),
                    new Vector3(0f, 2f, 1f), new Vector3(0f, 0f, 1f) };
                singleSidedWallMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                singleSidedWallMesh.RecalculateBounds();
                var meshWall = meshWallObject.AddComponent<MeshCollider>();
                meshWall.convex = false;
                meshWall.sharedMesh = singleSidedWallMesh;
                Physics.SyncTransforms();
                var meshBlockers = new Collider[] { meshWall };
                Require(meshWall.Raycast(new Ray(farAcrossWall, Vector3.left), out RaycastHit meshHit, 2f) &&
                    Near(meshHit.distance, 1f), "real_single_sided_mesh_front_raycast", checks);
                Require(!SHSamplingTestSampler.IsVisible(new Vector3(0.01f, 1f, 0f),
                    new Vector3(-1f, 1f, 0f), meshBlockers, out int frontRays) && frontRays > 0,
                    "single_sided_mesh_front_near_endpoint_blocked", checks);
                Require(!SHSamplingTestSampler.IsVisible(nearThinWall, farAcrossWall,
                    meshBlockers, out int backRays) && backRays > 0,
                    "single_sided_mesh_back_near_endpoint_blocked", checks);
            }
            finally
            {
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.CloseScene(temporary, true);
                if (singleSidedWallMesh != null) UnityEngine.Object.DestroyImmediate(singleSidedWallMesh);
            }
            Require(SceneManager.GetActiveScene() == original && original.isDirty == originalDirty &&
                SceneManager.sceneCount == originalSceneCount, "original_scene_state_preserved", checks);
            return JsonUtility.ToJson(new Report { checks = checks.ToArray(), bakeLayouts = new[] {
                ReadLayout("Maze"), ReadLayout("AdminstrativeSegregation") } }, true);
        }

        private static SHSamplingTestSampler.Probe Probe(int id, float x, float l0)
        {
            var probe = new SHSamplingTestSampler.Probe { Id = id, Position = new Vector3(x, 1f, 0f),
                Occlusion = Vector4.one * (l0 / 10f) };
            for (int channel = 0; channel < 3; channel++) probe.SH[channel, 0] = l0;
            return probe;
        }

        private static bool CoefficientsEqual(SHSamplingTestSampler.Result result, float l0)
        {
            for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    if (!Near(result.SH[channel, coefficient], coefficient == 0 ? l0 : 0f)) return false;
            return true;
        }

        private static bool Finite(SHSamplingTestSampler.Result result)
        {
            for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    if (!IsFinite(result.SH[channel, coefficient])) return false;
            for (int i = 0; i < 4; i++) if (!IsFinite(result.Occlusion[i])) return false;
            return IsFinite(result.Luminance);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Near(float a, float b) => IsFinite(a) && Mathf.Abs(a - b) < 0.00001f;
        private static void Require(bool condition, string name, List<string> checks)
        {
            if (!condition) throw new InvalidOperationException("SH sampling validation failed: " + name);
            checks.Add(name);
        }

        private static LayoutDiagnostic ReadLayout(string room)
        {
            var result = new LayoutDiagnostic { assetPath = "Assets/Prefabs/map_piece/NewPrison/test/V2_" +
                room + "/" + room + "_LightingSetV2.asset", status = "UNAVAILABLE" };
            string absolutePath = Path.Combine(Application.dataPath, "..", result.assetPath);
            if (!File.Exists(absolutePath)) return result;
            var heights = new SortedDictionary<float, int>();
            bool target = false, entries = false;
            // Stream text only: these assets embed huge cubemap data; never load Unity texture objects.
            foreach (string line in File.ReadLines(absolutePath))
            {
                if (line == "  m_Name: " + room + "_R000_P100_V2") target = true;
                if (!target) continue;
                if (line.StartsWith("---", StringComparison.Ordinal)) break;
                if (line == "  lightProbeEntries:") entries = true;
                if (!entries || !line.StartsWith("  - localPosition:", StringComparison.Ordinal)) continue;
                Match match = Regex.Match(line, @"y: ([-+0-9.eE]+)");
                if (!match.Success) continue;
                float y = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                heights.TryGetValue(y, out int count);
                heights[y] = count + 1;
                result.total++;
            }
            result.heightLevels = heights.Count;
            bool first = true;
            foreach (var height in heights)
            {
                if (first) { result.lowerY = height.Key; result.lowerCount = height.Value; first = false; }
                result.upperY = height.Key;
                result.upperCount = height.Value;
            }
            result.knownUpperLayerStarvation = result.total == 128 && heights.Count == 2 &&
                result.lowerCount == 127 && result.upperCount == 1;
            result.status = result.knownUpperLayerStarvation ? "KNOWN_DATA_ISSUE_UPPER_LAYER_STARVATION" :
                result.total > 0 ? "OBSERVED_LAYOUT" : "CANONICAL_PROBES_NOT_FOUND";
            return result;
        }
    }
}
