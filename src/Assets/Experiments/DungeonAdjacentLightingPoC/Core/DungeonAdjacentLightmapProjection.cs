using System;
using UnityEngine;

namespace DungeonAdjacentLightingPoC
{
    /// <summary>
    /// CPU reference implementation for the Blades-style secondary lightmap UV projection.
    /// It is deliberately allocation-free after the caller has fetched mesh arrays so the
    /// same math can later be moved to Burst jobs without changing the data contract.
    /// </summary>
    public static class DungeonAdjacentLightmapProjection
    {
        public struct ProjectionResult
        {
            public Vector2 atlasUv;
            public Vector3 closestPointWorld;
            public Vector3 projectionNormalWorld;
            public float distance;
            public int triangleIndex;
        }

        public static bool TryProjectPoint(
            Mesh extensionMesh,
            Matrix4x4 extensionLocalToWorld,
            Vector3 worldPoint,
            Vector4 lightmapScaleOffset,
            float maxProjectionDistance,
            out ProjectionResult result)
        {
            return TryProjectPoint(
                extensionMesh,
                extensionLocalToWorld,
                worldPoint,
                lightmapScaleOffset,
                maxProjectionDistance,
                Vector3.zero,
                0f,
                out result);
        }

        public static bool TryProjectPoint(
            Mesh extensionMesh,
            Matrix4x4 extensionLocalToWorld,
            Vector3 worldPoint,
            Vector4 lightmapScaleOffset,
            float maxProjectionDistance,
            Vector3 receiverSurfaceNormalWorld,
            float minimumPlaneAlignment,
            out ProjectionResult result)
        {
            result = default;

            if (extensionMesh == null)
                return false;

            Vector3[] vertices = extensionMesh.vertices;
            Vector2[] uv2 = extensionMesh.uv2;
            int[] triangles = extensionMesh.triangles;

            if (vertices == null || vertices.Length == 0 ||
                uv2 == null || uv2.Length != vertices.Length ||
                triangles == null || triangles.Length < 3)
                return false;

            float maxDistanceSquared = maxProjectionDistance > 0f
                ? maxProjectionDistance * maxProjectionDistance
                : float.PositiveInfinity;
            float bestDistanceSquared = float.PositiveInfinity;
            Vector3 bestPoint = default;
            Vector3 bestBarycentric = default;
            Vector3 bestPlaneNormalWorld = default;
            int bestTriangle = -1;
            bool filterBySurfaceNormal =
                receiverSurfaceNormalWorld.sqrMagnitude > 0.000001f &&
                minimumPlaneAlignment > 0f;
            Vector3 normalizedReceiverNormal = filterBySurfaceNormal
                ? receiverSurfaceNormalWorld.normalized
                : Vector3.zero;
            float safeMinimumPlaneAlignment = Mathf.Clamp01(minimumPlaneAlignment);

            for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                int index0 = triangles[triangle];
                int index1 = triangles[triangle + 1];
                int index2 = triangles[triangle + 2];
                if ((uint)index0 >= vertices.Length ||
                    (uint)index1 >= vertices.Length ||
                    (uint)index2 >= vertices.Length)
                    continue;

                Vector3 point0 = extensionLocalToWorld.MultiplyPoint3x4(vertices[index0]);
                Vector3 point1 = extensionLocalToWorld.MultiplyPoint3x4(vertices[index1]);
                Vector3 point2 = extensionLocalToWorld.MultiplyPoint3x4(vertices[index2]);
                Vector3 extensionPlaneNormal = Vector3.Cross(
                    point1 - point0,
                    point2 - point0);
                if (extensionPlaneNormal.sqrMagnitude <= 0.000001f)
                    continue;
                extensionPlaneNormal.Normalize();

                if (filterBySurfaceNormal)
                {
                    float alignment = Mathf.Abs(Vector3.Dot(
                        normalizedReceiverNormal,
                        extensionPlaneNormal));
                    if (alignment < safeMinimumPlaneAlignment)
                        continue;
                }

                ClosestPointOnTriangle(
                    worldPoint,
                    point0,
                    point1,
                    point2,
                    out Vector3 closestPoint,
                    out Vector3 barycentric);

                float distanceSquared = (worldPoint - closestPoint).sqrMagnitude;
                if (distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                bestPoint = closestPoint;
                bestBarycentric = barycentric;
                bestPlaneNormalWorld = extensionPlaneNormal;
                bestTriangle = triangle / 3;
            }

            if (bestTriangle < 0 || bestDistanceSquared > maxDistanceSquared)
                return false;

            int triangleOffset = bestTriangle * 3;
            Vector2 rawUv =
                uv2[triangles[triangleOffset]] * bestBarycentric.x +
                uv2[triangles[triangleOffset + 1]] * bestBarycentric.y +
                uv2[triangles[triangleOffset + 2]] * bestBarycentric.z;

            result = new ProjectionResult
            {
                atlasUv = Vector2.Scale(rawUv, new Vector2(lightmapScaleOffset.x, lightmapScaleOffset.y)) +
                          new Vector2(lightmapScaleOffset.z, lightmapScaleOffset.w),
                closestPointWorld = bestPoint,
                projectionNormalWorld = bestPlaneNormalWorld,
                distance = Mathf.Sqrt(bestDistanceSquared),
                triangleIndex = bestTriangle
            };
            return true;
        }

