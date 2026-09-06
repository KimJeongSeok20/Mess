using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;

public sealed class SkillTerminalHudRegressionTests
{
    private GameObject _root;
    private PlayerVitalsHud _hud;
    private TextMeshProUGUI _healthLabel;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("SkillTerminalHudRegressionTest", typeof(RectTransform));
        _root.GetComponent<RectTransform>().sizeDelta = new Vector2(278f, 198f);
        _hud = _root.AddComponent<PlayerVitalsHud>();
        _hud.enabled = false;

        var labelObject = new GameObject("Health_Value", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(_root.transform, false);
        _healthLabel = labelObject.GetComponent<TextMeshProUGUI>();
        SetField("_healthValue", _healthLabel);
    }

    [TearDown]
    public void TearDown()
    {
        if (_root != null)
            Object.DestroyImmediate(_root);
    }

    [Test]
    public void MaximumHealthUpgradeKeepsCurrentHealthLabelWithoutDamageFeedback()
    {
        _hud.SetValues(100, 100, 100f, 100f);
        _hud.SetValues(100, 125, 100f, 100f);

        Assert.That(_healthLabel.text, Is.EqualTo("100"));
        Assert.That(GetFloat("_healthNormalized"), Is.EqualTo(0.8f).Within(0.0001f));
        Assert.That(GetFloat("_healthTrailNormalized"), Is.EqualTo(0.8f).Within(0.0001f));
        Assert.That(GetFloat("_hitFlash"), Is.Zero);
        Assert.That(GetFloat("_healthTrailHold"), Is.Zero);
    }

    [Test]
    public void MaximumHealthRefundClampsCurrentHealthWithoutDamageFeedback()
    {
        _hud.SetValues(150, 150, 100f, 100f);
        _hud.SetValues(125, 125, 100f, 100f);

        Assert.That(_healthLabel.text, Is.EqualTo("125"));
        Assert.That(GetFloat("_healthNormalized"), Is.EqualTo(1f));
        Assert.That(GetFloat("_healthTrailNormalized"), Is.EqualTo(1f));
        Assert.That(GetFloat("_hitFlash"), Is.Zero);
        Assert.That(GetFloat("_healthTrailHold"), Is.Zero);
    }

    [Test]
    public void MaximumHealthUpgradePreservesRecentDamageTrailWithoutRestartingFeedback()
    {
        _hud.SetValues(100, 100, 100f, 100f);
        _hud.SetValues(80, 100, 100f, 100f);

        Assert.That(GetFloat("_hitFlash"), Is.EqualTo(1f));
        Assert.That(GetFloat("_healthTrailHold"), Is.GreaterThan(0f));
        Assert.That(GetFloat("_healthTrailNormalized"), Is.EqualTo(1f));

        // Simulate feedback partway through its fade, with 90 HP left in the damage trail.
        SetField("_hitFlash", 0.25f);
        SetField("_healthTrailHold", 0f);
        SetField("_healthTrailNormalized", 0.9f);

        _hud.SetValues(80, 125, 100f, 100f);

        Assert.That(_healthLabel.text, Is.EqualTo("80"));
        Assert.That(GetFloat("_healthNormalized"), Is.EqualTo(80f / 125f).Within(0.0001f));
        Assert.That(GetFloat("_healthTrailNormalized"), Is.EqualTo(90f / 125f).Within(0.0001f));
        Assert.That(GetFloat("_hitFlash"), Is.EqualTo(0.25f));
        Assert.That(GetFloat("_healthTrailHold"), Is.Zero);
    }

