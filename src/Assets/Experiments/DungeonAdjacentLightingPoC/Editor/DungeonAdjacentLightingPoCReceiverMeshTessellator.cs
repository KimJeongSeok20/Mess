using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonAdjacentLightingPoC.Editor
{
    internal static class DungeonAdjacentLightingPoCReceiverMeshTessellator
    {
        public static Mesh Tessellate(Mesh source, int subdivisions, string meshName)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            subdivisions = Mathf.Clamp(subdivisions, 1, 32);

            Vector3[] sourcePositions = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector4[] sourceTangents = source.tangents;
            Color[] sourceColors = source.colors;
            bool hasNormals = sourceNormals.Length == sourcePositions.Length;
            bool hasTangents = sourceTangents.Length == sourcePositions.Length;
            bool hasColors = sourceColors.Length == sourcePositions.Length;

            var sourceUvs = new List<Vector4>[8];
            var resultUvs = new List<Vector4>[8];
            for (int channel = 0; channel < sourceUvs.Length; channel++)
            {
                sourceUvs[channel] = new List<Vector4>();
                source.GetUVs(channel, sourceUvs[channel]);
                if (sourceUvs[channel].Count == sourcePositions.Length)
                    resultUvs[channel] = new List<Vector4>();
                else
                    sourceUvs[channel] = null;
            }

            var positions = new List<Vector3>();
            var normals = hasNormals ? new List<Vector3>() : null;
            var tangents = hasTangents ? new List<Vector4>() : null;
            var colors = hasColors ? new List<Color>() : null;
            var submeshTriangles = new List<int>[source.subMeshCount];

            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                if (source.GetTopology(submesh) != MeshTopology.Triangles)
                    throw new InvalidOperationException(
                        $"Receiver mesh '{source.name}' submesh {submesh} is not triangles.");

                submeshTriangles[submesh] = new List<int>();
                int[] triangles = source.GetTriangles(submesh);
                for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                {
                    int index0 = triangles[triangle];
                    int index1 = triangles[triangle + 1];
                    int index2 = triangles[triangle + 2];
                    var rows = new int[subdivisions + 1][];

                    for (int i = 0; i <= subdivisions; i++)
                    {
                        rows[i] = new int[subdivisions - i + 1];
                        for (int j = 0; j <= subdivisions - i; j++)
                        {
                            float weight1 = i / (float)subdivisions;
                            float weight2 = j / (float)subdivisions;
                            float weight0 = 1f - weight1 - weight2;
                            rows[i][j] = positions.Count;
                            positions.Add(
                                sourcePositions[index0] * weight0 +
                                sourcePositions[index1] * weight1 +
                                sourcePositions[index2] * weight2);

                            if (hasNormals)
                            {
                                normals.Add((
                                    sourceNormals[index0] * weight0 +
                                    sourceNormals[index1] * weight1 +
                                    sourceNormals[index2] * weight2).normalized);
                            }
                            if (hasTangents)
                            {
                                Vector4 tangent =
                                    sourceTangents[index0] * weight0 +
                                    sourceTangents[index1] * weight1 +
                                    sourceTangents[index2] * weight2;
                                Vector3 direction = new Vector3(tangent.x, tangent.y, tangent.z).normalized;
                                tangents.Add(new Vector4(
                                    direction.x,
                                    direction.y,
                                    direction.z,
                                    tangent.w >= 0f ? 1f : -1f));
                            }
                            if (hasColors)
                            {
                                colors.Add(
                                    sourceColors[index0] * weight0 +
                                    sourceColors[index1] * weight1 +
                                    sourceColors[index2] * weight2);
                            }
                            for (int channel = 0; channel < sourceUvs.Length; channel++)
                            {
                                if (sourceUvs[channel] == null)
                                    continue;
                                resultUvs[channel].Add(
                                    sourceUvs[channel][index0] * weight0 +
                                    sourceUvs[channel][index1] * weight1 +
                                    sourceUvs[channel][index2] * weight2);
                            }
                        }
                    }

                    for (int i = 0; i < subdivisions; i++)
                    {
                        for (int j = 0; j < subdivisions - i; j++)
                        {
                            submeshTriangles[submesh].Add(rows[i][j]);
                            submeshTriangles[submesh].Add(rows[i + 1][j]);
                            submeshTriangles[submesh].Add(rows[i][j + 1]);

                            if (j >= subdivisions - i - 1)
                                continue;
                            submeshTriangles[submesh].Add(rows[i + 1][j]);
                            submeshTriangles[submesh].Add(rows[i + 1][j + 1]);
                            submeshTriangles[submesh].Add(rows[i][j + 1]);
                        }
                    }
                }
            }

            var result = new Mesh
            {
                name = meshName,
                indexFormat = positions.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            result.SetVertices(positions);
            if (hasNormals)
                result.SetNormals(normals);
            if (hasTangents)
                result.SetTangents(tangents);
            if (hasColors)
                result.SetColors(colors);
            for (int channel = 0; channel < resultUvs.Length; channel++)
            {
                if (resultUvs[channel] != null)
                    result.SetUVs(channel, resultUvs[channel]);
            }

            result.subMeshCount = submeshTriangles.Length;
            for (int submesh = 0; submesh < submeshTriangles.Length; submesh++)
                result.SetTriangles(submeshTriangles[submesh], submesh, false);
            if (!hasNormals)
                result.RecalculateNormals();
            if (!hasTangents && resultUvs[0] != null)
                result.RecalculateTangents();
            result.RecalculateBounds();
            return result;
        }
    }
}
