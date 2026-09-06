using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

public sealed class SmilyInvestigationLogicTests
{
    private static readonly MethodInfo TickDetectionAndMovement = typeof(SmilyBrain).GetMethod(
        "TickDetectionAndMovement",
        BindingFlags.Instance | BindingFlags.NonPublic);

    [Test]
    public void HiddenTargetUsesLastKnownPositionUntilItIsSeenAgainOrMemoryExpires()
    {
        GameObject smily = null;
        GameObject player = null;
        GameObject wall = null;

        try
        {
            smily = new GameObject("SmilyInvestigationTest");
            smily.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            NavMeshAgent agent = smily.AddComponent<NavMeshAgent>();
            RangeDetector rangeDetector = smily.AddComponent<RangeDetector>();
            LineOfSightDetector lineOfSightDetector = smily.AddComponent<LineOfSightDetector>();
            SmilyBrain brain = smily.AddComponent<SmilyBrain>();
            SetField(brain, "agent", agent);
            SetField(brain, "rangeDetector", rangeDetector);
            SetField(brain, "lineOfSightDetector", lineOfSightDetector);
            SetField(brain, "targetMemoryDuration", 2.5f);
            SetField(brain, "investigationArrivalDistance", 0.8f);

            player = CreateRegisteredPlayer("InvestigationTarget", new Vector3(0f, 0f, 4f));

            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "InvestigationSightBlocker";
            wall.transform.SetPositionAndRotation(new Vector3(0f, 2f, 2f), Quaternion.identity);
            wall.transform.localScale = new Vector3(8f, 5f, 0.5f);
            Physics.SyncTransforms();

            InvokeDetectionTick(brain);
            Assert.That(brain.CurrentState, Is.EqualTo(SmilyBrain.SmilyState.Idle));
            Assert.That(brain.HasTarget, Is.False, "A never-seen player behind a wall must not be acquired.");
            Assert.That(brain.HasLastKnownTargetPosition, Is.False);

            wall.SetActive(false);
            Physics.SyncTransforms();
            InvokeDetectionTick(brain);

            Assert.That(brain.CurrentState, Is.EqualTo(SmilyBrain.SmilyState.Chase));
            Assert.That(brain.HasTarget, Is.True);
            Assert.That(brain.HasLastKnownTargetPosition, Is.True);
            Vector3 firstVisiblePosition = brain.LastKnownTargetPosition;
            Assert.That(Vector3.Distance(firstVisiblePosition, player.transform.position), Is.LessThan(0.001f));

            wall.SetActive(true);
            player.transform.position = new Vector3(2f, 0f, 4f);
            Physics.SyncTransforms();
            InvokeDetectionTick(brain);

            Assert.That(brain.CurrentState, Is.EqualTo(SmilyBrain.SmilyState.Investigate));
            Assert.That(Vector3.Distance(brain.LastKnownTargetPosition, firstVisiblePosition), Is.LessThan(0.001f),
                "The remembered destination must not follow a hidden player's live position.");
            Assert.That(Vector3.Distance(brain.LastKnownTargetPosition, player.transform.position), Is.GreaterThan(1f));

            wall.SetActive(false);
            Physics.SyncTransforms();
            InvokeDetectionTick(brain);

            Assert.That(brain.CurrentState, Is.EqualTo(SmilyBrain.SmilyState.Chase));
            Assert.That(Vector3.Distance(brain.LastKnownTargetPosition, player.transform.position), Is.LessThan(0.001f),
                "Seeing the player again must refresh the remembered position.");

            wall.SetActive(true);
            Physics.SyncTransforms();
            SetField(brain, "_lastKnownTargetExpiresAt", Time.time - 1f);
            InvokeDetectionTick(brain);

            Assert.That(brain.CurrentState, Is.EqualTo(SmilyBrain.SmilyState.Idle));
            Assert.That(brain.HasTarget, Is.False);
            Assert.That(brain.HasLastKnownTargetPosition, Is.False);
        }
        finally
        {
            if (wall != null)
                Object.DestroyImmediate(wall);
            if (player != null)
                Object.DestroyImmediate(player);
            if (smily != null)
                Object.DestroyImmediate(smily);
        }
    }

    [Test]
    public void VisibleTargetIsChosenWhenACloserPlayerIsOccluded()
    {
        GameObject smily = null;
        GameObject hiddenPlayer = null;
        GameObject visiblePlayer = null;
        GameObject wall = null;

        try
        {
            smily = new GameObject("SmilyVisibleTargetSelectionTest");
            smily.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            NavMeshAgent agent = smily.AddComponent<NavMeshAgent>();
            RangeDetector rangeDetector = smily.AddComponent<RangeDetector>();
            LineOfSightDetector lineOfSightDetector = smily.AddComponent<LineOfSightDetector>();
            SmilyBrain brain = smily.AddComponent<SmilyBrain>();
            SetField(brain, "agent", agent);
            SetField(brain, "rangeDetector", rangeDetector);
            SetField(brain, "lineOfSightDetector", lineOfSightDetector);

            hiddenPlayer = CreateRegisteredPlayer("CloserHiddenPlayer", new Vector3(0f, 0f, 4f));
            visiblePlayer = CreateRegisteredPlayer("FartherVisiblePlayer", new Vector3(3f, 0f, 5f));

            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "NarrowSightBlocker";
            wall.transform.SetPositionAndRotation(new Vector3(0f, 2f, 2f), Quaternion.identity);
            wall.transform.localScale = new Vector3(1.5f, 5f, 0.5f);
            Physics.SyncTransforms();

            InvokeDetectionTick(brain);

            Assert.That(brain.CurrentState, Is.EqualTo(SmilyBrain.SmilyState.Chase));
            Assert.That(brain.CurrentTargetName, Is.EqualTo(visiblePlayer.name));
            Assert.That(Vector3.Distance(brain.LastKnownTargetPosition, visiblePlayer.transform.position), Is.LessThan(0.001f));
        }
        finally
        {
            if (wall != null)
                Object.DestroyImmediate(wall);
            if (visiblePlayer != null)
                Object.DestroyImmediate(visiblePlayer);
            if (hiddenPlayer != null)
                Object.DestroyImmediate(hiddenPlayer);
            if (smily != null)
                Object.DestroyImmediate(smily);
        }
    }

    private static void InvokeDetectionTick(SmilyBrain brain)
    {
        Assert.That(TickDetectionAndMovement, Is.Not.Null);
        TickDetectionAndMovement.Invoke(brain, null);
    }

    private static GameObject CreateRegisteredPlayer(string name, Vector3 position)
    {
        GameObject player = new GameObject(name);
        player.transform.position = position;

        CapsuleCollider playerCollider = player.AddComponent<CapsuleCollider>();
        playerCollider.center = Vector3.up;
        playerCollider.height = 2f;

        PlayerPawn playerPawn = player.AddComponent<PlayerPawn>();
        if (!PlayerPawn.All.Contains(playerPawn))
            PlayerPawn.All.Add(playerPawn);

        return player;
    }

    private static void SetField<T>(SmilyBrain brain, string fieldName, T value)
    {
        FieldInfo field = typeof(SmilyBrain).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing SmilyBrain field '{fieldName}'.");
        field.SetValue(brain, value);
    }
}