    [Test]
    public void MaximumHealthExtendsVisibleCapacityAndKeepsValueClearOfTrace()
    {
        Object.DestroyImmediate(_healthLabel.gameObject);
        InvokeHud("Build", _root.GetComponent<RectTransform>());
        RectTransform trace = _root.transform.Find("Health_PulseTrace").GetComponent<RectTransform>();
        RectTransform value = _root.transform.Find("Health_Value").GetComponent<RectTransform>();
        int[] maximums = { 100, 125, 150, 100 };
        float[] widths = { 116f, 145f, 174f, 116f };

        for (int i = 0; i < maximums.Length; i++)
        {
            _hud.SetValues(100, maximums[i], 100f, 100f);
            Assert.That(trace.sizeDelta.x, Is.EqualTo(widths[i]).Within(0.0001f));
            Assert.That(value.anchoredPosition.x, Is.EqualTo(55f + widths[i] + 10f).Within(0.0001f));
            Assert.That(_root.GetComponent<RectTransform>().rect.width,
                Is.GreaterThanOrEqualTo(value.anchoredPosition.x + value.sizeDelta.x));
        }

        _hud.SetValues(100, 150, 100f, 100f);
        var graphic = trace.GetComponent<UnityEngine.UI.Graphic>();
        MethodInfo populateMesh = graphic.GetType().GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(UnityEngine.UI.VertexHelper) }, null);
        Assert.That(populateMesh, Is.Not.Null);
        using var mesh = new UnityEngine.UI.VertexHelper();
        populateMesh.Invoke(graphic, new object[] { mesh });
        float furthestX = float.NegativeInfinity;
        var vertex = new UnityEngine.UIVertex();
        for (int i = 0; i < mesh.currentVertCount; i++)
        {
            mesh.PopulateUIVertex(ref vertex, i);
            furthestX = Mathf.Max(furthestX, vertex.position.x);
        }
        Assert.That(furthestX, Is.GreaterThanOrEqualTo(trace.rect.xMax),
            "The unfilled maximum-health extension must remain visible when current health stays at 100.");
    }

    [Test]
    public void SpecialPerkTokensShowOnlyOwnedAbilitiesAndCloseGapsWhenRemoved()
    {
        InvokeHud("BuildPerkTokens", _root.GetComponent<RectTransform>());
        _root.SetActive(false);
        PlayerPerks perks = _root.AddComponent<PlayerPerks>();
        perks.enabled = false;
        _root.SetActive(true);
        FieldInfo totalsField = typeof(PlayerPerks).GetField("_totals", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(totalsField, Is.Not.Null);
        var totals = (Dictionary<PlayerPerkKind, float>)totalsField.GetValue(perks);
        RectTransform row = _root.transform.Find("Perk_Tokens").GetComponent<RectTransform>();
        RectTransform jump = row.Find("Perk_ExtraAirJump").GetComponent<RectTransform>();
        RectTransform slam = row.Find("Perk_GroundSlam").GetComponent<RectTransform>();

        _hud.SetPerks(perks);
        Assert.That(row.gameObject.activeSelf, Is.False);

        totals[PlayerPerkKind.ExtraAirJump] = 1f;
        totals[PlayerPerkKind.GroundSlam] = 30f;
        _hud.SetPerks(perks);
        Assert.That(row.gameObject.activeSelf, Is.True);
        Assert.That(jump.gameObject.activeSelf, Is.True);
        Assert.That(slam.gameObject.activeSelf, Is.True);
        Assert.That(jump.anchoredPosition.x, Is.Zero);
        Assert.That(slam.anchoredPosition.x, Is.EqualTo(30f));
        Assert.That(jump.sizeDelta, Is.EqualTo(new Vector2(24f, 24f)));
        Assert.That(row.Find("Perk_LastStand").gameObject.activeSelf, Is.False);

        int dirtyCallbacks = 0;
        jump.GetComponent<UnityEngine.UI.Graphic>().RegisterDirtyVerticesCallback(() => dirtyCallbacks++);
        _hud.SetPerks(perks);
        Assert.That(dirtyCallbacks, Is.Zero, "Unchanged perk ownership must not rebuild HUD token geometry.");

        totals.Remove(PlayerPerkKind.ExtraAirJump);
        _hud.SetPerks(perks);
        Assert.That(jump.gameObject.activeSelf, Is.False);
        Assert.That(slam.anchoredPosition.x, Is.Zero);

        _hud.SetPerks(null);
        Assert.That(row.gameObject.activeSelf, Is.False);
    }

    private void InvokeHud(string methodName, RectTransform root)
    {
        MethodInfo method = typeof(PlayerVitalsHud).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, methodName);
        method.Invoke(_hud, new object[] { root });
    }

    private void SetField(string name, object value)
    {
        FieldInfo field = typeof(PlayerVitalsHud).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        field.SetValue(_hud, value);
    }

    private float GetFloat(string name)
    {
        FieldInfo field = typeof(PlayerVitalsHud).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        return (float)field.GetValue(_hud);
    }
}