        public static bool TryProjectPointOnAlignedPlane(
            Mesh extensionMesh,
            Matrix4x4 extensionLocalToWorld,
            Vector3 worldPoint,
            Vector4 lightmapScaleOffset,
            Vector3 receiverSurfaceNormalWorld,
            float minimumPlaneAlignment,
            out ProjectionResult result)
        {
            result = default;
            if (extensionMesh == null)
                return false;

            Vector3[] vertices = extensionMesh.vertices;
            Vector2[] uv2 = extensionMesh.uv2;
            int[] triangles = extensionMesh.triangles;
            if (vertices == null || vertices.Length == 0 ||
                uv2 == null || uv2.Length != vertices.Length ||
                triangles == null || triangles.Length < 3)
            {
                return false;
            }

            bool filterBySurfaceNormal = receiverSurfaceNormalWorld.sqrMagnitude > 0.000001f;
            Vector3 receiverNormal = filterBySurfaceNormal
                ? receiverSurfaceNormalWorld.normalized
                : Vector3.zero;
            float safeAlignment = Mathf.Clamp01(minimumPlaneAlignment);
            float bestPlaneDistanceSquared = float.PositiveInfinity;
            Vector3 bestProjectedPoint = default;
            Vector3 bestBarycentric = default;
            Vector3 bestPlaneNormalWorld = default;
            int bestTriangle = -1;

            for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                int index0 = triangles[triangle];
                int index1 = triangles[triangle + 1];
                int index2 = triangles[triangle + 2];
                if ((uint)index0 >= vertices.Length ||
                    (uint)index1 >= vertices.Length ||
                    (uint)index2 >= vertices.Length)
                {
                    continue;
                }

                Vector3 point0 = extensionLocalToWorld.MultiplyPoint3x4(vertices[index0]);
                Vector3 point1 = extensionLocalToWorld.MultiplyPoint3x4(vertices[index1]);
                Vector3 point2 = extensionLocalToWorld.MultiplyPoint3x4(vertices[index2]);
                Vector3 planeNormal = Vector3.Cross(point1 - point0, point2 - point0);
                float normalLengthSquared = planeNormal.sqrMagnitude;
                if (normalLengthSquared <= 0.000001f)
                    continue;
                planeNormal /= Mathf.Sqrt(normalLengthSquared);

                if (filterBySurfaceNormal &&
                    Mathf.Abs(Vector3.Dot(receiverNormal, planeNormal)) < safeAlignment)
                {
                    continue;
                }

                float signedPlaneDistance = Vector3.Dot(worldPoint - point0, planeNormal);
                float planeDistanceSquared = signedPlaneDistance * signedPlaneDistance;
                if (planeDistanceSquared >= bestPlaneDistanceSquared)
                    continue;

                Vector3 projectedPoint = worldPoint - planeNormal * signedPlaneDistance;
                if (!TryCalculateUnboundedBarycentric(
                        projectedPoint,
                        point0,
                        point1,
                        point2,
                        out Vector3 barycentric))
                {
                    continue;
                }

                bestPlaneDistanceSquared = planeDistanceSquared;
                bestProjectedPoint = projectedPoint;
                bestBarycentric = barycentric;
                bestPlaneNormalWorld = planeNormal;
                bestTriangle = triangle / 3;
            }

