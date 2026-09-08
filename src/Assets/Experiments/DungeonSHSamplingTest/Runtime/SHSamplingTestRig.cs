using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DungeonSHSamplingTest
{
    public sealed class SHSamplingTestRig : MonoBehaviour
    {
        public Transform roomA, roomB;
        public DungeonTileLightmapSwitcher switcherA, switcherB;
        public Bounds localBoundsA, localBoundsB;
        public Collider[] blockers;
        // Door bottom center; local +Z points from A to B. Stations are world positions.
        public Transform portalFrame, doorLeaf, actor, sampleAnchor;
        public Renderer[] actorRenderers;
        public Camera viewCamera;
        public float portalWidth = 2f, portalHeight = 2.4f;
        public bool improved = true, doorOpen, powerA = true, powerB, useBodyAnchor = true;
        public Vector3[] stations = Array.Empty<Vector3>();
        public Vector3[] stationCameraOffsets = Array.Empty<Vector3>();
        public string[] stationNames = Array.Empty<string>();
        public bool showHud = true, followCamera = true;
        public float moveSpeed = 2f;

        public SHSamplingTestSampler.Result BaselineResult { get; private set; }
        public SHSamplingTestSampler.Result ImprovedResult { get; private set; }
        public Vector3 SamplePosition { get; private set; }
        private SHSamplingTestSampler.Probe[] probesA = Array.Empty<SHSamplingTestSampler.Probe>();
        private SHSamplingTestSampler.Probe[] probesB = Array.Empty<SHSamplingTestSampler.Probe>();
        private readonly Dictionary<int, Vector3> probePositions = new Dictionary<int, Vector3>();
        private MaterialPropertyBlock block;
        private readonly SphericalHarmonicsL2[] oneSH = new SphericalHarmonicsL2[1];
        private readonly Vector4[] oneOcclusion = new Vector4[1];
        private bool initialized;
        private float nextRefresh;
        private Vector3 lastSamplePosition, cameraOffset;
        private int station = -1, failureCount, baselineOccluded, improvedOccluded, diagnosticRays;
        private double sampleMilliseconds;
        private string configurationError;

        private void OnEnable() { initialized = false; }
        private void Start() { Initialize(); }

        public void Initialize()
        {
            // Unity loads scene components on a serialization thread. Native rendering
            // objects must be created here on the main thread, not in field initializers.
            if (block == null) block = new MaterialPropertyBlock();
            initialized = true;
            configurationError = roomA == null || roomB == null || switcherA == null ||
                switcherB == null || portalFrame == null || actor == null
                ? "Assign both rooms, switchers, portal and actor." : null;
            if (viewCamera != null && actor != null)
                cameraOffset = viewCamera.transform.position - actor.position;
            ApplyState();
        }

        public void ApplyState()
        {
            if (!initialized) { Initialize(); return; }
            if (switcherA != null)
                switcherA.SetPowerLevel(powerA ? DungeonTileLightmapSwitcher.PowerLevel.P100 : DungeonTileLightmapSwitcher.PowerLevel.P0);
            if (switcherB != null)
                switcherB.SetPowerLevel(powerB ? DungeonTileLightmapSwitcher.PowerLevel.P100 : DungeonTileLightmapSwitcher.PowerLevel.P0);
            if (doorLeaf != null)
                doorLeaf.localRotation = Quaternion.Euler(0f, doorOpen ? 90f : 0f, 0f);
            Physics.SyncTransforms();
            probePositions.Clear();
            probesA = BuildProbes(roomA, switcherA, 0);
            probesB = BuildProbes(roomB, switcherB, 1 << 24);
            RefreshSample();
        }

        public void SetMode(bool value) { improved = value; RefreshSample(); }
        public void SetDoor(bool value) { doorOpen = value; ApplyState(); }
        public void SetPowers(bool a, bool b) { powerA = a; powerB = b; ApplyState(); }
        public void SetStation(int index)
        {
            if (actor == null || stations == null || index < 0 || index >= stations.Length) return;
            station = index;
            actor.position = stations[index];
            Physics.SyncTransforms();
            if (viewCamera != null && portalFrame != null)
            {
                float side = portalFrame.InverseTransformPoint(actor.position).z > 0f ? 1f : -1f;
                cameraOffset = portalFrame.forward * (side * 3f) + portalFrame.right * 1.6f + Vector3.up * 1.7f;
                if (stationCameraOffsets != null && index < stationCameraOffsets.Length && stationCameraOffsets[index].sqrMagnitude > 0f)
                    cameraOffset = stationCameraOffsets[index];
                viewCamera.transform.position = actor.position + cameraOffset;
                viewCamera.transform.LookAt(actor.position + Vector3.up);
            }
            FollowActor();
            RefreshSample();
        }

        private SHSamplingTestSampler.Probe[] BuildProbes(Transform root, DungeonTileLightmapSwitcher switcher, int idBase)
        {
            DungeonTileBakeData bake = switcher != null ? switcher.CurrentBakeData : null;
            if (root == null || bake == null || bake.lightProbeEntries == null)
                return Array.Empty<SHSamplingTestSampler.Probe>();
            var probes = new SHSamplingTestSampler.Probe[bake.lightProbeEntries.Length];
            for (int i = 0; i < probes.Length; i++)
            {
                var entry = bake.lightProbeEntries[i];
                probes[i] = new SHSamplingTestSampler.Probe {
                    Position = root.TransformPoint(entry.localPosition), SH = entry.ToSphericalHarmonics(),
                    Occlusion = entry.occlusion, Id = idBase + i };
                probePositions[probes[i].Id] = probes[i].Position;
            }
            return probes;
        }

        public void RefreshSample()
        {
            if (!initialized) { Initialize(); return; }
            SamplePosition = useBodyAnchor && sampleAnchor != null ? sampleAnchor.position :
                actor != null ? actor.position : Vector3.zero;
            long started = Stopwatch.GetTimestamp();
            BaselineResult = Evaluate(SamplePosition, false);
            ImprovedResult = Evaluate(SamplePosition, true);
            diagnosticRays = 0;
            baselineOccluded = CountOccluded(BaselineResult, SamplePosition);
            improvedOccluded = CountOccluded(ImprovedResult, SamplePosition);
            sampleMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            SHSamplingTestSampler.Result active = improved ? ImprovedResult : BaselineResult;
            if (!active.Success) failureCount++;
            ApplyToActor(active);
            lastSamplePosition = SamplePosition;
            nextRefresh = Time.unscaledTime + 0.1f;
        }

        private SHSamplingTestSampler.Result Evaluate(Vector3 position, bool candidate)
        {
            if (configurationError != null)
                return new SHSamplingTestSampler.Result { ProbeIds = Array.Empty<int>(), Occlusion = Vector4.one };
            Vector3 portal = portalFrame.InverseTransformPoint(position);
            bool ownerA = Mathf.Abs(portal.z) <= 1.35f ? portal.z <= 0f :
                localBoundsA.SqrDistance(roomA.InverseTransformPoint(position)) <=
                localBoundsB.SqrDistance(roomB.InverseTransformPoint(position));
            bool blend = candidate
                ? doorOpen && Mathf.Abs(portal.z) <= 0.6f && Mathf.Abs(portal.x) <= portalWidth * 0.5f &&
                    portal.y >= 0f && portal.y <= portalHeight
                : Mathf.Abs(portal.z) <= 1.35f && Mathf.Abs(portal.x) <= portalWidth * 0.5f + 0.75f;
            if (!blend)
                return SHSamplingTestSampler.Sample(ownerA ? probesA : probesB, position, candidate, blockers);

            var a = SHSamplingTestSampler.Sample(probesA, position, candidate, blockers);
            var b = SHSamplingTestSampler.Sample(probesB, position, candidate, blockers);
            if (!a.Success || !b.Success)
            {
                // A visible own-room sample may survive an unavailable neighbour. Never use
                // the neighbour to hide a failure in the room that contains the receiver.
                var own = ownerA ? a : b;
                own.CandidateCount = a.CandidateCount + b.CandidateCount;
                own.RejectedCount = a.RejectedCount + b.RejectedCount;
                own.RayCount = a.RayCount + b.RayCount;
                own.UsedFallback = own.Success;
                return own;
            }
            float depth = candidate ? 0.6f : 1.35f;
            float weightB = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-depth, depth, portal.z));
            var result = new SHSamplingTestSampler.Result {
                Success = true, Occlusion = Vector4.Lerp(a.Occlusion, b.Occlusion, weightB),
                CandidateCount = a.CandidateCount + b.CandidateCount,
                RejectedCount = a.RejectedCount + b.RejectedCount, RayCount = a.RayCount + b.RayCount,
                ProbeIds = new int[a.ProbeIds.Length + b.ProbeIds.Length],
                Luminance = Mathf.Lerp(a.Luminance, b.Luminance, weightB) };
            for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    result.SH[channel, coefficient] = Mathf.Lerp(a.SH[channel, coefficient], b.SH[channel, coefficient], weightB);
            Array.Copy(a.ProbeIds, result.ProbeIds, a.ProbeIds.Length);
            Array.Copy(b.ProbeIds, 0, result.ProbeIds, a.ProbeIds.Length, b.ProbeIds.Length);
            return result;
        }

        private int CountOccluded(SHSamplingTestSampler.Result result, Vector3 position)
        {
            int count = 0;
            if (result.ProbeIds == null) return count;
            foreach (int id in result.ProbeIds)
                if (probePositions.TryGetValue(id, out Vector3 probePosition))
                {
                    if (!SHSamplingTestSampler.IsVisible(position, probePosition, blockers, out int rays)) count++;
                    diagnosticRays += rays;
                }
            return count;
        }

        private void ApplyToActor(SHSamplingTestSampler.Result result)
        {
            if (block == null) block = new MaterialPropertyBlock();
            oneSH[0] = result.Success ? result.SH : default;
            oneOcclusion[0] = result.Success ? result.Occlusion : Vector4.one;
            if (actorRenderers == null) return;
            foreach (Renderer target in actorRenderers)
            {
                if (target == null) continue;
                target.lightProbeUsage = LightProbeUsage.CustomProvided;
                int materialCount = target.sharedMaterials.Length;
                for (int i = 0; i < materialCount; i++)
                {
                    block.Clear();
                    target.GetPropertyBlock(block, i);
                    block.CopySHCoefficientArraysFrom(oneSH);
                    block.CopyProbeOcclusionArrayFrom(oneOcclusion);
                    target.SetPropertyBlock(block, i);
                }
            }
        }

        private void Update()
        {
            if (!initialized) Initialize();
            if (actor == null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) -
                    (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
                float z = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) -
                    (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
                Vector3 move = new Vector3(x, 0f, z);
                if (move.sqrMagnitude > 0f)
                {
                    actor.position += move.normalized * moveSpeed * Time.unscaledDeltaTime;
                    station = -1;
                }
            }
            FollowActor();
            Vector3 sample = useBodyAnchor && sampleAnchor != null ? sampleAnchor.position : actor.position;
            if ((sample - lastSamplePosition).sqrMagnitude > 0.0001f || Time.unscaledTime >= nextRefresh)
                RefreshSample();
        }

        private void FollowActor()
        {
            if (followCamera && viewCamera != null && actor != null)
                viewCamera.transform.position = actor.position + cameraOffset;
        }

        [Serializable]
        private sealed class Snapshot
        {
            public bool success, usedFallback;
            public float luminance;
            public int validCount, candidateCount, rejectedCount, rayCount, occludedSelectedCount;
            public int[] probeIds;
            public Snapshot(SHSamplingTestSampler.Result result, int occluded)
            {
                success = result.Success; usedFallback = result.UsedFallback; luminance = result.Luminance;
                probeIds = result.ProbeIds ?? Array.Empty<int>(); validCount = probeIds.Length;
                candidateCount = result.CandidateCount; rejectedCount = result.RejectedCount;
                rayCount = result.RayCount; occludedSelectedCount = occluded;
            }
        }

        [Serializable]
        private sealed class Report
        {
            public string configurationError, station, mode, anchor;
            public bool doorOpen, powerA, powerB;
            public Vector3 actorPosition, samplePosition;
            public int failureCount, diagnosticRayCount, probesA, probesB;
            public double sampleCpuMilliseconds;
            public Snapshot baseline, candidate;
        }

        public string GetReportJson()
        {
            return JsonUtility.ToJson(new Report {
                configurationError = configurationError, station = StationName(station),
                mode = improved ? "B: visibility + portal" : "A: distance baseline (test emulation)",
                anchor = useBodyAnchor && sampleAnchor != null ? "body" : "actor root",
                doorOpen = doorOpen, powerA = powerA, powerB = powerB,
                actorPosition = actor != null ? actor.position : Vector3.zero, samplePosition = SamplePosition,
                failureCount = failureCount, diagnosticRayCount = diagnosticRays,
                probesA = probesA.Length, probesB = probesB.Length, sampleCpuMilliseconds = sampleMilliseconds,
                baseline = new Snapshot(BaselineResult, baselineOccluded),
                candidate = new Snapshot(ImprovedResult, improvedOccluded) }, true);
        }

        private string StationName(int index)
        {
            return index < 0 ? "Free movement" : stationNames != null && index < stationNames.Length
                ? stationNames[index] : "Station " + index;
        }

        private void OnGUI()
        {
            if (!showHud) return;
            GUILayout.BeginArea(new Rect(12f, 12f, 370f, Mathf.Min(Screen.height - 24f, 680f)), GUI.skin.box);
            GUILayout.Label("SH SAMPLING LAB - test rooms only");
            GUILayout.Label("WASD / arrows: debug movement (no collision)");
            if (GUILayout.Button(improved ? "Sampler B: visible probes + portal" : "Sampler A: distance baseline (test)")) SetMode(!improved);
            if (GUILayout.Button(useBodyAnchor ? "Anchor: BODY" : "Anchor: ROOT")) { useBodyAnchor = !useBodyAnchor; RefreshSample(); }
            if (GUILayout.Button(doorOpen ? "Door: OPEN" : "Door: CLOSED")) SetDoor(!doorOpen);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(powerA ? "Room A: ON" : "Room A: OFF")) SetPowers(!powerA, powerB);
            if (GUILayout.Button(powerB ? "Room B: ON" : "Room B: OFF")) SetPowers(powerA, !powerB);
            GUILayout.EndHorizontal();
            if (stations != null)
                for (int i = 0; i < stations.Length; i++)
                    if (GUILayout.Button(StationName(i))) SetStation(i);
            GUILayout.Label(StationName(station) + " | sample " + SamplePosition.ToString("F2"));
            DrawResult("A", BaselineResult, baselineOccluded);
            DrawResult("B", ImprovedResult, improvedOccluded);
            GUILayout.Label($"Sample + audit CPU: {sampleMilliseconds:F3} ms (not GPU)");
            GUILayout.Label($"Failures: {failureCount}; failure output = zero SH");
            if (configurationError != null) GUILayout.Label(configurationError);
            GUILayout.EndArea();
        }

        private static void DrawResult(string label, SHSamplingTestSampler.Result result, int occluded)
        {
            GUILayout.Label($"{label}: {(result.Success ? "OK" : "FAIL")} L0 {result.Luminance:F3} | valid {result.ProbeIds?.Length ?? 0} / checked {result.CandidateCount}");
            GUILayout.Label($"Rejected {result.RejectedCount}, selected blocked {occluded}, rays {result.RayCount}, fallback {result.UsedFallback}");
        }
    }
}
