using System.Linq;
using DunGen;
using DunGen.Graph;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class DungeonStartRoomBranchingTests
{
    private const string ProductionFlowPath =
        "Assets/Prefabs/map_piece/NewPrison/New_Prison_V2_Flow.asset";
    private const string ProductionStartRoomPath =
        "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/StartRoom.prefab";

    [Test]
    public void NewDungeonFlowsKeepStartNodeBranchingOptIn()
    {
        DungeonFlow flow = ScriptableObject.CreateInstance<DungeonFlow>();
        try
        {
            Assert.That(flow.BranchFromStartNode, Is.False);
            Assert.That(flow.FillAllUnusedStartDoorways, Is.False);
            Assert.That(flow.MinimumStartNodeBranches, Is.Zero);
            Assert.That(flow.MatchStartBranchDepthToMainPath, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(flow);
        }
    }

    [Test]
    public void ProductionFlowRequiresTwoToThreeConnectedStartDoors()
    {
        DungeonFlow flow = LoadRequired<DungeonFlow>(ProductionFlowPath);

        Assert.That(flow.Length.Min, Is.EqualTo(3));
        Assert.That(flow.Length.Max, Is.EqualTo(4));
        Assert.That(flow.BranchFromStartNode, Is.True);
        Assert.That(flow.FillAllUnusedStartDoorways, Is.True);
        Assert.That(flow.MinimumStartNodeBranches, Is.EqualTo(1));
        Assert.That(flow.MatchStartBranchDepthToMainPath, Is.False);
    }

    [Test]
    public void ProductionStartRoomHasExactlyThreeDoorways()
    {
        GameObject prefab = LoadRequired<GameObject>(ProductionStartRoomPath);
        Doorway[] doorways = prefab.GetComponentsInChildren<Doorway>(true);

        Assert.That(doorways, Has.Length.EqualTo(3));
    }

    [TestCase(314159)]
    [TestCase(8675309)]
    [TestCase(20260828)]
    public void ProductionGenerationConnectsTwoOrThreeStartDoors(int seed)
    {
        DungeonFlow flow = LoadRequired<DungeonFlow>(ProductionFlowPath);
        GameObject root = new GameObject($"DungeonStartBranchingTest_{seed}");
        var generator = new DunGen.DungeonGenerator(root)
        {
            DungeonFlow = flow,
            ShouldRandomizeSeed = false,
            Seed = seed,
            LengthMultiplier = 2f,
            GenerateAsynchronously = false,
            MaxAttemptCount = 30,
            DebugRender = false,
            TriggerPlacement = TriggerPlacementMode.None,
            AllowTilePooling = false,
        };

        try
        {
            generator.Generate();

            Assert.That(generator.Status, Is.EqualTo(GenerationStatus.Complete));
            Assert.That(generator.CurrentDungeon, Is.Not.Null);
            Assert.That(generator.CurrentDungeon.MainPathTiles.Count, Is.InRange(6, 8));

            Tile startTile = generator.CurrentDungeon.MainPathTiles.Single(
                tile => tile.Placement.GraphNode != null &&
                        tile.Placement.GraphNode.NodeType == NodeType.Start);
            int connectedDoorCount = startTile.UsedDoorways.Count();

            Assert.That(
                connectedDoorCount,
                Is.InRange(2, 3),
                $"seed={seed}, chosenSeed={generator.ChosenSeed}, start={startTile.name}");
        }
        finally
        {
            generator.Clear(stopCoroutines: true);
            Object.DestroyImmediate(root);
        }
    }

    private static T LoadRequired<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.That(asset, Is.Not.Null, $"Missing required asset: {path}");
        return asset;
    }
}