            if (bestTriangle < 0)
                return false;

            int triangleOffset = bestTriangle * 3;
            Vector2 rawUv =
                uv2[triangles[triangleOffset]] * bestBarycentric.x +
                uv2[triangles[triangleOffset + 1]] * bestBarycentric.y +
                uv2[triangles[triangleOffset + 2]] * bestBarycentric.z;
            result = new ProjectionResult
            {
                atlasUv = Vector2.Scale(
                              rawUv,
                              new Vector2(lightmapScaleOffset.x, lightmapScaleOffset.y)) +
                          new Vector2(lightmapScaleOffset.z, lightmapScaleOffset.w),
                closestPointWorld = bestProjectedPoint,
                projectionNormalWorld = bestPlaneNormalWorld,
                distance = Mathf.Sqrt(bestPlaneDistanceSquared),
                triangleIndex = bestTriangle
            };
            return true;
        }

        /// <summary>
        /// Secondary-lightmap weight: 0.5 at the doorway seam and 0 at blendDepth.
        /// Both rooms perform the same blend, which makes the seam meet at 50/50.
        /// </summary>
        public static float ComputeSecondaryWeight(float distanceIntoReceiver, float blendDepth)
        {
            if (blendDepth <= 0f)
                return 0f;

            float t = Mathf.Clamp01(1f - Mathf.Max(0f, distanceIntoReceiver) / blendDepth);
            float smooth = t * t * (3f - 2f * t);
            return 0.5f * smooth;
        }

        public static float ComputeProjectionFalloff(float projectionDistance, float fadeDistance)
        {
            if (fadeDistance <= 0f)
                return 0f;

            float t = Mathf.Clamp01(1f - Mathf.Max(0f, projectionDistance) / fadeDistance);
            return t * t * (3f - 2f * t);
        }

        public static float ComputeDoorwayLateralWeight(
            float distanceFromDoorwayCenter,
            float doorwayHalfWidth,
            float fadeDistance)
        {
            float outsideOpening = Mathf.Max(
                0f,
                Mathf.Abs(distanceFromDoorwayCenter) - Mathf.Max(0f, doorwayHalfWidth));
            if (outsideOpening <= 0f)
                return 1f;
            if (fadeDistance <= 0f)
                return 0f;

            float t = Mathf.Clamp01(1f - outsideOpening / fadeDistance);
            return t * t * (3f - 2f * t);
        }

