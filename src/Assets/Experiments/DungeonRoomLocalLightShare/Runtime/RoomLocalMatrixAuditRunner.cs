using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DunGen;
using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    [DisallowMultipleComponent]
    public sealed class RoomLocalMatrixAuditRunner : MonoBehaviour
    {
        private const int CaseWaitFrameLimit = 240;
        private static readonly DungeonTileLightmapSwitcher.PowerLevel[] Powers =
        {
            DungeonTileLightmapSwitcher.PowerLevel.P0,
            DungeonTileLightmapSwitcher.PowerLevel.P100
        };
        private static readonly float[] DoorFractions = { 0.5f, 1f };

        private RoomLocalMatrixController controller;
        private string reportPath;
        private string captureDirectory;
        private Coroutine auditCoroutine;

        public bool IsRunning { get; private set; }
        public bool IsComplete { get; private set; }
        public string Status { get; private set; } = "Idle";
        public string ReportPath => reportPath ?? string.Empty;
        public string CaptureDirectory => captureDirectory ?? string.Empty;
        public int CompletedStates { get; private set; }
        public int TotalStates { get; private set; }

        public bool Begin(
            RoomLocalMatrixController target,
            string outputPath,
            string screenshotDirectory)
        {
            if (IsRunning || target == null || target.CaseCount <= 0 ||
                string.IsNullOrWhiteSpace(outputPath))
                return false;

            controller = target;
            reportPath = Path.GetFullPath(outputPath);
            captureDirectory = string.IsNullOrWhiteSpace(screenshotDirectory)
                ? string.Empty
                : Path.GetFullPath(screenshotDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? ".");
            if (!string.IsNullOrEmpty(captureDirectory))
                Directory.CreateDirectory(captureDirectory);
            IsRunning = true;
            IsComplete = false;
            CompletedStates = 0;
            TotalStates = target.CaseCount * Powers.Length * Powers.Length * DoorFractions.Length;
            Status = "Starting matrix audit";
            auditCoroutine = StartCoroutine(RunAudit());
            return true;
        }

        private IEnumerator RunAudit()
        {
            int originalCase = controller.CaseIndex;
            float originalDoor = controller.Connection != null && controller.Connection.DoorAngle != null
                ? controller.Connection.DoorAngle.OpenFraction
                : 0.5f;
            bool originalEnabled = controller.Connection == null || controller.Connection.ConnectionEnabled;
            DungeonTileLightmapSwitcher.PowerLevel originalPowerA =
                CurrentPower(controller.Connection != null ? controller.Connection.StartLighting : null);
            DungeonTileLightmapSwitcher.PowerLevel originalPowerB =
                CurrentPower(controller.Connection != null ? controller.Connection.AdministrativeLighting : null);
            float originalIntensity = controller.DirectIntensity;
            float originalRange = controller.DirectRange;

            var csv = new StringBuilder(64 * 1024);
            csv.AppendLine(
                "case_number,case_label,room_a,door_kind_a,room_b,door_kind_b," +
                "power_a,power_b,door_fraction,door_prefab,door_parent_ok," +
                "doorway_gap_m,doorway_forward_dot,door_local_offset_error_m," +
                "leaf_hinge_distance_m,leaf_bounds_x,leaf_bounds_y,leaf_bounds_z," +
                "selected_in_a,selected_in_b,placement_flags,probe_applied,probe_fault," +
                "probe_bindings,face_normal_sampling,face_a_l0,face_b_l0,edge_l0," +
                "connection_fault,has_outgoing,has_bounce,capture_file," +
                "door_viewport_min_x,door_viewport_min_y,door_viewport_max_x,door_viewport_max_y");

            for (int caseIndex = 0; caseIndex < controller.CaseCount; caseIndex++)
            {
                Status = $"Loading case {caseIndex + 1}/{controller.CaseCount}";
                if (controller.CaseIndex != caseIndex)
                    controller.JumpToCase(caseIndex);
                yield return WaitForCase(caseIndex);

                if (controller.Connection == null)
                {
                    AppendMissingCase(csv, caseIndex, controller.Status);
                    CompletedStates += Powers.Length * Powers.Length * DoorFractions.Length;
                    continue;
                }

                for (int powerAIndex = 0; powerAIndex < Powers.Length; powerAIndex++)
                for (int powerBIndex = 0; powerBIndex < Powers.Length; powerBIndex++)
                for (int doorIndex = 0; doorIndex < DoorFractions.Length; doorIndex++)
                {
                    DungeonTileLightmapSwitcher.PowerLevel powerA = Powers[powerAIndex];
                    DungeonTileLightmapSwitcher.PowerLevel powerB = Powers[powerBIndex];
                    float doorFraction = DoorFractions[doorIndex];

                    controller.SetPower(powerA, powerB);
                    controller.SetDoor(doorFraction);
                    controller.SetConnectionEnabled(true);
                    yield return null;
                    yield return null;

                    RoomLocalDoorProbeDriver driver = controller.DoorProbeDriver;
                    if (driver != null && !driver.IsFaultLatched)
                        driver.TryApplyNow(out _);

                    string capturePath = BuildCapturePath(
                        caseIndex,
                        powerA,
                        powerB,
                        doorFraction);
                    AppendState(
                        csv,
                        caseIndex,
                        powerA,
                        powerB,
                        doorFraction,
                        capturePath);
                    if (!string.IsNullOrEmpty(capturePath))
                    {
                        ScreenCapture.CaptureScreenshot(capturePath);
                        yield return new WaitForEndOfFrame();
                        yield return null;
                    }
                    CompletedStates++;
                    Status = $"Audited {CompletedStates}/{TotalStates}";
                }
            }

            File.WriteAllText(reportPath, csv.ToString(), new UTF8Encoding(false));
            yield return RestoreOriginalState(
                originalCase,
                originalPowerA,
                originalPowerB,
                originalDoor,
                originalEnabled,
                originalIntensity,
                originalRange);
            Status = $"Complete: {CompletedStates}/{TotalStates}";
            IsComplete = true;
            IsRunning = false;
            auditCoroutine = null;
        }

        private IEnumerator WaitForCase(int expectedCase)
        {
            int startFrame = Time.frameCount;
            while ((controller.CaseIndex != expectedCase || controller.Connection == null) &&
                   Time.frameCount - startFrame < CaseWaitFrameLimit)
                yield return null;
            yield return null;
        }

        private IEnumerator RestoreOriginalState(
            int caseIndex,
            DungeonTileLightmapSwitcher.PowerLevel powerA,
            DungeonTileLightmapSwitcher.PowerLevel powerB,
            float doorFraction,
            bool connectionEnabled,
            float intensity,
            float range)
        {
            if (controller != null && controller.CaseIndex != caseIndex)
            {
                controller.JumpToCase(caseIndex);
                yield return WaitForCase(caseIndex);
            }

            if (controller != null)
            {
                controller.SetDirectTuning(intensity, range);
                controller.SetPower(powerA, powerB);
                controller.SetDoor(doorFraction);
                controller.SetConnectionEnabled(connectionEnabled);
            }
        }

        private void AppendState(
            StringBuilder csv,
            int caseIndex,
            DungeonTileLightmapSwitcher.PowerLevel powerA,
            DungeonTileLightmapSwitcher.PowerLevel powerB,
            float doorFraction,
            string capturePath)
        {
            RoomLocalConnection connection = controller.Connection;
            RoomLocalDoorProbeDriver driver = controller.DoorProbeDriver;
            Transform leaf = connection != null && connection.DoorAngle != null
                ? connection.DoorAngle.DoorLeaf
                : null;
            Transform doorRoot = leaf != null ? leaf.parent : null;
            Transform doorwayA = connection != null ? connection.StartDoorway : null;
            Transform doorwayB = connection != null ? connection.AdministrativeDoorway : null;
            Doorway ownerA = doorwayA != null ? doorwayA.GetComponent<Doorway>() : null;
            Doorway ownerB = doorwayB != null ? doorwayB.GetComponent<Doorway>() : null;

            controller.Catalog.TryResolve(
                caseIndex,
                out RoomLocalMatrixCatalog.PairCase pair,
                out RoomLocalMatrixCatalog.RoomEntry roomA,
                out RoomLocalMatrixCatalog.DoorwayEntry doorwayEntryA,
                out RoomLocalMatrixCatalog.RoomEntry roomB,
                out RoomLocalMatrixCatalog.DoorwayEntry doorwayEntryB,
                out _);

            string doorName = doorRoot != null ? doorRoot.name : "missing";
            bool selectedInA = HasConnectorNamed(ownerA, doorName);
            bool selectedInB = HasConnectorNamed(ownerB, doorName);
            bool parentIsA = doorRoot != null && doorwayA != null && doorRoot.parent == doorwayA;
            bool parentIsB = doorRoot != null && doorwayB != null && doorRoot.parent == doorwayB;
            bool parentOk = parentIsA || parentIsB;
            Doorway placementOwner = parentIsA ? ownerA : parentIsB ? ownerB : null;
            float doorwayGap = doorwayA != null && doorwayB != null
                ? Vector3.Distance(doorwayA.position, doorwayB.position)
                : float.NaN;
            float forwardDot = doorwayA != null && doorwayB != null
                ? Vector3.Dot(doorwayA.forward.normalized, doorwayB.forward.normalized)
                : float.NaN;
            Vector3 expectedLocalOffset = placementOwner != null
                ? placementOwner.DoorPrefabPositionOffset
                : Vector3.zero;
            float localOffsetError = doorRoot != null
                ? Vector3.Distance(doorRoot.localPosition, expectedLocalOffset)
                : float.NaN;
            float hingeDistance = leaf != null && placementOwner != null
                ? Vector3.Distance(leaf.position, placementOwner.transform.position)
                : float.NaN;
            Bounds leafBounds = CalculateVisibleBounds(leaf, out bool hasLeafBounds);
            Vector3 leafSize = hasLeafBounds ? leafBounds.size : Vector3.zero;
            Rect viewportRect = hasLeafBounds
                ? CalculateViewportRect(controller.PreviewCamera, leafBounds)
                : new Rect(float.NaN, float.NaN, float.NaN, float.NaN);
            string placementFlags = BuildPlacementFlags(
                doorwayEntryA != null ? doorwayEntryA.Kind : string.Empty,
                doorwayEntryB != null ? doorwayEntryB.Kind : string.Empty,
                doorName,
                parentOk,
                doorwayGap,
                forwardDot,
                localOffsetError,
                selectedInA,
                selectedInB,
                parentIsA,
                parentIsB);

            AppendCsv(csv,
                (caseIndex + 1).ToString(CultureInfo.InvariantCulture),
                pair != null ? pair.CaseId : controller.CaseLabel,
                roomA != null ? roomA.RoomId : "?",
                doorwayEntryA != null ? doorwayEntryA.Kind : "?",
                roomB != null ? roomB.RoomId : "?",
                doorwayEntryB != null ? doorwayEntryB.Kind : "?",
                powerA.ToString(),
                powerB.ToString(),
                F(doorFraction),
                doorName,
                parentOk.ToString(),
                F(doorwayGap),
                F(forwardDot),
                F(localOffsetError),
                F(hingeDistance),
                F(leafSize.x),
                F(leafSize.y),
                F(leafSize.z),
                selectedInA.ToString(),
                selectedInB.ToString(),
                placementFlags,
                (driver != null && driver.IsApplied).ToString(),
                (driver != null && driver.IsFaultLatched).ToString(),
                (driver != null ? driver.BoundRendererCount : 0).ToString(CultureInfo.InvariantCulture),
                (driver != null && driver.LastUsedFaceNormalSampling).ToString(),
                F(driver != null ? driver.LastStartFacingLuminance : float.NaN),
                F(driver != null ? driver.LastAdministrativeFacingLuminance : float.NaN),
                F(driver != null ? driver.LastEdgeLuminance : float.NaN),
                (connection != null && connection.IsFaultLatched).ToString(),
                controller.HasCompleteOutgoing.ToString(),
                controller.HasCompleteBounce.ToString(),
                capturePath,
                F(viewportRect.xMin),
                F(viewportRect.yMin),
                F(viewportRect.xMax),
                F(viewportRect.yMax));
        }

        private static void AppendMissingCase(StringBuilder csv, int caseIndex, string status)
        {
            AppendCsv(csv,
                (caseIndex + 1).ToString(CultureInfo.InvariantCulture),
                status,
                "?", "?", "?", "?", "?", "?", "?", "missing",
                "False", "NaN", "NaN", "NaN", "NaN", "0", "0", "0",
                "False", "False", "CASE_BUILD_FAILED", "False", "True", "0",
                "False", "NaN", "NaN", "NaN", "True", "False", "False",
                string.Empty, "NaN", "NaN", "NaN", "NaN");
        }

        private string BuildCapturePath(
            int caseIndex,
            DungeonTileLightmapSwitcher.PowerLevel powerA,
            DungeonTileLightmapSwitcher.PowerLevel powerB,
            float doorFraction)
        {
            if (string.IsNullOrEmpty(captureDirectory) ||
                powerA != DungeonTileLightmapSwitcher.PowerLevel.P0 ||
                powerB != DungeonTileLightmapSwitcher.PowerLevel.P100)
                return string.Empty;

            int doorPercent = Mathf.RoundToInt(doorFraction * 100f);
            return Path.Combine(
                captureDirectory,
                $"Case_{caseIndex + 1:D2}_P0_P100_D{doorPercent:D3}.png");
        }

        private static Rect CalculateViewportRect(Camera camera, Bounds bounds)
        {
            if (camera == null)
                return new Rect(float.NaN, float.NaN, float.NaN, float.NaN);

            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3[] corners =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, max.y, max.z)
            };

            float minX = 1f;
            float minY = 1f;
            float maxX = 0f;
            float maxY = 0f;
            bool found = false;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 viewport = camera.WorldToViewportPoint(corners[i]);
                if (viewport.z <= 0f)
                    continue;
                minX = Mathf.Min(minX, viewport.x);
                minY = Mathf.Min(minY, viewport.y);
                maxX = Mathf.Max(maxX, viewport.x);
                maxY = Mathf.Max(maxY, viewport.y);
                found = true;
            }

            if (!found)
                return new Rect(float.NaN, float.NaN, float.NaN, float.NaN);
            minX = Mathf.Clamp01(minX);
            minY = Mathf.Clamp01(minY);
            maxX = Mathf.Clamp01(maxX);
            maxY = Mathf.Clamp01(maxY);
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static Bounds CalculateVisibleBounds(Transform leaf, out bool hasBounds)
        {
            Bounds bounds = default;
            hasBounds = false;
            if (leaf == null)
                return bounds;

            DungeonDoorProbeRendererGroup[] groups =
                leaf.GetComponentsInChildren<DungeonDoorProbeRendererGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                Renderer renderer = groups[i] != null ? groups[i].GetComponent<Renderer>() : null;
                if (renderer == null || !renderer.enabled)
                    continue;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return bounds;
        }

        private static bool HasConnectorNamed(Doorway doorway, string prefabName)
        {
            if (doorway == null || doorway.ConnectorPrefabWeights == null)
                return false;
            for (int i = 0; i < doorway.ConnectorPrefabWeights.Count; i++)
            {
                var weighted = doorway.ConnectorPrefabWeights[i];
                if (weighted != null && weighted.Weight > 0f && weighted.GameObject != null &&
                    weighted.GameObject.name == prefabName)
                    return true;
            }
            return false;
        }

        private static string BuildPlacementFlags(
            string kindA,
            string kindB,
            string doorName,
            bool parentOk,
            float doorwayGap,
            float forwardDot,
            float localOffsetError,
            bool selectedInA,
            bool selectedInB,
            bool parentIsA,
            bool parentIsB)
        {
            var flags = new List<string>();
            if (!parentOk)
                flags.Add("WRONG_PARENT");
            if (!float.IsNaN(doorwayGap) && doorwayGap > 0.02f)
                flags.Add("DOORWAY_GAP");
            if (!float.IsNaN(forwardDot) && forwardDot > -0.98f)
                flags.Add("DOORWAY_DIRECTION");
            if (!float.IsNaN(localOffsetError) && localOffsetError > 0.005f)
                flags.Add("OFFSET_MISMATCH");
            if ((parentIsA && !selectedInA && selectedInB) ||
                (parentIsB && !selectedInB && selectedInA))
                flags.Add("PREFAB_NOT_IN_OWNER");
            if (!string.Equals(kindA, kindB, StringComparison.Ordinal))
                flags.Add("MIXED_KIND");
            string ownerKind = parentIsA ? kindA : parentIsB ? kindB : string.Empty;
            if (doorName.Contains("_LG_", StringComparison.Ordinal) &&
                !string.Equals(ownerKind, "LG", StringComparison.Ordinal))
                flags.Add("LG_ON_NON_LG_OWNER");
            if (doorName.Contains("_SM_", StringComparison.Ordinal) &&
                !string.Equals(ownerKind, "SM", StringComparison.Ordinal))
                flags.Add("SM_ON_NON_SM_OWNER");
            return flags.Count > 0 ? string.Join("|", flags) : "OK";
        }

        private static DungeonTileLightmapSwitcher.PowerLevel CurrentPower(
            DungeonTileLightmapSwitcher switcher)
        {
            return switcher != null
                ? switcher.CurrentPowerLevel
                : DungeonTileLightmapSwitcher.PowerLevel.P0;
        }

        private static string F(float value)
        {
            return float.IsNaN(value)
                ? "NaN"
                : value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static void AppendCsv(StringBuilder builder, params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    builder.Append(',');
                string value = values[i] ?? string.Empty;
                if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                    builder.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
                else
                    builder.Append(value);
            }
            builder.AppendLine();
        }
    }
}
