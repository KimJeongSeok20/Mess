using NUnit.Framework;
using UnityEngine;

public sealed class HeldItemStudioTests
{
    [Test]
    public void CenterLocalBounds_MovesWorldCenterOntoParentOrigin()
    {
        GameObject pivot = new("HeldItemPivot");
        GameObject visual = new("Visual");
        visual.transform.SetParent(pivot.transform, false);
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(visual.transform, false);
        cube.transform.localPosition = new Vector3(2f, 0f, 0f);

        HeldItemVisualPlacement.CenterLocalBounds(visual.transform);

        Assert.That(HeldItemVisualPlacement.TryGetWorldRendererBounds(visual.transform, out Bounds bounds), Is.True);
        Vector3 centerInPivot = pivot.transform.InverseTransformPoint(bounds.center);
        Assert.That(centerInPivot.magnitude, Is.LessThan(0.02f));

        Object.DestroyImmediate(pivot);
    }

    [Test]
    public void Apply_UsesPivotPoseAndKeepsVisualCentered()
    {
        GameObject anchor = new("Anchor");
        GameObject pivot = new("Pivot");
        pivot.transform.SetParent(anchor.transform, false);
        GameObject visual = new("Visual");
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(visual.transform, false);
        cube.transform.localPosition = new Vector3(0f, 1f, 0f);

        var entry = new HeldItemVisualCatalog.Entry
        {
            itemName = "Test",
            localPosition = new Vector3(0.1f, 0f, 0f),
            localEulerAngles = new Vector3(0f, 90f, 0f),
            localScale = new Vector3(2f, 2f, 2f)
        };

        HeldItemVisualPlacement.Apply(pivot.transform, visual.transform, entry);

        Assert.That(pivot.transform.localPosition, Is.EqualTo(entry.localPosition));
        Assert.That(Quaternion.Angle(pivot.transform.localRotation, Quaternion.Euler(entry.localEulerAngles)),
            Is.LessThan(0.1f));
        Assert.That(pivot.transform.localScale, Is.EqualTo(entry.localScale));
        Assert.That(HeldItemVisualPlacement.TryGetWorldRendererBounds(visual.transform, out Bounds bounds), Is.True);
        Vector3 centerInPivot = pivot.transform.InverseTransformPoint(bounds.center);
        Assert.That(centerInPivot.magnitude, Is.LessThan(0.05f));

        Object.DestroyImmediate(anchor);
    }

    [Test]
    public void CollectSources_IncludesStartMapGiftBoxesAndDungeonItems()
    {
        var names = HeldItemAuthoringTool.GetExpectedHeldItemNames();
        Assert.That(names, Does.Contain("GiftBox1"));
        Assert.That(names, Does.Contain("GiftBox2"));
        Assert.That(names, Does.Contain("GiftBox3"));
        Assert.That(names, Does.Contain("GiftBox4"));
        Assert.That(names, Does.Contain("Banana"));
        Assert.That(names, Does.Contain("RedGuitar"));
        Assert.That(names, Does.Contain("Octopus"));
        Assert.That(names, Does.Contain("Trophy"));
        Assert.That(names, Does.Not.Contain("AK"));
        Assert.That(names, Does.Not.Contain("Fireball"));
    }
}
