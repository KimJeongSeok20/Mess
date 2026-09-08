using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DunGen.Graph;
using DungeonRoomLocalLightShare;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Generates local map resources and strips only the copies serialized into player scenes.</summary>
public sealed class DungeonMapContentBuild : IPreprocessBuildWithReport, IProcessSceneWithReport
{
    public const string ResourceFolder = "Assets/Generated/DungeonContent/Resources/DungeonMaps";
    public int callbackOrder => -800;

    [MenuItem("Tools/Dungeon/Refresh Deferred Map Content")]
    public static void RefreshContent()
    {
        Directory.CreateDirectory(ResourceFolder);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (string guid in AssetDatabase.FindAssets("t:DungeonMapList"))
        {
            var catalog = AssetDatabase.LoadAssetAtPath<DungeonMapList>(AssetDatabase.GUIDToAssetPath(guid));
            if (catalog == null || catalog.UsesDeferredLoading) continue;
            foreach (var entry in catalog.Entries)
            {
                if (entry?.flow == null) continue; // Invalid active catalogs fail in CreateBuildCatalog.
                string flowPath = AssetDatabase.GetAssetPath(entry.flow);
                AssetDatabase.ImportAsset(flowPath, ImportAssetOptions.ForceSynchronousImport);
                string id = AssetDatabase.AssetPathToGUID(flowPath);
                if (string.IsNullOrEmpty(id)) throw new BuildFailedException("Dungeon flows must be saved assets.");
                string path = ResourceFolder + "/" + id + ".asset";
                if (!required.Add(path)) continue;
                var content = AssetDatabase.LoadAssetAtPath<DungeonMapContent>(path);
                if (content == null)
                {
                    content = ScriptableObject.CreateInstance<DungeonMapContent>();
                    AssetDatabase.CreateAsset(content, path);
                }
                content.contentId = id + "-" + AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(entry.flow));
                content.flow = entry.flow;
                EditorUtility.SetDirty(content);
                // FindAssets/importing the next new map can refresh newly created resource files.
                // Persist the reference before another import can read the initial empty asset.
                AssetDatabase.SaveAssetIfDirty(content);
            }
        }
        // This folder is owned by the generator. Remove only its own stale content assets.
        foreach (string guid in AssetDatabase.FindAssets("t:DungeonMapContent", new[] { ResourceFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith(ResourceFolder + "/", StringComparison.Ordinal) && !required.Contains(path))
                AssetDatabase.DeleteAsset(path);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[DungeonContentBuild] Generated {required.Count} unique local map resources.");
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        // Scene processing must run when map entries change, including scripts-only rebuild requests.
        if ((report.summary.options & BuildOptions.BuildScriptsOnly) != 0)
            throw new BuildFailedException("Deferred dungeon content requires a normal Player build, not BuildScriptsOnly.");
        RefreshContent();
    }

    public static DungeonMapList CreateBuildCatalog(DungeonMapList source)
    {
        if (source == null || source.Count == 0)
            throw new BuildFailedException("An active dungeon map catalog is empty.");
        var entries = new List<DungeonMapList.MapEntry>();
        for (int i = 0; i < source.Count; i++)
        {
            var entry = source.Entries[i];
            if (entry?.flow == null)
                throw new BuildFailedException($"Dungeon catalog '{source.name}', entry {i}: assign a saved DungeonFlow.");
            string id = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(entry.flow));
            string versionId = id + "-" + AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(entry.flow));
            var content = AssetDatabase.LoadAssetAtPath<DungeonMapContent>(ResourceFolder + "/" + id + ".asset");
            if (string.IsNullOrEmpty(id) || content == null || content.flow != entry.flow || content.contentId != versionId)
                throw new BuildFailedException($"Dungeon catalog '{source.name}', entry {i}: generated content is missing or stale. Expected {versionId}, got {(content != null ? content.contentId : "missing")}, flowMatch={content != null && content.flow == entry.flow}.");
            entries.Add(new DungeonMapList.MapEntry
            {
                flow = null, selector = entry.selector, budget = entry.budget,
                contentId = versionId, resourceKey = "DungeonMaps/" + id,
                flowAssetPath = AssetDatabase.GetAssetPath(entry.flow)
            });
        }
        var clone = ScriptableObject.CreateInstance<DungeonMapList>();
        clone.name = source.name + " (Deferred)";
        clone.ConfigureDeferredBuild(entries);
        return clone;
    }

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (report == null || !BuildPipeline.isBuildingPlayer) return;
        var catalogs = new Dictionary<DungeonMapList, DungeonMapList>();
        foreach (var root in scene.GetRootGameObjects())
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (component is RoomLocalStartMapInstaller && property.propertyType == SerializedPropertyType.ObjectReference &&
                    property.objectReferenceValue is RoomLocalMatrixCatalog matrix)
                {
                    property.objectReferenceValue = CreateRuntimeMatrixCatalog(matrix);
                    continue;
                }
                if (property.propertyType != SerializedPropertyType.ObjectReference ||
                    !(property.objectReferenceValue is DungeonMapList source)) continue;
                if (!catalogs.TryGetValue(source, out var clone))
                {
                    clone = CreateBuildCatalog(source);
                    catalogs.Add(source, clone);
                }
                property.objectReferenceValue = clone;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        if (catalogs.Count == 0) return;
        var dependencies = EditorUtility.CollectDependencies(scene.GetRootGameObjects().Cast<Object>().ToArray());
        var leaked = dependencies.Where(x => x is DungeonFlow || x is DungeonTileRotationLightingSetV2 || x is DungeonTileBakeData)
            .Select(AssetDatabase.GetAssetPath).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray();
        if (leaked.Length != 0)
            throw new BuildFailedException("Dungeon content is still directly referenced by " + scene.path + ":\n" + string.Join("\n", leaked.Take(12)));
        Directory.CreateDirectory("Reports/StartupLoading");
        File.WriteAllText("Reports/StartupLoading/deferred-scene-audit.txt",
            $"PASS scene={scene.path} catalogs={catalogs.Count} maps={catalogs.Values.Sum(x => x.Count)} directDungeonAssets=0");
        Debug.Log($"[DungeonContentBuild] {scene.name}: {catalogs.Count} deferred catalogs, no direct dungeon data.");
    }

    public static RoomLocalMatrixCatalog CreateRuntimeMatrixCatalog(RoomLocalMatrixCatalog source)
    {
        var clone = ScriptableObject.CreateInstance<RoomLocalMatrixCatalog>();
        clone.name = source.name + " (Runtime)";
        // The production installer resolves room/door IDs, never RoomEntry.Prefab (authoring/matrix tests only).
        // Keep the exact outgoing maps, bounce data, identifiers and case metadata.
        clone.ConfigureAuthoring(source.SourceFlowPath, source.CensusSeedCount, source.CensusSuccessCount,
            source.LastNewCaseSeed, source.RawSocketCandidateCount,
            source.Rooms.Select(room => room == null ? null :
                new RoomLocalMatrixCatalog.RoomEntry(room.RoomId, null, room.Doorways)).ToArray(), source.Cases);
        return clone;
    }
}
