using NUnit.Framework;
using UnityEngine;

public sealed class DungeonSingleBakedLightingTests
{
    [Test]
    public void SingleBakedStateAlwaysKeepsP100()
    {
        var gameObject = new GameObject("SingleBakedState_Test");
        try
        {
            var switcher = gameObject.AddComponent<DungeonTileLightmapSwitcher>();
            var policy = gameObject.AddComponent<DungeonTileLightingPolicy>();
            policy.Configure(
                DungeonTileLightmapSwitcher.LightingMode.SingleBakedState);

            switcher.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P0);

            Assert.IsFalse(switcher.SupportsPowerToggle);
            Assert.AreEqual(
                DungeonTileLightmapSwitcher.PowerLevel.P100,
                switcher.CurrentPowerLevel);
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }
}