        public static float ComputePortalSurfaceFacingWeight(
            Vector3 receiverPositionWorld,
            Vector3 receiverNormalWorld,
            Vector3 portalCenterWorld,
            Vector3 portalInteriorNormalWorld)
        {
            Vector3 toPortal = portalCenterWorld - receiverPositionWorld;
            if (toPortal.sqrMagnitude <= 0.000001f ||
                receiverNormalWorld.sqrMagnitude <= 0.000001f ||
                portalInteriorNormalWorld.sqrMagnitude <= 0.000001f)
            {
                return 0f;
            }

            Vector3 directionToPortal = toPortal.normalized;
            float receiverCosine = Mathf.Clamp01(Vector3.Dot(
                receiverNormalWorld.normalized,
                directionToPortal));
            float receiverFacing = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.02f, 0.35f, receiverCosine));
            float portalCosine = Mathf.Clamp01(Vector3.Dot(
                portalInteriorNormalWorld.normalized,
                -directionToPortal));
            return receiverFacing * Mathf.Sqrt(portalCosine);
        }

        public static float ComputePortalApertureRevealWeight(
            float lateralDistance,
            float verticalFromFloor,
            float distanceIntoReceiver,
            float portalHalfWidth,
            float portalHeight,
            Vector3 receiverNormalWorld,
            Vector3 portalInteriorNormalWorld,
            float apertureSeam = 0.04f,
            float revealDepth = 0.4f)
        {
            if (receiverNormalWorld.sqrMagnitude <= 0.000001f ||
                portalInteriorNormalWorld.sqrMagnitude <= 0.000001f)
            {
                return 0f;
            }

            float safeSeam = Mathf.Max(0.0001f, apertureSeam);
            float safeDepth = Mathf.Max(0.0001f, revealDepth);
            float horizontal = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    Mathf.Max(0f, portalHalfWidth) + safeSeam,
                    Mathf.Max(0f, portalHalfWidth) + safeSeam * 2f,
                    Mathf.Abs(lateralDistance)));
            float bottom = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(-safeSeam * 2f, -safeSeam, verticalFromFloor));
            float top = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    Mathf.Max(0.1f, portalHeight) + safeSeam,
                    Mathf.Max(0.1f, portalHeight) + safeSeam * 2f,
                    verticalFromFloor));
            float depth = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(safeDepth * 0.5f, safeDepth, distanceIntoReceiver));
            float axialAlignment = Mathf.Abs(Vector3.Dot(
                receiverNormalWorld.normalized,
                portalInteriorNormalWorld.normalized));
            float transverseFacing = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.35f, 0.75f, axialAlignment));
            float portalFacingCosine = Mathf.Clamp01(Vector3.Dot(
                receiverNormalWorld.normalized,
                -portalInteriorNormalWorld.normalized));
            float portalFacing = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.02f, 0.35f, portalFacingCosine));
            float revealSurfaceFacing = Mathf.Max(transverseFacing, portalFacing);
            return horizontal * bottom * top * depth * revealSurfaceFacing;
        }

        public static bool BoundsOverlapsPortalBand(
            Bounds worldBounds,
            Vector3 doorwayPositionWorld,
            Vector3 doorwayForwardWorld,
            float receiverInteriorSign,
            float blendDepth,
            float doorwayHalfWidth,
            float lateralFadeDistance)
        {
            Vector3 forward = doorwayForwardWorld.sqrMagnitude > 0.000001f
                ? doorwayForwardWorld.normalized
                : Vector3.forward;
            Vector3 interiorAxis = forward * (receiverInteriorSign >= 0f ? 1f : -1f);
            Vector3 rightAxis = Vector3.Cross(Vector3.up, forward).normalized;
            if (rightAxis.sqrMagnitude <= 0.000001f)
                rightAxis = Vector3.right;

            Vector3 centerOffset = worldBounds.center - doorwayPositionWorld;
            float depthCenter = Vector3.Dot(centerOffset, interiorAxis);
            float depthRadius = ProjectBoundsRadius(worldBounds.extents, interiorAxis);
            if (depthCenter + depthRadius < -0.05f ||
                depthCenter - depthRadius > Mathf.Max(0.05f, blendDepth))
            {
                return false;
            }

            float lateralCenter = Vector3.Dot(centerOffset, rightAxis);
            float lateralRadius = ProjectBoundsRadius(worldBounds.extents, rightAxis);
            float lateralLimit = Mathf.Max(0f, doorwayHalfWidth) +
                                 Mathf.Max(0f, lateralFadeDistance);
            return lateralCenter + lateralRadius >= -lateralLimit &&
                   lateralCenter - lateralRadius <= lateralLimit;
        }

        /// <summary>
        /// Resolves the four-state pair-bake texture keys without depending on
        /// runtime lighting components. State indices match P0P0, P100P0,
        /// P0P100, and P100P100 in that order.
        /// </summary>
        public static void ResolvePairStateKeys(
            bool receiverIsStartRoom,
            bool receiverPowered,
            bool sourcePowered,
            out int currentState,
            out int baselineState)
        {
            bool currentStartPowered = receiverIsStartRoom ? receiverPowered : sourcePowered;
            bool currentAdministrativePowered = receiverIsStartRoom ? sourcePowered : receiverPowered;
            bool baselineStartPowered = receiverIsStartRoom && receiverPowered;
            bool baselineAdministrativePowered = !receiverIsStartRoom && receiverPowered;
            currentState = ComposePairStateIndex(currentStartPowered, currentAdministrativePowered);
            baselineState = ComposePairStateIndex(baselineStartPowered, baselineAdministrativePowered);
        }

        public static int ComposePairStateIndex(bool startPowered, bool administrativePowered)
        {
            if (startPowered && administrativePowered)
                return 3;
            if (startPowered)
                return 1;
            return administrativePowered ? 2 : 0;
        }

        public static int BuildVertexData(
            Mesh receiverMesh,
            Matrix4x4 receiverLocalToWorld,
            Vector3 doorwayPositionWorld,
            Vector3 doorwayForwardWorld,
            float receiverInteriorSign,
            Mesh extensionMesh,
            Matrix4x4 extensionLocalToWorld,
            Vector4 extensionLightmapScaleOffset,
            float blendDepth,
            float maxProjectionDistance,
            Vector4[] outputUv4,
            float lateralFadeDistance = 1.25f,
            Vector4[] outputProjectionBasis = null,
            float portalHalfWidth = 0f,
            float portalHeight = 0f)
        {
            if (receiverMesh == null)
                throw new ArgumentNullException(nameof(receiverMesh));
            if (extensionMesh == null)
                throw new ArgumentNullException(nameof(extensionMesh));

            Vector3[] receiverVertices = receiverMesh.vertices;
            Vector3[] receiverNormals = receiverMesh.normals;
            bool hasReceiverNormals =
                receiverNormals != null && receiverNormals.Length == receiverVertices.Length;
            Matrix4x4 receiverNormalMatrix = receiverLocalToWorld.inverse.transpose;
            if (outputUv4 == null || outputUv4.Length != receiverVertices.Length)
                throw new ArgumentException("Output UV4 array must match receiver vertex count.", nameof(outputUv4));
            if (outputProjectionBasis != null && outputProjectionBasis.Length != receiverVertices.Length)
            {
                throw new ArgumentException(
                    "Projection basis array must match receiver vertex count.",
                    nameof(outputProjectionBasis));
            }

            int validProjectionCount = 0;
            bool[] compactPortalVertices = outputProjectionBasis != null &&
                                           portalHalfWidth > 0f &&
                                           portalHeight > 0f
                ? BuildCompactPortalVertexMask(
                    receiverMesh,
                    receiverLocalToWorld,
                    doorwayPositionWorld,
                    doorwayForwardWorld,
                    portalHalfWidth,
                    portalHeight)
                : null;

            for (int vertex = 0; vertex < receiverVertices.Length; vertex++)
            {
                Vector3 worldPoint = receiverLocalToWorld.MultiplyPoint3x4(receiverVertices[vertex]);
                Vector3 receiverNormalWorld = hasReceiverNormals
                    ? receiverNormalMatrix.MultiplyVector(receiverNormals[vertex]).normalized
                    : Vector3.zero;
                // Project every vertex, including the zero-weight band. Keeping valid UVs
                // on both sides of the fade prevents interpolation from pulling atlas UV 0
                // into triangles at the portal boundary.
                bool usedFallback = false;
                bool projected = TryProjectPointOnAlignedPlane(
                        extensionMesh,
                        extensionLocalToWorld,
                        worldPoint,
                        extensionLightmapScaleOffset,
                        receiverNormalWorld,
                        hasReceiverNormals ? 0.65f : 0f,
                        out ProjectionResult projection);

                // Door frames and arbitrary props commonly contain faces whose normals
                // have no matching plane in the compact portal shell. Keep the aligned
                // projection as the preferred path, then fall back to the closest bounded
                // shell triangle. This fills those faces without extrapolating UVs beyond
                // the baked shell chart or adding prop-name/material exceptions.
                if (!projected)
                {
                    usedFallback = true;
                    projected = TryProjectPoint(
                        extensionMesh,
                        extensionLocalToWorld,
                        worldPoint,
                        extensionLightmapScaleOffset,
                        maxProjectionDistance,
                        out projection);
                }

                if (!projected)
                {
                    outputUv4[vertex] = Vector4.zero;
                    if (outputProjectionBasis != null)
                        outputProjectionBasis[vertex] = Vector4.zero;
                    continue;
                }

                validProjectionCount++;

                float projectionWeight = ComputeProjectionFalloff(
                    projection.distance,
                    maxProjectionDistance);

                outputUv4[vertex] = new Vector4(
                    projection.atlasUv.x,
                    projection.atlasUv.y,
                    projectionWeight,
                    1f);
                if (outputProjectionBasis != null)
                {
                    Vector3 projectionNormal = projection.projectionNormalWorld.sqrMagnitude > 0.000001f
                        ? projection.projectionNormalWorld.normalized
                        : receiverNormalWorld;
                    outputProjectionBasis[vertex] = new Vector4(
                        projectionNormal.x,
                        projectionNormal.y,
                        projectionNormal.z,
                        (usedFallback ? 1f : 0f) +
                        (compactPortalVertices != null && compactPortalVertices[vertex] ? 2f : 0f));
                }
            }

            // The fragment shader evaluates portal depth and lateral fade per pixel.
            // A coarse floor quad can cover the doorway while all four vertices lie
            // outside the fade band, so renderer eligibility must use valid projected
            // UVs rather than non-zero vertex weights.
            return validProjectionCount;
        }

        public static bool IsCompactPortalSubmeshBounds(
            float minimumLateral,
            float maximumLateral,
            float minimumVertical,
            float maximumVertical,
            float minimumDepth,
            float maximumDepth,
            float portalHalfWidth,
            float portalHeight)
        {
            const float frameLateralAllowance = 0.25f;
            const float frameVerticalAllowance = 0.5f;
            const float frameDepthAllowance = 0.35f;
            float safeHalfWidth = Mathf.Max(0.05f, portalHalfWidth);
            float safeHeight = Mathf.Max(0.1f, portalHeight);
            return Mathf.Max(Mathf.Abs(minimumLateral), Mathf.Abs(maximumLateral)) <=
                       safeHalfWidth + frameLateralAllowance &&
                   minimumVertical >= -frameLateralAllowance &&
                   maximumVertical <= safeHeight + frameVerticalAllowance &&
                   Mathf.Max(Mathf.Abs(minimumDepth), Mathf.Abs(maximumDepth)) <=
                       frameDepthAllowance;
        }

        private static bool[] BuildCompactPortalVertexMask(
            Mesh receiverMesh,
            Matrix4x4 receiverLocalToWorld,
            Vector3 doorwayPositionWorld,
            Vector3 doorwayForwardWorld,
            float portalHalfWidth,
            float portalHeight)
        {
            var mask = new bool[receiverMesh.vertexCount];
            Vector3[] vertices = receiverMesh.vertices;
            Vector3 forward = doorwayForwardWorld.sqrMagnitude > 0.000001f
                ? doorwayForwardWorld.normalized
                : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            if (right.sqrMagnitude <= 0.000001f)
                right = Vector3.right;

            for (int subMesh = 0; subMesh < receiverMesh.subMeshCount; subMesh++)
            {
                int[] triangles = receiverMesh.GetTriangles(subMesh);
                if (triangles == null || triangles.Length == 0)
                    continue;

                float minimumLateral = float.PositiveInfinity;
                float maximumLateral = float.NegativeInfinity;
                float minimumVertical = float.PositiveInfinity;
                float maximumVertical = float.NegativeInfinity;
                float minimumDepth = float.PositiveInfinity;
                float maximumDepth = float.NegativeInfinity;
                for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex++)
                {
                    int vertexIndex = triangles[triangleIndex];
                    if ((uint)vertexIndex >= vertices.Length)
                        continue;

                    Vector3 offset = receiverLocalToWorld.MultiplyPoint3x4(vertices[vertexIndex]) -
                                     doorwayPositionWorld;
                    float lateral = Vector3.Dot(offset, right);
                    float vertical = offset.y;
                    float depth = Vector3.Dot(offset, forward);
                    minimumLateral = Mathf.Min(minimumLateral, lateral);
                    maximumLateral = Mathf.Max(maximumLateral, lateral);
                    minimumVertical = Mathf.Min(minimumVertical, vertical);
                    maximumVertical = Mathf.Max(maximumVertical, vertical);
                    minimumDepth = Mathf.Min(minimumDepth, depth);
                    maximumDepth = Mathf.Max(maximumDepth, depth);
                }

                if (!IsCompactPortalSubmeshBounds(
                        minimumLateral,
                        maximumLateral,
                        minimumVertical,
                        maximumVertical,
                        minimumDepth,
                        maximumDepth,
                        portalHalfWidth,
                        portalHeight))
                {
                    continue;
                }

                for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex++)
                {
                    int vertexIndex = triangles[triangleIndex];
                    if ((uint)vertexIndex < mask.Length)
                        mask[vertexIndex] = true;
                }
            }

            return mask;
        }

        private static float ProjectBoundsRadius(Vector3 extents, Vector3 axis)
        {
            return Mathf.Abs(axis.x) * extents.x +
                   Mathf.Abs(axis.y) * extents.y +
                   Mathf.Abs(axis.z) * extents.z;
        }

        private static bool TryCalculateUnboundedBarycentric(
            Vector3 point,
            Vector3 point0,
            Vector3 point1,
            Vector3 point2,
            out Vector3 barycentric)
        {
            Vector3 edge0 = point1 - point0;
            Vector3 edge1 = point2 - point0;
            Vector3 offset = point - point0;
            float dot00 = Vector3.Dot(edge0, edge0);
            float dot01 = Vector3.Dot(edge0, edge1);
            float dot11 = Vector3.Dot(edge1, edge1);
            float dot20 = Vector3.Dot(offset, edge0);
            float dot21 = Vector3.Dot(offset, edge1);
            float denominator = dot00 * dot11 - dot01 * dot01;
            if (Mathf.Abs(denominator) <= 0.000001f)
            {
                barycentric = default;
                return false;
            }

            float inverse = 1f / denominator;
            float v = (dot11 * dot20 - dot01 * dot21) * inverse;
            float w = (dot00 * dot21 - dot01 * dot20) * inverse;
            barycentric = new Vector3(1f - v - w, v, w);
            return true;
        }

        private static void ClosestPointOnTriangle(
            Vector3 point,
            Vector3 point0,
            Vector3 point1,
            Vector3 point2,
            out Vector3 closestPoint,
            out Vector3 barycentric)
        {
            Vector3 edge01 = point1 - point0;
            Vector3 edge02 = point2 - point0;
            Vector3 pointFrom0 = point - point0;
            float d1 = Vector3.Dot(edge01, pointFrom0);
            float d2 = Vector3.Dot(edge02, pointFrom0);
            if (d1 <= 0f && d2 <= 0f)
            {
                closestPoint = point0;
                barycentric = new Vector3(1f, 0f, 0f);
                return;
            }

            Vector3 pointFrom1 = point - point1;
            float d3 = Vector3.Dot(edge01, pointFrom1);
            float d4 = Vector3.Dot(edge02, pointFrom1);
            if (d3 >= 0f && d4 <= d3)
            {
                closestPoint = point1;
                barycentric = new Vector3(0f, 1f, 0f);
                return;
            }

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                closestPoint = point0 + v * edge01;
                barycentric = new Vector3(1f - v, v, 0f);
                return;
            }

            Vector3 pointFrom2 = point - point2;
            float d5 = Vector3.Dot(edge01, pointFrom2);
            float d6 = Vector3.Dot(edge02, pointFrom2);
            if (d6 >= 0f && d5 <= d6)
            {
                closestPoint = point2;
                barycentric = new Vector3(0f, 0f, 1f);
                return;
            }

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                closestPoint = point0 + w * edge02;
                barycentric = new Vector3(1f - w, 0f, w);
                return;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                closestPoint = point1 + w * (point2 - point1);
                barycentric = new Vector3(0f, 1f - w, w);
                return;
            }

            float denominator = 1f / (va + vb + vc);
            float faceV = vb * denominator;
            float faceW = vc * denominator;
            float faceU = 1f - faceV - faceW;
            closestPoint = point0 * faceU + point1 * faceV + point2 * faceW;
            barycentric = new Vector3(faceU, faceV, faceW);
        }
    }
}
