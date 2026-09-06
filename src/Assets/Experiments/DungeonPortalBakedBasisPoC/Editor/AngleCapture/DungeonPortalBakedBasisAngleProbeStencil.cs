using System;
using System.Collections.Generic;
using DunGen;
using DungeonPortalTransportPoC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DungeonPortalTransportPoC.Editor
{
    public static partial class DungeonPortalReceiverBounceBaker
    {
        private const int RequiredAngleProbeSampleCount = 27;
        private const float AngleProbeCollisionRadius = 0.05f;
        private const float AngleProbeGeometryClearance = 0.075f;
        private const float CanonicalNearProbeDepth = -0.25f;
        private const float CanonicalMiddleProbeDepth = -1.5f;
        private const float CanonicalFarProbeDepth = -3f;
        private const string PoseAwareAngleProbeStencilPolicy =
            "Pose-aware physical doorway aperture: equal-area thirds of the closed door collider aperture; " +
            "the receiver-side near plane is the canonical -0.25m plane or the first plane beyond the " +
            "posed door OBB by 0.075m, whichever is deeper; middle/far planes remain -1.5m/-3m; " +
            "all 27 required samples must clear receiver and moving-door colliders by a 0.05m sphere; " +
            "no point nudging or sample dropping.";

        public readonly struct DoorAngleProbeStencilDiagnostic
        {
            public DoorAngleProbeStencilDiagnostic(
                float openFraction,
                Vector3[] localPositions,
                string localPositionSignature,
                float nearDepth,
                float validatedCollisionRadius,
                bool legacyRejectedPointOverlapsDoor,
                string legacyRejectedColliderName)
            {
                OpenFraction = openFraction;
                LocalPositions = localPositions != null ? (Vector3[])localPositions.Clone() : Array.Empty<Vector3>();
                LocalPositionSignature = localPositionSignature ?? string.Empty;
                NearDepth = nearDepth;
                ValidatedCollisionRadius = validatedCollisionRadius;
                LegacyRejectedPointOverlapsDoor = legacyRejectedPointOverlapsDoor;
                LegacyRejectedColliderName = legacyRejectedColliderName ?? string.Empty;
            }

            public float OpenFraction { get; }
            public Vector3[] LocalPositions { get; }
            public int SampleCount => LocalPositions != null ? LocalPositions.Length : 0;
            public string LocalPositionSignature { get; }
            public float NearDepth { get; }
            public float ValidatedCollisionRadius { get; }
            public bool LegacyRejectedPointOverlapsDoor { get; }
            public string LegacyRejectedColliderName { get; }
        }

        public static DoorAngleProbeStencilDiagnostic InspectStartAngleProbeStencilForTest(
            float openFraction)
        {
            if (!IsFinite(openFraction) || openFraction < 0f || openFraction > 1f)
                throw new ArgumentOutOfRangeException(nameof(openFraction));

            Scene source = SceneManager.GetActiveScene();
            if (!source.IsValid() || !source.isLoaded || source.isDirty || string.IsNullOrWhiteSpace(source.path))
            {
                throw new InvalidOperationException(
                    "Pose-stencil inspection requires one saved, clean source scene for exact restoration.");
            }
            Scene fixtureScene = default;
            try
            {
                fixtureScene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Additive);
                if (!fixtureScene.IsValid() || !fixtureScene.isLoaded)
                    throw new InvalidOperationException("Unable to create the transient angle-stencil fixture scene.");
                AngleWorkspaceObjects objects = BuildStartAngleProbeFixture(fixtureScene);
                objects.DoorLeaf.localRotation = ComputeDoorAnglePoseForTest(
                    objects.Marker.ClosedLocalRotation,
                    objects.Marker.LocalHingeAxis,
                    objects.Marker.OpenAngleDegrees,
                    openFraction);
                Physics.SyncTransforms();

                AngleProbeStencil stencil = BuildPoseAwareAngleProbeStencil(objects, openFraction);
                Vector3 legacyRejectedWorld = objects.Doorway.TransformPoint(new Vector3(0f, 0.25f, -0.25f));
                bool legacyOverlap = TryFindOwnedColliderOverlap(
                    legacyRejectedWorld,
                    AngleProbeCollisionRadius,
                    objects.Room,
                    objects.RealDoor,
                    true,
                    out Collider legacyCollider,
                    out _);
                return new DoorAngleProbeStencilDiagnostic(
                    openFraction,
                    stencil.LocalPositions,
                    stencil.LocalPositionSignature,
                    stencil.LocalPositions[0].z,
                    AngleProbeCollisionRadius,
                    legacyOverlap,
                    legacyCollider != null ? legacyCollider.name : string.Empty);
            }
            finally
            {
                if (fixtureScene.IsValid() && fixtureScene.isLoaded &&
                    !EditorSceneManager.CloseScene(fixtureScene, true))
                    throw new InvalidOperationException("Unable to close the transient angle-stencil fixture scene.");
                if (SceneManager.GetActiveScene() != source && !EditorSceneManager.SetActiveScene(source))
                    throw new InvalidOperationException("Unable to restore the source scene after stencil inspection.");
                if (!source.IsValid() || !source.isLoaded || source.isDirty)
                    throw new InvalidOperationException("Angle-stencil inspection did not preserve the clean source scene.");
            }
        }

        private static AngleWorkspaceObjects BuildStartAngleProbeFixture(Scene fixtureScene)
        {
            GameObject roomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StartSpec.RoomPrefabPath);
            GameObject doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath);
            if (roomPrefab == null || doorPrefab == null)
                throw new InvalidOperationException("Required production room/door prefab is missing for stencil inspection.");

            GameObject room = PrefabUtility.InstantiatePrefab(roomPrefab, fixtureScene) as GameObject;
            if (room == null)
                throw new InvalidOperationException("Unable to instantiate the transient Start receiver fixture.");
            room.name = GetRoomInstanceName(StartSpec);
            room.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            room.transform.localScale = Vector3.one;
            Doorway doorway = ResolveExactStableDoorway(room, StartSpec.RoomId);
            ConfigureDoorwayVisualsForRealDoor(doorway.transform);
            GameObject realDoor = InstantiateDoorUsingDunGenPlacement(doorPrefab, fixtureScene, doorway);
            Transform leaf = BindAngleDoorClosed(realDoor, doorway);
            var marker = realDoor.AddComponent<DungeonPortalBakedBasisAngleWorkspaceMarker>();
            marker.Configure(StartSpec.RoomId, leaf.localRotation, Vector3.up, AngleCaptureOpenAngleDegrees);
            return new AngleWorkspaceObjects(room, doorway.transform, realDoor, leaf, null, marker);
        }

        private static DungeonPortalReceiverResponseCapture.ProbeSample[]
            CapturePoseAwareAngleProbeGrid(
                AngleWorkspaceObjects objects,
                DoorAngleCaptureContract contract,
                out string localPositionSignature)
        {
            if (objects == null)
                throw new ArgumentNullException(nameof(objects));
            if (LightmapSettings.lightProbes == null)
                throw new InvalidOperationException("The completed angle bake has no LightProbes asset for doorway response sampling.");

            AngleProbeStencil stencil = BuildPoseAwareAngleProbeStencil(objects, contract.OpenFraction);
            localPositionSignature = stencil.LocalPositionSignature;
            Vector3[] localPositions = stencil.LocalPositions;
            var worldPositions = new Vector3[localPositions.Length];
            for (int i = 0; i < localPositions.Length; i++)
                worldPositions[i] = objects.Doorway.TransformPoint(localPositions[i]);

            var harmonics = new SphericalHarmonicsL2[worldPositions.Length];
            var occlusions = new Vector4[worldPositions.Length];
            LightProbes.CalculateInterpolatedLightAndOcclusionProbes(worldPositions, harmonics, occlusions);
            var samples = new DungeonPortalReceiverResponseCapture.ProbeSample[worldPositions.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = new DungeonPortalReceiverResponseCapture.ProbeSample
                {
                    probeIndex = i,
                    localPosition = localPositions[i],
                    worldPosition = worldPositions[i],
                    coefficient0 = GetShCoefficient(harmonics[i], 0),
                    coefficient1 = GetShCoefficient(harmonics[i], 1),
                    coefficient2 = GetShCoefficient(harmonics[i], 2),
                    coefficient3 = GetShCoefficient(harmonics[i], 3),
                    coefficient4 = GetShCoefficient(harmonics[i], 4),
                    coefficient5 = GetShCoefficient(harmonics[i], 5),
                    coefficient6 = GetShCoefficient(harmonics[i], 6),
                    coefficient7 = GetShCoefficient(harmonics[i], 7),
                    coefficient8 = GetShCoefficient(harmonics[i], 8),
                    occlusion = occlusions[i]
                };
                if (!IsFinite(samples[i].occlusion) || !IsFinite(samples[i].coefficient0) ||
                    !IsFinite(samples[i].coefficient1) || !IsFinite(samples[i].coefficient2) ||
                    !IsFinite(samples[i].coefficient3) || !IsFinite(samples[i].coefficient4) ||
                    !IsFinite(samples[i].coefficient5) || !IsFinite(samples[i].coefficient6) ||
                    !IsFinite(samples[i].coefficient7) || !IsFinite(samples[i].coefficient8))
                {
                    throw new InvalidOperationException("Pose-aware doorway probe capture produced non-finite SH data.");
                }
            }
            return samples;
        }

        private static AngleProbeStencil BuildPoseAwareAngleProbeStencil(
            AngleWorkspaceObjects objects,
            float openFraction)
        {
            if (objects == null || !IsFinite(openFraction) || openFraction < 0f || openFraction > 1f)
                throw new ArgumentOutOfRangeException(nameof(openFraction));

            BoxCollider doorCollider = ResolveUniqueDoorLeafBoxCollider(objects);
            DoorwayLocalBounds closedBounds = ComputeDoorColliderBoundsAtClosedPose(objects, doorCollider);
            DoorwayLocalBounds posedBounds = ComputeDoorwayLocalBounds(
                objects.Doorway.worldToLocalMatrix * doorCollider.transform.localToWorldMatrix,
                doorCollider.center,
                doorCollider.size);

            float xMin = closedBounds.Min.x + AngleProbeGeometryClearance;
            float xMax = closedBounds.Max.x - AngleProbeGeometryClearance;
            float yMin = closedBounds.Min.y + AngleProbeGeometryClearance;
            float yMax = closedBounds.Max.y - AngleProbeGeometryClearance;
            if (xMax <= xMin || yMax <= yMin)
                throw new InvalidOperationException("The closed door collider does not define a usable physical aperture.");

            float nearDepth = Mathf.Min(
                CanonicalNearProbeDepth,
                posedBounds.Min.z - AngleProbeGeometryClearance);
            if (nearDepth <= CanonicalMiddleProbeDepth + AngleProbeGeometryClearance)
            {
                throw new InvalidOperationException(
                    "The posed door consumes the required near-to-middle doorway sampling volume. " +
                    "The tool will not relocate or drop samples.");
            }

            float[] depths =
            {
                nearDepth,
                CanonicalMiddleProbeDepth,
                CanonicalFarProbeDepth
            };
            var positions = new Vector3[RequiredAngleProbeSampleCount];
            int index = 0;
            for (int zIndex = 0; zIndex < depths.Length; zIndex++)
            {
                for (int yIndex = 0; yIndex < 3; yIndex++)
                {
                    float y = Mathf.Lerp(yMin, yMax, (yIndex + 0.5f) / 3f);
                    for (int xIndex = 0; xIndex < 3; xIndex++)
                    {
                        float x = Mathf.Lerp(xMin, xMax, (xIndex + 0.5f) / 3f);
                        positions[index++] = new Vector3(x, y, depths[zIndex]);
                    }
                }
            }
            if (index != RequiredAngleProbeSampleCount)
                throw new InvalidOperationException("Pose-aware doorway stencil did not produce all 27 required samples.");

            Physics.SyncTransforms();
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 world = objects.Doorway.TransformPoint(positions[i]);
                if (TryFindOwnedColliderOverlap(
                        world,
                        AngleProbeCollisionRadius,
                        objects.Room,
                        objects.RealDoor,
                        false,
                        out Collider overlap,
                        out _))
                {
                    throw new InvalidOperationException(
                        "Pose-aware doorway probe stencil overlaps receiver/door geometry at index " + i +
                        " local=" + positions[i] + " collider='" + overlap.name + "' radius=" +
                        F(AngleProbeCollisionRadius) + ". The tool will not nudge or drop required samples.");
                }
            }

            return new AngleProbeStencil(
                positions,
                ComputeProbeLocalPositionSignature(positions));
        }

        private static BoxCollider ResolveUniqueDoorLeafBoxCollider(AngleWorkspaceObjects objects)
        {
            BoxCollider[] candidates = objects.DoorLeaf.GetComponentsInChildren<BoxCollider>(true);
            BoxCollider result = null;
            int count = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                BoxCollider candidate = candidates[i];
                if (candidate == null || !candidate.enabled || candidate.isTrigger ||
                    !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }
                result = candidate;
                count++;
            }
            if (count != 1 || result == null || result.transform != objects.DoorLeaf)
            {
                throw new InvalidOperationException(
                    "Pose-aware aperture sampling requires exactly one enabled, non-trigger BoxCollider on Door_01.");
            }
            return result;
        }

        private static DoorwayLocalBounds ComputeDoorColliderBoundsAtClosedPose(
            AngleWorkspaceObjects objects,
            BoxCollider doorCollider)
        {
            Transform leaf = objects.DoorLeaf;
            if (leaf.parent == null || doorCollider.transform != leaf)
                throw new InvalidOperationException("Door collider/leaf hierarchy is not the exact supported contract.");
            Matrix4x4 closedLeafLocalToWorld = leaf.parent.localToWorldMatrix * Matrix4x4.TRS(
                leaf.localPosition,
                objects.Marker.ClosedLocalRotation,
                leaf.localScale);
            Matrix4x4 closedColliderToDoorway = objects.Doorway.worldToLocalMatrix * closedLeafLocalToWorld;
            return ComputeDoorwayLocalBounds(
                closedColliderToDoorway,
                doorCollider.center,
                doorCollider.size);
        }

        private static DoorwayLocalBounds ComputeDoorwayLocalBounds(
            Matrix4x4 colliderToDoorway,
            Vector3 center,
            Vector3 size)
        {
            Vector3 half = size * 0.5f;
            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = center + Vector3.Scale(half, new Vector3(x, y, z));
                        Vector3 doorwayLocal = colliderToDoorway.MultiplyPoint3x4(corner);
                        min = Vector3.Min(min, doorwayLocal);
                        max = Vector3.Max(max, doorwayLocal);
                    }
                }
            }
            if (!IsFinite(min) || !IsFinite(max) || min.x >= max.x || min.y >= max.y || min.z >= max.z)
                throw new InvalidOperationException("Door collider produced invalid doorway-local bounds.");
            return new DoorwayLocalBounds(min, max);
        }

        private static bool TryFindOwnedColliderOverlap(
            Vector3 worldPosition,
            float radius,
            GameObject receiver,
            GameObject realDoor,
            bool doorOnly,
            out Collider overlap,
            out float clearance)
        {
            overlap = null;
            clearance = float.PositiveInfinity;
            Collider[] colliders = Physics.OverlapSphere(
                worldPosition,
                radius,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider candidate = colliders[i];
                if (candidate == null || !candidate.enabled || candidate.isTrigger ||
                    !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }
                bool belongsToDoor = candidate.transform == realDoor.transform ||
                                     candidate.transform.IsChildOf(realDoor.transform);
                bool belongsToReceiver = candidate.transform == receiver.transform ||
                                         candidate.transform.IsChildOf(receiver.transform);
                if (!belongsToReceiver && !belongsToDoor)
                    continue;
                if (doorOnly && !belongsToDoor)
                    continue;
                overlap = candidate;
                clearance = 0f;
                return true;
            }
            return false;
        }

        private static string ComputeAngleProbeLocalPositionSignature(
            DungeonPortalReceiverResponseCapture.ProbeSample[] probes)
        {
            if (probes == null)
                return string.Empty;
            var positions = new Vector3[probes.Length];
            for (int i = 0; i < probes.Length; i++)
            {
                if (probes[i].probeIndex != i || !IsFinite(probes[i].localPosition))
                    return string.Empty;
                positions[i] = probes[i].localPosition;
            }
            return ComputeProbeLocalPositionSignature(positions);
        }

        private readonly struct AngleProbeStencil
        {
            public AngleProbeStencil(Vector3[] localPositions, string localPositionSignature)
            {
                LocalPositions = localPositions;
                LocalPositionSignature = localPositionSignature;
            }

            public Vector3[] LocalPositions { get; }
            public string LocalPositionSignature { get; }
        }

        private readonly struct DoorwayLocalBounds
        {
            public DoorwayLocalBounds(Vector3 min, Vector3 max)
            {
                Min = min;
                Max = max;
            }

            public Vector3 Min { get; }
            public Vector3 Max { get; }
        }
    }
}
