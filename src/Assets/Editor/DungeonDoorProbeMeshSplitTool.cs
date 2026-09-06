using System;
using System.Collections.Generic;
using System.IO;
using DunGen;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class DungeonDoorProbeMeshSplitTool
{
    private const string MeshFolder = "Assets/Prefabs/map_piece/DoorProbeMeshes";
    private const string SplitRootName = "DungeonDoorProbeSplitRoot";
    private const float SideNormalThreshold = 0.35f;

    private static readonly string[] TargetPrefabPaths =
    {
        "Assets/Prefabs/map_piece/NewPrison/SomePicees/Door_SM_A_Door_Placement.prefab",
        "Assets/Prefabs/map_piece/NewPrison/SomePicees/Door_SM_B_DoorPlacement.prefab",
        "Assets/Prefabs/map_piece/NewPrison/SomePicees/Door_LG_A_DoorPlacement.prefab",
        "Assets/Prefabs/map_piece/NewPrison/SomePicees/Door_LG_B_DoorPlacement.prefab",
        "Assets/Prefabs/map_piece/Prison/Parts/Door.prefab"
    };

    [MenuItem("Tools/Dungeon/Setup Door Dual Side Probe Meshes")]
    public static void SetupAll()
    {
        Debug.Log(SetupAllForCli());
    }

    public static string SetupAllForCli()
    {
        EnsureFolder(MeshFolder);

        var report = new System.Text.StringBuilder();
        int prefabCount = 0;
        int splitRendererCount = 0;
        int generatedMeshCount = 0;

        for (int i = 0; i < TargetPrefabPaths.Length; i++)
        {
            string prefabPath = TargetPrefabPaths[i];
            if (!File.Exists(prefabPath))
            {
                report.AppendLine($"missing prefab: {prefabPath}");
                continue;
            }

            var result = ProcessPrefab(prefabPath);
            prefabCount++;
            splitRendererCount += result.splitRenderers;
            generatedMeshCount += result.generatedMeshes;
            report.AppendLine($"{prefabPath}: splitRenderers={result.splitRenderers} meshes={result.generatedMeshes} dualReceivers={result.dualReceivers} removedGenericReceivers={result.removedGenericReceivers} skipped={result.skippedRenderers}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        report.Insert(0, $"Door dual-side probe setup complete prefabs={prefabCount} splitRenderers={splitRendererCount} generatedMeshes={generatedMeshCount}\n");
        return report.ToString();
    }

    [MenuItem("Tools/Dungeon/Report Door Dual Side Probe Meshes")]
    public static void ReportAll()
    {
        Debug.Log(ReportAllForCli());
    }

    public static string ReportAllForCli()
    {
        var report = new System.Text.StringBuilder();
        for (int i = 0; i < TargetPrefabPaths.Length; i++)
            report.AppendLine(ReportPrefab(TargetPrefabPaths[i]));

        return report.ToString();
    }

    private static ProcessResult ProcessPrefab(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var result = new ProcessResult();

            RemoveExistingSplitRoots(root);
            ConfigureDoorReceivers(root, result);

            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer == null || renderer.GetComponentInParent<DungeonDoorProbeRendererGroup>(true) != null)
                    continue;

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh sourceMesh = filter != null ? filter.sharedMesh : null;
                if (sourceMesh == null || sourceMesh.vertexCount == 0 || sourceMesh.subMeshCount == 0)
                {
                    result.skippedRenderers++;
                    continue;
                }

                int generated = SplitRenderer(prefabPath, root.transform, renderer, sourceMesh);
                if (generated <= 0)
                {
                    result.skippedRenderers++;
                    continue;
                }

                renderer.enabled = false;
                result.splitRenderers++;
                result.generatedMeshes += generated;
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return result;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureDoorReceivers(GameObject root, ProcessResult result)
    {
        var doors = root.GetComponentsInChildren<DunGen.Door>(true);
        for (int i = 0; i < doors.Length; i++)
        {
            DunGen.Door door = doors[i];
            if (door == null)
                continue;

            var genericReceiver = door.GetComponent<DungeonDynamicProbeReceiver>();
            if (genericReceiver != null)
            {
                UnityEngine.Object.DestroyImmediate(genericReceiver, true);
                result.removedGenericReceivers++;
            }

            if (door.GetComponent<DungeonDoorDualSideProbeReceiver>() == null)
            {
                door.gameObject.AddComponent<DungeonDoorDualSideProbeReceiver>();
                result.dualReceivers++;
            }
        }
    }

    private static int SplitRenderer(string prefabPath, Transform root, MeshRenderer sourceRenderer, Mesh sourceMesh)
    {
        var splitRoot = new GameObject(SplitRootName);
        splitRoot.transform.SetParent(sourceRenderer.transform, false);
        splitRoot.transform.localPosition = Vector3.zero;
        splitRoot.transform.localRotation = Quaternion.identity;
        splitRoot.transform.localScale = Vector3.one;

        int generated = 0;
        generated += CreateGroupObject(prefabPath, root, sourceRenderer, sourceMesh, splitRoot.transform, DungeonDoorProbeRendererGroup.Group.PositiveZ);
        generated += CreateGroupObject(prefabPath, root, sourceRenderer, sourceMesh, splitRoot.transform, DungeonDoorProbeRendererGroup.Group.NegativeZ);
        generated += CreateGroupObject(prefabPath, root, sourceRenderer, sourceMesh, splitRoot.transform, DungeonDoorProbeRendererGroup.Group.Edge);

        if (generated == 0)
            UnityEngine.Object.DestroyImmediate(splitRoot);

        return generated;
    }

    private static int CreateGroupObject(
        string prefabPath,
        Transform root,
        MeshRenderer sourceRenderer,
        Mesh sourceMesh,
        Transform splitRoot,
        DungeonDoorProbeRendererGroup.Group group)
    {
        Mesh mesh = BuildGroupMesh(sourceMesh, group);
        if (mesh == null)
            return 0;

        string meshPath = BuildMeshAssetPath(prefabPath, root, sourceRenderer.transform, group);
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);

        var go = new GameObject("DungeonDoorProbe_" + group);
        go.transform.SetParent(splitRoot, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        CopyRendererSettings(sourceRenderer, renderer);

        var marker = go.AddComponent<DungeonDoorProbeRendererGroup>();
        marker.Configure(group);
        return 1;
    }

    private static Mesh BuildGroupMesh(Mesh sourceMesh, DungeonDoorProbeRendererGroup.Group group)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var tangents = new List<Vector4>();
        var uv0 = new List<Vector2>();
        var uv1 = new List<Vector2>();
        var colors = new List<Color>();
        var trianglesBySubmesh = new List<int>[sourceMesh.subMeshCount];
        var remap = new Dictionary<int, int>();

        Vector3[] sourceVertices = sourceMesh.vertices;
        Vector3[] sourceNormals = sourceMesh.normals;
        Vector4[] sourceTangents = sourceMesh.tangents;
        Vector2[] sourceUv0 = sourceMesh.uv;
        Vector2[] sourceUv1 = sourceMesh.uv2;
        Color[] sourceColors = sourceMesh.colors;

        for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
        {
            trianglesBySubmesh[submesh] = new List<int>();
            int[] sourceTriangles = sourceMesh.GetTriangles(submesh);
            for (int i = 0; i + 2 < sourceTriangles.Length; i += 3)
            {
                int a = sourceTriangles[i];
                int b = sourceTriangles[i + 1];
                int c = sourceTriangles[i + 2];
                Vector3 faceNormal = Vector3.Cross(sourceVertices[b] - sourceVertices[a], sourceVertices[c] - sourceVertices[a]).normalized;

                if (!BelongsToGroup(faceNormal, group))
                    continue;

                trianglesBySubmesh[submesh].Add(RemapVertex(a));
                trianglesBySubmesh[submesh].Add(RemapVertex(b));
                trianglesBySubmesh[submesh].Add(RemapVertex(c));
            }
        }

        if (vertices.Count == 0)
            return null;

        var mesh = new Mesh
        {
            name = sourceMesh.name + "_" + group,
            indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16
        };

        mesh.SetVertices(vertices);
        if (normals.Count == vertices.Count)
            mesh.SetNormals(normals);
        if (tangents.Count == vertices.Count)
            mesh.SetTangents(tangents);
        if (uv0.Count == vertices.Count)
            mesh.SetUVs(0, uv0);
        if (uv1.Count == vertices.Count)
            mesh.SetUVs(1, uv1);
        if (colors.Count == vertices.Count)
            mesh.SetColors(colors);

        mesh.subMeshCount = sourceMesh.subMeshCount;
        for (int submesh = 0; submesh < trianglesBySubmesh.Length; submesh++)
            mesh.SetTriangles(trianglesBySubmesh[submesh], submesh, true);

        if (normals.Count != vertices.Count)
            mesh.RecalculateNormals();

        mesh.RecalculateBounds();
        return mesh;

        int RemapVertex(int sourceIndex)
        {
            if (remap.TryGetValue(sourceIndex, out int mappedIndex))
                return mappedIndex;

            mappedIndex = vertices.Count;
            remap.Add(sourceIndex, mappedIndex);
            vertices.Add(sourceVertices[sourceIndex]);

            if (sourceNormals != null && sourceNormals.Length == sourceVertices.Length)
                normals.Add(sourceNormals[sourceIndex]);
            if (sourceTangents != null && sourceTangents.Length == sourceVertices.Length)
                tangents.Add(sourceTangents[sourceIndex]);
            if (sourceUv0 != null && sourceUv0.Length == sourceVertices.Length)
                uv0.Add(sourceUv0[sourceIndex]);
            if (sourceUv1 != null && sourceUv1.Length == sourceVertices.Length)
                uv1.Add(sourceUv1[sourceIndex]);
            if (sourceColors != null && sourceColors.Length == sourceVertices.Length)
                colors.Add(sourceColors[sourceIndex]);

            return mappedIndex;
        }
    }

    private static bool BelongsToGroup(Vector3 faceNormal, DungeonDoorProbeRendererGroup.Group group)
    {
        switch (group)
        {
            case DungeonDoorProbeRendererGroup.Group.PositiveZ:
                return faceNormal.z >= SideNormalThreshold;
            case DungeonDoorProbeRendererGroup.Group.NegativeZ:
                return faceNormal.z <= -SideNormalThreshold;
            default:
                return faceNormal.z > -SideNormalThreshold && faceNormal.z < SideNormalThreshold;
        }
    }

    private static void CopyRendererSettings(MeshRenderer source, MeshRenderer destination)
    {
        destination.sharedMaterials = source.sharedMaterials;
        destination.shadowCastingMode = source.shadowCastingMode;
        destination.receiveShadows = source.receiveShadows;
        destination.lightProbeUsage = source.lightProbeUsage;
        destination.reflectionProbeUsage = source.reflectionProbeUsage;
        destination.probeAnchor = source.probeAnchor;
        destination.lightProbeProxyVolumeOverride = source.lightProbeProxyVolumeOverride;
        destination.renderingLayerMask = source.renderingLayerMask;
        destination.rendererPriority = source.rendererPriority;
        destination.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        destination.motionVectorGenerationMode = source.motionVectorGenerationMode;
    }

    private static void RemoveExistingSplitRoots(GameObject root)
    {
        var transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = transforms.Length - 1; i >= 0; i--)
        {
            Transform transform = transforms[i];
            if (transform != null && transform.name == SplitRootName)
                UnityEngine.Object.DestroyImmediate(transform.gameObject);
        }
    }

    private static string ReportPrefab(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            return $"{prefabPath}: missing";

        int dualReceivers = prefab.GetComponentsInChildren<DungeonDoorDualSideProbeReceiver>(true).Length;
        int genericReceivers = prefab.GetComponentsInChildren<DungeonDynamicProbeReceiver>(true).Length;
        int markers = prefab.GetComponentsInChildren<DungeonDoorProbeRendererGroup>(true).Length;
        int splitRoots = CountNamedTransforms(prefab.transform, SplitRootName);
        int disabledSourceRenderers = 0;
        int splitRenderers = 0;
        var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (renderer.GetComponent<DungeonDoorProbeRendererGroup>() != null)
            {
                splitRenderers++;
                continue;
            }

            if (!renderer.enabled && HasChildNamed(renderer.transform, SplitRootName))
                disabledSourceRenderers++;
        }

        return $"{prefabPath}: dual={dualReceivers} generic={genericReceivers} markers={markers} splitRoots={splitRoots} splitRenderers={splitRenderers} disabledSources={disabledSourceRenderers}";
    }

    private static int CountNamedTransforms(Transform root, string name)
    {
        int count = 0;
        var transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null && transforms[i].name == name)
                count++;
        }

        return count;
    }

    private static bool HasChildNamed(Transform root, string name)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == name)
                return true;
        }

        return false;
    }

    private static string BuildMeshAssetPath(
        string prefabPath,
        Transform prefabRoot,
        Transform rendererTransform,
        DungeonDoorProbeRendererGroup.Group group)
    {
        string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
        string rendererPath = BuildRelativePath(prefabRoot, rendererTransform);
        string fileName = SanitizeFileName($"{prefabName}_{rendererPath}_{group}.asset");
        return $"{MeshFolder}/{fileName}";
    }

    private static string BuildRelativePath(Transform root, Transform transform)
    {
        var names = new Stack<string>();
        Transform current = transform;
        while (current != null && current != root)
        {
            names.Push(current.name);
            current = current.parent;
        }

        if (names.Count == 0)
            return root.name;

        return string.Join("_", names.ToArray());
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
            value = value.Replace(invalid[i], '_');

        return value.Replace(' ', '_');
    }

    private static void EnsureFolder(string folder)
    {
        string[] parts = folder.Split('/');
        if (parts.Length == 0)
            return;

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }

    private struct ProcessResult
    {
        public int splitRenderers;
        public int generatedMeshes;
        public int dualReceivers;
        public int removedGenericReceivers;
        public int skippedRenderers;
    }
}
