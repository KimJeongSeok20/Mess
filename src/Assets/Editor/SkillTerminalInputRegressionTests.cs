using System.Reflection;
using Demo.Scripts.Runtime.Character;
using NUnit.Framework;
using UnityEngine;
using WebViewUGUI = Esper.SkillWeb.UI.UGUI.WebViewUGUI;

public sealed class SkillTerminalInputRegressionTests
{
    [Test]
    public void ClosingFrameBlocksGameplayCallbacksAndClearsHeldInput()
    {
        FieldInfo closedFrame = typeof(SkillWebTerminalInteraction).GetField("_lastClosedFrame", BindingFlags.Static | BindingFlags.NonPublic);
        int previousClosedFrame = (int)closedFrame.GetValue(null);
        FieldInfo activeTerminal = typeof(SkillWebTerminalInteraction).GetField("_activeTerminal", BindingFlags.Static | BindingFlags.NonPublic);
        object previousTerminal = activeTerminal.GetValue(null);
        GameObject player = null;
        try
        {
            player = new GameObject("SkillTerminalInputRegressionTest");
            FPSController controller = player.AddComponent<FPSController>();
            FPSMovement movement = player.GetComponent<FPSMovement>();
            FPSInputHandler handler = player.GetComponent<FPSInputHandler>();
            SetField(controller, "_inputHandler", handler);
            SetField(controller, "_movementComponent", movement);
            SetField(controller, "_lookDeltaInput", new Vector2(8f, 4f));
            SetField(movement, "_inputDirection", Vector2.one);
            SetField(movement, "_sprintHeld", true);
            SetField(movement, "_velocity", new Vector3(3f, 7f, 4f));
            handler.ProcessLookInput(new Vector2(8f, 4f));
            int fireReleases = 0;
            handler.OnFireReleased += () => fireReleases++;

            closedFrame.SetValue(null, Time.frameCount);
            activeTerminal.SetValue(null, null);
            Assert.DoesNotThrow(() => SkillWebTerminalInteraction.CloseForPlayerStateChange());
            Assert.That(SkillWebTerminalInteraction.BlocksGameplayInput, Is.True);
            controller.ClearGameplayInputForModal();

            Assert.That(GetField<Vector2>(controller, "_lookDeltaInput"), Is.EqualTo(Vector2.zero));
            Assert.That(handler.LookDeltaInput, Is.EqualTo(Vector2.zero));
            Assert.That(fireReleases, Is.EqualTo(1));
            Assert.That(GetField<Vector2>(movement, "_inputDirection"), Is.EqualTo(Vector2.zero));
            Assert.That(GetField<bool>(movement, "_sprintHeld"), Is.False);
            Assert.That(GetField<Vector3>(movement, "_velocity"), Is.EqualTo(new Vector3(0f, 7f, 0f)),
                "Opening a menu must stop held horizontal movement while preserving gravity/vertical motion.");

            // These callbacks must return before reading the input or opening another screen.
            Assert.DoesNotThrow(() => controller.OnToggleInventory(null));
            Assert.DoesNotThrow(() => controller.OnToggleRoomPower(null));
            Assert.DoesNotThrow(() => controller.OnLook(null));
            Assert.DoesNotThrow(() => movement.OnMove(null));
            Assert.DoesNotThrow(() => movement.OnJump());
        }
        finally
        {
            if (player != null)
                Object.DestroyImmediate(player);
            activeTerminal.SetValue(null, previousTerminal);
            closedFrame.SetValue(null, previousClosedFrame);
        }
    }

    [Test]
    public void StartingPointSeedDoesNotRefillSpentOrRestoredBalances()
    {
        int oldRank = TeamProgress.CivicRank;
        int oldEarned = TeamProgress.SkillPointsEarned;
        int oldPoints = Esper.SkillWeb.SkillWeb.skillPoints;
        int oldLevel = Esper.SkillWeb.SkillWeb.playerLevel;
        FieldInfo applied = typeof(TeamProgress).GetField("_skillPointsApplied", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo seeded = typeof(TeamProgress).GetField("_initialSkillPointStateEstablished", BindingFlags.Static | BindingFlags.NonPublic);
        object oldApplied = applied.GetValue(null);
        object oldSeeded = seeded.GetValue(null);
        PropertyInfo activeView = typeof(WebViewUGUI).GetProperty("Active", BindingFlags.Public | BindingFlags.Static);
        object oldView = activeView.GetValue(null);
        try
        {
            // Keep this pure progression test from resetting any authored graph visible in the Editor.
            activeView.SetValue(null, null);
            TeamProgress.ResetAll();
            TeamProgress.EnsureInitialSkillPoints(1);
            Assert.That(Esper.SkillWeb.SkillWeb.skillPoints, Is.EqualTo(1));

            Esper.SkillWeb.SkillWeb.skillPoints = 0;
            TeamProgress.EnsureInitialSkillPoints(1);
            Assert.That(Esper.SkillWeb.SkillWeb.skillPoints, Is.Zero, "Reopening a terminal must not refill a spent seed.");

            TeamProgress.RestoreCheckpoint(3, 8, 0);
            TeamProgress.EnsureInitialSkillPoints(1);
            Assert.That(Esper.SkillWeb.SkillWeb.skillPoints, Is.Zero, "A restored zero balance is authoritative.");
            Assert.That(TeamProgress.CivicRank, Is.EqualTo(3));

            TeamProgress.ResetAll();
            TeamProgress.EnsureInitialSkillPoints(1);
            Assert.That(Esper.SkillWeb.SkillWeb.skillPoints, Is.EqualTo(1), "A new run may receive its initial seed once.");
        }
        finally
        {
            TeamProgress.RestoreCheckpoint(oldRank, oldEarned, oldPoints);
            Esper.SkillWeb.SkillWeb.playerLevel = oldLevel;
            applied.SetValue(null, oldApplied);
            seeded.SetValue(null, oldSeeded);
            activeView.SetValue(null, oldView);
        }
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }
}
