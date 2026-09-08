using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonSHSamplingTest
{
    public static class SHSamplingTestSampler
    {
        public struct Probe
        {
            public Vector3 Position;
            public SphericalHarmonicsL2 SH;
            public Vector4 Occlusion;
            public int Id;
        }

        public struct Result
        {
            public bool Success;
            public SphericalHarmonicsL2 SH;
            public Vector4 Occlusion;
            public int[] ProbeIds;
            public int CandidateCount, RejectedCount, RayCount;
            public bool UsedFallback;
            public float Luminance;
        }

        private struct Candidate
        {
            public int Index;
            public int Id;
            public float SqrDistance;
        }

        // The caller supplies only wall, partition and door blockers, never the receiver.
        // CandidateCount counts inspected candidates; RayCount counts Collider.Raycast calls.
        public static Result Sample(
            IReadOnlyList<Probe> probes,
            Vector3 position,
            bool checkVisibility,
            IReadOnlyList<Collider> blockers,
            int maxCandidates = 24,
            int blendCount = 4)
        {
            var result = new Result { Occlusion = Vector4.one, ProbeIds = Array.Empty<int>() };
            if (probes == null || probes.Count == 0 || blendCount <= 0 ||
                (checkVisibility && maxCandidates <= 0))
                return result;

            int candidateLimit = Math.Min(probes.Count, checkVisibility ? maxCandidates : blendCount);
            var candidates = new Candidate[candidateLimit];
            int candidateCount = 0;
            for (int i = 0; i < probes.Count; i++)
            {
                float distance = (probes[i].Position - position).sqrMagnitude;
                if (float.IsNaN(distance) || float.IsInfinity(distance))
                    continue;

                var candidate = new Candidate { Index = i, Id = probes[i].Id, SqrDistance = distance };
                int insertion = candidateCount;
                for (int j = 0; j < candidateCount; j++)
                {
                    if (ComesBefore(candidate, candidates[j]))
                    {
                        insertion = j;
                        break;
                    }
                }

                if (insertion >= candidateLimit)
                    continue;
                int last = Math.Min(candidateCount, candidateLimit - 1);
                for (int j = last; j > insertion; j--)
                    candidates[j] = candidates[j - 1];
                candidates[insertion] = candidate;
                candidateCount = Math.Min(candidateLimit, candidateCount + 1);
            }

            var acceptedIds = new int[Math.Min(blendCount, candidateCount)];
            int acceptedCount = 0;
            float totalWeight = 0f;
            Vector4 blendedOcclusion = Vector4.zero;
            for (int i = 0; i < candidateCount && acceptedCount < acceptedIds.Length; i++)
            {
                Candidate candidate = candidates[i];
                Probe sample = probes[candidate.Index];
                result.CandidateCount++;
                if (checkVisibility)
                {
                    bool visible = IsVisible(position, sample.Position, blockers, out int rays);
                    result.RayCount += rays;
                    if (!visible)
                    {
                        result.RejectedCount++;
                        continue;
                    }
                }

                float distance = Mathf.Sqrt(Mathf.Max(0f, candidate.SqrDistance));
                float weight = 1f / Mathf.Max(0.05f, distance + 0.05f);
                for (int channel = 0; channel < 3; channel++)
                    for (int coefficient = 0; coefficient < 9; coefficient++)
                        result.SH[channel, coefficient] += sample.SH[channel, coefficient] * weight;
                blendedOcclusion += sample.Occlusion * weight;
                totalWeight += weight;
                acceptedIds[acceptedCount++] = sample.Id;
            }

            // A failed visibility query stays a failure; no blocked or other-room fallback.
            if (acceptedCount == 0 || totalWeight <= 0f)
                return result;

            float inverseWeight = 1f / totalWeight;
            for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    result.SH[channel, coefficient] *= inverseWeight;
            result.Occlusion = blendedOcclusion * inverseWeight;
            if (acceptedCount != acceptedIds.Length)
                Array.Resize(ref acceptedIds, acceptedCount);
            result.ProbeIds = acceptedIds;
            result.Luminance = Mathf.Max(0f,
                result.SH[0, 0] * 0.2126f + result.SH[1, 0] * 0.7152f + result.SH[2, 0] * 0.0722f);
            result.Success = true;
            return result;
        }

        // Test the full segment: an endpoint inset can skip a nearby thin wall entirely.
        // ClosestPoint handles a ray origin inside a solid blocker, which Collider.Raycast
        // alone does not reliably report as a hit. Surface contact is conservatively blocked.
        public static bool IsVisible(
            Vector3 from, Vector3 to, IReadOnlyList<Collider> blockers, out int rayCount)
        {
            rayCount = 0;
            if (blockers == null || blockers.Count == 0)
                return true;

            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (float.IsNaN(distance) || float.IsInfinity(distance))
                return false;
            Vector3 direction = distance > 0.000001f ? delta / distance : Vector3.zero;
            var ray = new Ray(from, direction);

            for (int i = 0; i < blockers.Count; i++)
            {
                Collider blocker = blockers[i];
                if (blocker == null || !blocker.enabled || blocker.isTrigger ||
                    !blocker.gameObject.activeInHierarchy)
                    continue;
                Bounds bounds = blocker.bounds;
                if (!bounds.Contains(from) &&
                    (!bounds.IntersectRay(ray, out float entryDistance) || entryDistance > distance))
                    continue;
                // Non-convex mesh interiors are not reliably defined by ClosestPoint.
                // Keep the solid-interior check for primitives and convex meshes.
                if (!(blocker is MeshCollider mesh && !mesh.convex) &&
                    (blocker.ClosestPoint(from) - from).sqrMagnitude <= 0.00000001f)
                    return false;
                if (distance <= 0.000001f)
                    continue;
                rayCount++;
                if (blocker.Raycast(ray, out _, distance))
                    return false;
                // Source walls can be single-sided meshes. A reverse ray catches the
                // opposite winding without changing global Physics.queriesHitBackfaces.
                rayCount++;
                if (blocker.Raycast(new Ray(to, -direction), out _, distance))
                    return false;
            }
            return true;
        }

        private static bool ComesBefore(Candidate first, Candidate second)
        {
            if (first.SqrDistance != second.SqrDistance)
                return first.SqrDistance < second.SqrDistance;
            if (first.Id != second.Id)
                return first.Id < second.Id;
            return first.Index < second.Index;
        }
    }
}
