using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class NewPrisonReceptionWindowBakeSafeSetup
{
    private const string SourcePrefabPath =
        "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Prefabs/Modules/Wall_Interior_Reception.prefab";
    private const string OutputFolder = "Assets/Prefabs/map_piece/NewPrison/Added";
    private const string BakeSafePrefabPath =
        OutputFolder + "/Wall_Interior_Reception_BakeSafe.prefab";
    private const string WallStaticMeshPath =
        OutputFolder + "/Wall_Interior_Reception_BakeSafe_WallStatic.asset";
    private const string ReceptionFrameStaticMeshPath =
        OutputFolder + "/Wall_Interior_Reception_BakeSafe_ReceptionFrameStatic.asset";
    private const string ReceptionWindowVisualMeshPath =
        OutputFolder + "/Wall_Interior_Reception_BakeSafe_ReceptionWindowVisual.asset";
    private const string TileModifiedFolder = "Assets/Prefabs/map_piece/NewPrison/Tile_modified";
    private const string TilesFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles";
    private const string SourceName = "Wall_Interior_Reception";
    private const string WallStaticName = "WallStatic";
    private const string ReceptionFrameStaticName = "ReceptionFrameStatic";
    private const string ReceptionWindowVisualName = "ReceptionWindowVisual";
    private const string SamplePointName = "SH_SamplePoint";

    private static readonly int[] WallSubMeshes = { 0, 1 };
    private static readonly int[] GlassVisualSubMeshes = { 2 };
    private static readonly int[] FrameStaticSubMeshes = { 3 };

    [MenuItem("Tools/Dungeon/Setup NewPrison Reception Window Bake Safe")]
    public static void SetupReceptionWindowBakeSafeMenu()
    {
        Debug.Log(NormalizeTileModifiedAndTilesReceptionWindows());
    }

    [MenuItem("Tools/Dungeon/Report NewPrison Reception Window Bake Safe")]
    public static void ReportReceptionWindowBakeSafeMenu()
    {
        Debug.Log(ReportTileModifiedAndTilesReceptionWindows());
    }

    public static string NormalizeTileModifiedAndTilesReceptionWindows()
    {
        EnsureFolder(OutputFolder);
        GameObject bakeSafePrefab = CreateOrUpdateBakeSafePrefab();
        if (bakeSafePrefab == null)
            throw new InvalidOperationException($"Failed to create bake-safe prefab at {BakeSafePrefabPath}");

        var report = new StringBuilder();
        int totalPrefabs = 0;
        int changedPrefabs = 0;
        int replacedInstances = 0;

        foreach (string folder in new[] { TileModifiedFolder, TilesFolder })
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            foreach (string guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                totalPrefabs++;

                int changed = ReplaceReceptionWindowsInPrefab(prefabPath, bakeSafePrefab);
                changed += UpgradeBakeSafeReceptionWindowsInPrefab(prefabPath);
                if (changed <= 0)
                    continue;

                changedPrefabs++;
                replacedInstances += changed;
                report.AppendLine($"  {prefabPath}: changedReceptionWindows={changed}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        report.Insert(
            0,
            $"[NewPrisonReceptionWindowBakeSafeSetup] prefabs={totalPrefabs}, changed={changedPrefabs}, replaced={replacedInstances}, prefab={BakeSafePrefabPath}\n");
        return report.ToString().TrimEnd();
    }

    public static string ReportTileModifiedAndTilesReceptionWindows()
    {
        var report = new StringBuilder();
        int originalInstances = 0;
        int bakeSafeInstances = 0;
        int visualRenderers = 0;
        int visualContributeGi = 0;
        int visualReceiveShadows = 0;
        int visualCastShadows = 0;
        int visualMissingReceiver = 0;
        int visualMissingSamplePoint = 0;
        int frameRenderers = 0;
        int frameMissingContributeGi = 0;

        foreach (string folder in new[] { TileModifiedFolder, TilesFolder })
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            foreach (string guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    int prefabOriginals = 0;
                    int prefabBakeSafe = 0;

                    foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (IsOriginalReceptionSourceInstance(transform.gameObject))
                            prefabOriginals++;

                        if (IsBakeSafeReceptionInstance(transform.gameObject))
                            prefabBakeSafe++;
                    }

                    originalInstances += prefabOriginals;
                    bakeSafeInstances += prefabBakeSafe;
                    if (prefabOriginals > 0 || prefabBakeSafe > 0)
                    {
                        report.AppendLine(
                            $"  {prefabPath}: original={prefabOriginals}, bakeSafe={prefabBakeSafe}");
                    }

                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (IsReceptionFrameStaticRenderer(renderer))
                        {
                            frameRenderers++;
                            var frameFlags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                            if ((frameFlags & StaticEditorFlags.ContributeGI) == 0)
                                frameMissingContributeGi++;
                            continue;
                        }

                        if (!IsReceptionWindowVisualRenderer(renderer))
                            continue;

                        visualRenderers++;
                        var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                        if ((flags & StaticEditorFlags.ContributeGI) != 0)
                            visualContributeGi++;
                        if (renderer.receiveShadows)
                            visualReceiveShadows++;
                        if (renderer.shadowCastingMode != ShadowCastingMode.Off)
                            visualCastShadows++;
                        if (renderer.GetComponentInParent<DungeonDynamicProbeReceiver>(true) == null)
                            visualMissingReceiver++;
                        var receiver = renderer.GetComponent<DungeonDynamicProbeReceiver>();
                        if (receiver == null || renderer.transform.Find(SamplePointName) == null)
                            visualMissingSamplePoint++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        report.Insert(
            0,
            "[NewPrisonReceptionWindowBakeSafeSetup] Verification report\n");
        report.AppendLine(
            $"summary: originalInstances={originalInstances}, bakeSafeInstances={bakeSafeInstances}, frameRenderers={frameRenderers}, frameMissingContributeGi={frameMissingContributeGi}, visualRenderers={visualRenderers}, visualContributeGi={visualContributeGi}, visualReceiveShadows={visualReceiveShadows}, visualCastShadows={visualCastShadows}, visualMissingReceiver={visualMissingReceiver}, visualMissingSamplePoint={visualMissingSamplePoint}");
        return report.ToString().TrimEnd();
    }

    public static bool IsReceptionWindowVisualRenderer(Renderer renderer)
    {
        return renderer != null && IsReceptionWindowVisualTransform(renderer.transform);
    }

    public static bool IsReceptionWindowVisualTransform(Transform transform)
    {
        while (transform != null)
        {
            if (string.Equals(transform.name, ReceptionWindowVisualName, StringComparison.OrdinalIgnoreCase))
                return true;

            transform = transform.parent;
        }

        return false;
    }

    public static bool IsReceptionFrameStaticRenderer(Renderer renderer)
    {
        return renderer != null &&
            string.Equals(renderer.transform.name, ReceptionFrameStaticName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReceptionWindowVisualRelativePath(string relativePath)
    {
        return !string.IsNullOrEmpty(relativePath) &&
            relativePath.IndexOf(ReceptionWindowVisualName, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static GameObject CreateOrUpdateBakeSafePrefab()
    {
        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
        if (sourcePrefab == null)
            throw new InvalidOperationException($"Missing source prefab: {SourcePrefabPath}");

        var sourceFilter = sourcePrefab.GetComponentInChildren<MeshFilter>(true);
        var sourceRenderer = sourcePrefab.GetComponentInChildren<MeshRenderer>(true);
        if (sourceFilter == null || sourceFilter.sharedMesh == null || sourceRenderer == null)
            throw new InvalidOperationException($"Source prefab is missing a mesh renderer: {SourcePrefabPath}");

        Mesh sourceMesh = sourceFilter.sharedMesh;
        Material[] sourceMaterials = sourceRenderer.sharedMaterials;
        if (sourceMesh.subMeshCount < 4 || sourceMaterials.Length < 4)
        {
            throw new InvalidOperationException(
                $"Expected {SourceName} to have at least four submeshes/materials, got submeshes={sourceMesh.subMeshCount}, materials={sourceMaterials.Length}");
        }

        Mesh wallMesh = CreateSplitMesh(sourceMesh, WallSubMeshes, "Wall_Interior_Reception_WallStatic");
        Mesh frameMesh = CreateSplitMesh(sourceMesh, FrameStaticSubMeshes, "Wall_Interior_Reception_ReceptionFrameStatic");
        Mesh visualMesh = CreateSplitMesh(sourceMesh, GlassVisualSubMeshes, "Wall_Interior_Reception_ReceptionWindowVisual");
        wallMesh = CreateOrUpdateMeshAsset(wallMesh, WallStaticMeshPath);
        frameMesh = CreateOrUpdateMeshAsset(frameMesh, ReceptionFrameStaticMeshPath);
        visualMesh = CreateOrUpdateMeshAsset(visualMesh, ReceptionWindowVisualMeshPath);

        var root = new GameObject("Wall_Interior_Reception_BakeSafe");
        try
        {
            var wall = new GameObject(WallStaticName);
            wall.transform.SetParent(root.transform, false);
            wall.layer = sourcePrefab.layer;
            wall.AddComponent<MeshFilter>().sharedMesh = wallMesh;
            var wallRenderer = wall.AddComponent<MeshRenderer>();
            wallRenderer.sharedMaterials = SelectMaterials(sourceMaterials, WallSubMeshes);
            wallRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            wallRenderer.receiveShadows = sourceRenderer.receiveShadows;
            wallRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
            wallRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
            wallRenderer.scaleInLightmap = sourceRenderer.scaleInLightmap;
            wallRenderer.stitchLightmapSeams = sourceRenderer.stitchLightmapSeams;

            var frame = new GameObject(ReceptionFrameStaticName);
            frame.transform.SetParent(root.transform, false);
            frame.layer = sourcePrefab.layer;
            frame.AddComponent<MeshFilter>().sharedMesh = frameMesh;
            var frameRenderer = frame.AddComponent<MeshRenderer>();
            frameRenderer.sharedMaterials = SelectMaterials(sourceMaterials, FrameStaticSubMeshes);
            frameRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            frameRenderer.receiveShadows = sourceRenderer.receiveShadows;
            frameRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
            frameRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
            frameRenderer.scaleInLightmap = sourceRenderer.scaleInLightmap;
            frameRenderer.stitchLightmapSeams = sourceRenderer.stitchLightmapSeams;

            var visual = new GameObject(ReceptionWindowVisualName);
            visual.transform.SetParent(root.transform, false);
            visual.layer = sourcePrefab.layer;
            visual.AddComponent<MeshFilter>().sharedMesh = visualMesh;
            var visualRenderer = visual.AddComponent<MeshRenderer>();
            visualRenderer.sharedMaterials = SelectMaterials(sourceMaterials, GlassVisualSubMeshes);
            visualRenderer.shadowCastingMode = ShadowCastingMode.Off;
            visualRenderer.receiveShadows = false;
            visualRenderer.lightProbeUsage = LightProbeUsage.CustomProvided;
            visualRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
            visualRenderer.scaleInLightmap = 0f;

            EnsureVisualSamplePointAndReceiver(visual.transform, visualRenderer);

            StaticEditorFlags sourceFlags = GameObjectUtility.GetStaticEditorFlags(sourceRenderer.gameObject);
            GameObjectUtility.SetStaticEditorFlags(root, 0);
            GameObjectUtility.SetStaticEditorFlags(wall, sourceFlags | StaticEditorFlags.ContributeGI);
            GameObjectUtility.SetStaticEditorFlags(frame, sourceFlags | StaticEditorFlags.ContributeGI);
            GameObjectUtility.SetStaticEditorFlags(visual, 0);

            return PrefabUtility.SaveAsPrefabAsset(root, BakeSafePrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static int ReplaceReceptionWindowsInPrefab(string prefabPath, GameObject bakeSafePrefab)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        int replaced = 0;

        try
        {
            var targets = new List<Transform>();
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (IsOriginalReceptionSourceInstance(transform.gameObject))
                    targets.Add(transform);
            }

            for (int i = 0; i < targets.Count; i++)
            {
                Transform target = targets[i];
                if (target == null)
                    continue;

                var sourceRenderer = target.GetComponent<MeshRenderer>();
                Material[] sourceMaterials = sourceRenderer != null ? sourceRenderer.sharedMaterials : Array.Empty<Material>();

                Transform parent = target.parent;
                int siblingIndex = target.GetSiblingIndex();
                string objectName = target.name;
                bool active = target.gameObject.activeSelf;
                int layer = target.gameObject.layer;
                string tag = target.gameObject.tag;
                Vector3 localPosition = target.localPosition;
                Quaternion localRotation = target.localRotation;
                Vector3 localScale = target.localScale;

                var replacement = (GameObject)PrefabUtility.InstantiatePrefab(bakeSafePrefab, parent);
                replacement.name = objectName;
                replacement.SetActive(active);
                replacement.layer = layer;
                replacement.tag = tag;
                replacement.transform.SetSiblingIndex(siblingIndex);
                replacement.transform.localPosition = localPosition;
                replacement.transform.localRotation = localRotation;
                replacement.transform.localScale = localScale;

                ApplyInstanceMaterials(replacement, sourceMaterials);
                ApplyInstanceRendererSettings(replacement);

                UnityEngine.Object.DestroyImmediate(target.gameObject);
                replaced++;
            }

            if (replaced > 0)
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return replaced;
    }

    private static int UpgradeBakeSafeReceptionWindowsInPrefab(string prefabPath)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        int changed = 0;

        try
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsBakeSafeReceptionInstance(transform.gameObject))
                    continue;

                Transform frame = transform.Find(ReceptionFrameStaticName);
                Transform visual = transform.Find(ReceptionWindowVisualName);
                if (visual == null)
                    continue;

                var visualRenderer = visual.GetComponent<MeshRenderer>();
                var frameRenderer = frame != null ? frame.GetComponent<MeshRenderer>() : null;

                Material[] visualMaterials = visualRenderer != null ? visualRenderer.sharedMaterials : Array.Empty<Material>();
                if (visualMaterials.Length > 1)
                {
                    if (frameRenderer != null)
                        frameRenderer.sharedMaterials = new[] { visualMaterials[1] };

                    visualRenderer.sharedMaterials = new[] { visualMaterials[0] };
                    changed++;
                }

                ApplyInstanceRendererSettings(transform.gameObject);
            }

            if (changed > 0)
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return changed;
    }

    private static void ApplyInstanceMaterials(GameObject replacement, Material[] sourceMaterials)
    {
        if (replacement == null || sourceMaterials == null || sourceMaterials.Length < 4)
            return;

        Transform wall = replacement.transform.Find(WallStaticName);
        Transform frame = replacement.transform.Find(ReceptionFrameStaticName);
        Transform visual = replacement.transform.Find(ReceptionWindowVisualName);

        var wallRenderer = wall != null ? wall.GetComponent<MeshRenderer>() : null;
        if (wallRenderer != null)
            wallRenderer.sharedMaterials = SelectMaterials(sourceMaterials, WallSubMeshes);

        var frameRenderer = frame != null ? frame.GetComponent<MeshRenderer>() : null;
        if (frameRenderer != null)
            frameRenderer.sharedMaterials = SelectMaterials(sourceMaterials, FrameStaticSubMeshes);

        var visualRenderer = visual != null ? visual.GetComponent<MeshRenderer>() : null;
        if (visualRenderer != null)
            visualRenderer.sharedMaterials = SelectMaterials(sourceMaterials, GlassVisualSubMeshes);
    }

    private static void ApplyInstanceRendererSettings(GameObject replacement)
    {
        var wall = replacement.transform.Find(WallStaticName);
        if (wall != null)
            GameObjectUtility.SetStaticEditorFlags(wall.gameObject, GameObjectUtility.GetStaticEditorFlags(wall.gameObject) | StaticEditorFlags.ContributeGI);

        var frame = replacement.transform.Find(ReceptionFrameStaticName);
        if (frame != null)
            GameObjectUtility.SetStaticEditorFlags(frame.gameObject, GameObjectUtility.GetStaticEditorFlags(frame.gameObject) | StaticEditorFlags.ContributeGI);

        var visual = replacement.transform.Find(ReceptionWindowVisualName);
        if (visual == null)
            return;

        GameObjectUtility.SetStaticEditorFlags(visual.gameObject, 0);

        var renderer = visual.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
            renderer.scaleInLightmap = 0f;
        }

        EnsureVisualSamplePointAndReceiver(visual, renderer);
    }

    private static bool IsOriginalReceptionSourceInstance(GameObject gameObject)
    {
        if (gameObject == null || !string.Equals(gameObject.name, SourceName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (IsBakeSafeReceptionInstance(gameObject))
            return false;

        var renderer = gameObject.GetComponent<MeshRenderer>();
        var filter = gameObject.GetComponent<MeshFilter>();
        if (renderer == null || filter == null || filter.sharedMesh == null)
            return false;

        if (renderer.sharedMaterials == null || renderer.sharedMaterials.Length < 4)
            return false;

        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
        string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
        if (string.Equals(sourcePath, SourcePrefabPath, StringComparison.OrdinalIgnoreCase))
            return true;

        return filter.sharedMesh.name.IndexOf(SourceName, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsBakeSafeReceptionInstance(GameObject gameObject)
    {
        if (gameObject == null)
            return false;

        return gameObject.transform.Find(WallStaticName) != null &&
            gameObject.transform.Find(ReceptionFrameStaticName) != null &&
            gameObject.transform.Find(ReceptionWindowVisualName) != null;
    }

    private static void EnsureVisualSamplePointAndReceiver(Transform visual, Renderer renderer)
    {
        if (visual == null)
            return;

        Transform samplePoint = visual.Find(SamplePointName);
        if (samplePoint == null)
        {
            var sampleObject = new GameObject(SamplePointName);
            sampleObject.transform.SetParent(visual, false);
            samplePoint = sampleObject.transform;

            // Preserve existing authored anchors, which may be offset out of wall geometry.
            if (renderer != null)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    samplePoint.localPosition = filter.sharedMesh.bounds.center;
            }
        }

        var receiver = visual.GetComponent<DungeonDynamicProbeReceiver>();
        if (receiver == null)
            receiver = visual.gameObject.AddComponent<DungeonDynamicProbeReceiver>();

        receiver.SetSamplePoint(samplePoint);
    }

    private static Mesh CreateSplitMesh(Mesh source, int[] subMeshes, string meshName)
    {
        var mesh = new Mesh
        {
            name = meshName,
            indexFormat = source.indexFormat
        };

        mesh.vertices = source.vertices;
        if (source.normals != null && source.normals.Length == source.vertexCount)
            mesh.normals = source.normals;
        if (source.tangents != null && source.tangents.Length == source.vertexCount)
            mesh.tangents = source.tangents;
        if (source.colors != null && source.colors.Length == source.vertexCount)
            mesh.colors = source.colors;
        if (source.uv != null && source.uv.Length == source.vertexCount)
            mesh.uv = source.uv;
        if (source.uv2 != null && source.uv2.Length == source.vertexCount)
            mesh.uv2 = source.uv2;
        if (source.uv3 != null && source.uv3.Length == source.vertexCount)
            mesh.uv3 = source.uv3;
        if (source.uv4 != null && source.uv4.Length == source.vertexCount)
            mesh.uv4 = source.uv4;
        if (source.uv5 != null && source.uv5.Length == source.vertexCount)
            mesh.uv5 = source.uv5;
        if (source.uv6 != null && source.uv6.Length == source.vertexCount)
            mesh.uv6 = source.uv6;
        if (source.uv7 != null && source.uv7.Length == source.vertexCount)
            mesh.uv7 = source.uv7;
        if (source.uv8 != null && source.uv8.Length == source.vertexCount)
            mesh.uv8 = source.uv8;

        mesh.subMeshCount = subMeshes.Length;
        for (int i = 0; i < subMeshes.Length; i++)
        {
            int sourceSubMesh = subMeshes[i];
            mesh.SetIndices(
                source.GetIndices(sourceSubMesh),
                source.GetTopology(sourceSubMesh),
                i,
                false);
        }

        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateOrUpdateMeshAsset(Mesh mesh, string path)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        EditorUtility.CopySerialized(mesh, existing);
        EditorUtility.SetDirty(existing);
        UnityEngine.Object.DestroyImmediate(mesh);
        return existing;
    }

    private static Material[] SelectMaterials(Material[] materials, int[] subMeshes)
    {
        var selected = new Material[subMeshes.Length];
        for (int i = 0; i < subMeshes.Length; i++)
            selected[i] = materials[subMeshes[i]];
        return selected;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || AssetDatabase.IsValidFolder(folderPath))
            return;

        string normalized = folderPath.Replace("\\", "/").Trim('/');
        string[] parts = normalized.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
