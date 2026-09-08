using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DunGen.Graph;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Object = UnityEngine.Object;

public static class DungeonMapLoadingRegressionTests
{
    private const string FixtureRoot = "Assets/Generated/DungeonLoadingRegression";
    private const string ReportPath = "Reports/StartupLoading/map-loading-tests.txt";

    // Invoked via official Editor eval; keeps the user's open scene and Play Mode intact.
    public static async void Run()
    {
        if (AssetDatabase.IsValidFolder(FixtureRoot)) throw new InvalidOperationException("Regression fixture folder already exists.");
        var results = new List<string>();
        DungeonMapList source = null;
        DungeonMapList runtime = null;
        try
        {
            Directory.CreateDirectory(FixtureRoot);
            AssetDatabase.Refresh();
            var first = ScriptableObject.CreateInstance<DungeonFlow>();
            var second = ScriptableObject.CreateInstance<DungeonFlow>();
            AssetDatabase.CreateAsset(first, FixtureRoot + "/First.asset");
            AssetDatabase.CreateAsset(second, FixtureRoot + "/Second.asset");
            source = ScriptableObject.CreateInstance<DungeonMapList>();
            AssetDatabase.CreateAsset(source, FixtureRoot + "/Catalog.asset");
            var serialized = new SerializedObject(source);
            var entries = serialized.FindProperty("entries");
            entries.arraySize = 2;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("flow").objectReferenceValue = first;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("budget").intValue = 41;
            entries.GetArrayElementAtIndex(1).FindPropertyRelative("flow").objectReferenceValue = second;
            entries.GetArrayElementAtIndex(1).FindPropertyRelative("budget").intValue = 82;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            DungeonMapContentBuild.RefreshContent();
            runtime = DungeonMapContentBuild.CreateBuildCatalog(source);
            Assert.That(runtime.Count, Is.EqualTo(2));
            Assert.That(runtime.Entries[0].flow, Is.Null);
            Assert.That(runtime.Entries[1].flow, Is.Null);
            Assert.That(runtime.GetContentId(0), Is.Not.EqualTo(runtime.GetContentId(1)));
            Assert.That(runtime.Entries[1].budget, Is.EqualTo(82));
            Assert.That(runtime.GetFlowAssetPath(1), Is.EqualTo(FixtureRoot + "/Second.asset"));
            Assert.That(source.Entries[0].flow, Is.SameAs(first));
            results.Add("PASS two-map build catalog contains metadata only; authored references and budgets preserved");

            await Pump(runtime.LoadFlowAsync(1));
            Assert.That(runtime.LoadError, Is.Null);
            Assert.That(runtime.TryGetFlow(1, out var loadedSecond), Is.True);
            Assert.That(loadedSecond, Is.SameAs(second));
            Assert.That(runtime.Entries[0].flow, Is.Null);
            results.Add("PASS selecting the added map loads only that map");

            await Pump(runtime.LoadFlowAsync(0));
            Assert.That(runtime.TryGetFlow(0, out var loadedFirst), Is.True);
            Assert.That(loadedFirst, Is.SameAs(first));
            Assert.That(runtime.Entries[1].flow, Is.Null);
            Assert.That(runtime.LoadedFlowIndex, Is.Zero);
            results.Add("PASS switching maps releases the previous catalog reference; one resident map");

            runtime.ReleaseLoadedFlow();
            await Task.WhenAll(Pump(runtime.LoadFlowAsync(1)), Pump(runtime.LoadFlowAsync(1)));
            Assert.That(runtime.TryGetFlow(1, out _), Is.True);
            Assert.That(runtime.IsLoading, Is.False);
            results.Add("PASS simultaneous requests for the same map complete without a duplicate cache entry");

            runtime.ReleaseLoadedFlow();
            runtime.Entries[0].contentId = "mismatched-build";
            await Pump(runtime.LoadFlowAsync(0));
            Assert.That(runtime.LoadError, Does.Contain("mismatched"));
            Assert.That(runtime.TryGetFlow(0, out _), Is.False);
            await Pump(runtime.LoadFlowAsync(-1));
            Assert.That(runtime.LoadError, Does.Contain("Invalid"));
            results.Add("PASS invalid index and mismatched map content fail without generating a wrong map");

            serialized.Update();
            serialized.FindProperty("entries").GetArrayElementAtIndex(1).FindPropertyRelative("flow").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.Throws<BuildFailedException>(() => DungeonMapContentBuild.CreateBuildCatalog(source));
            results.Add("PASS a newly added entry with no flow is rejected during build");

            var matrix = AssetDatabase.LoadAssetAtPath<DungeonRoomLocalLightShare.RoomLocalMatrixCatalog>(
                "Assets/Experiments/DungeonRoomLocalLightShare/Data/Matrix/StartMapRoomMatrixCatalog.asset");
            var runtimeMatrix = DungeonMapContentBuild.CreateRuntimeMatrixCatalog(matrix);
            try
            {
                Assert.That(runtimeMatrix.Rooms.Length, Is.EqualTo(matrix.Rooms.Length));
                for (int i = 0; i < matrix.Rooms.Length; i++)
                {
                    Assert.That(runtimeMatrix.Rooms[i].Prefab, Is.Null);
                    Assert.That(runtimeMatrix.Rooms[i].RoomId, Is.EqualTo(matrix.Rooms[i].RoomId));
                    Assert.That(runtimeMatrix.Rooms[i].Doorways, Is.SameAs(matrix.Rooms[i].Doorways));
                    foreach (var doorway in matrix.Rooms[i].Doorways)
                    {
                        Assert.That(matrix.TryFindDoorway(matrix.Rooms[i].RoomId, doorway.Path, out _, out var before), Is.True);
                        Assert.That(runtimeMatrix.TryFindDoorway(matrix.Rooms[i].RoomId, doorway.Path, out _, out var found), Is.True);
                        Assert.That(found.Outgoing, Is.SameAs(before.Outgoing));
                        Assert.That(found.IncomingBounce, Is.SameAs(before.IncomingBounce));
                    }
                }
                results.Add("PASS production doorway lookup and exact lighting data survive removal of authoring prefab references");
            }
            finally { Object.DestroyImmediate(runtimeMatrix); }
        }
        catch (Exception exception) { results.Add("FAIL " + exception); }
        finally
        {
            if (runtime != null) Object.DestroyImmediate(runtime);
            // FixtureRoot was proven absent before this test created it.
            AssetDatabase.DeleteAsset(FixtureRoot);
            DungeonMapContentBuild.RefreshContent();
            Directory.CreateDirectory("Reports/StartupLoading");
            File.WriteAllLines(ReportPath, results);
            Debug.Log("[DungeonMapTests] " + string.Join("\n", results));
        }
    }

    private static async Task Pump(IEnumerator routine)
    {
        float deadline = Time.realtimeSinceStartup + 30f;
        while (routine.MoveNext())
        {
            if (routine.Current is AsyncOperation operation)
                while (!operation.isDone)
                {
                    if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Map resource request timed out.");
                    await Task.Delay(20);
                }
            else await Task.Delay(20);
        }
    }
}
