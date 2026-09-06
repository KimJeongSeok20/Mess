using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Read-only bridge to the StylishEsper SkillWeb runtime (reflection, so no assembly reference is
/// needed and a missing package degrades to "no skills"). Returns the total obtained level of a
/// skill by name across the active web view.
/// </summary>
public static class SkillWebQuery
{
    private static bool _resolved;
    private static PropertyInfo _activeWebViewProperty;
    private static FieldInfo _webField;
    private static MethodInfo _getSkillNodesMethod;
    private static PropertyInfo _nodeLevelProperty;
    private static PropertyInfo _nodeIsObtainedProperty;

    public static bool IsAvailable
    {
        get
        {
            Resolve();
            return _activeWebViewProperty != null && _webField != null && _getSkillNodesMethod != null;
        }
    }

    /// <summary>
    /// Sum of levels of every obtained node named <paramref name="skillName"/>.
    /// Returns false when the web is not loaded yet (treat as "unknown", not zero).
    /// </summary>
    public static bool TryGetObtainedLevel(string skillName, out int totalLevel)
    {
        totalLevel = 0;
        if (string.IsNullOrWhiteSpace(skillName) || !IsAvailable)
            return false;

        object activeView = _activeWebViewProperty.GetValue(null);
        if (activeView == null)
            return false;

        object web = _webField.GetValue(activeView);
        if (web == null)
            return false;

        object nodesObject;
        try
        {
            nodesObject = _getSkillNodesMethod.Invoke(web, new object[] { skillName });
        }
        catch (Exception)
        {
            return false;
        }

        if (nodesObject is not IEnumerable nodes)
            return true;

        foreach (object node in nodes)
        {
            if (node == null)
                continue;

            bool obtained = _nodeIsObtainedProperty == null || (_nodeIsObtainedProperty.GetValue(node) is bool b && b);
            if (!obtained)
                continue;

            int level = 1;
            if (_nodeLevelProperty != null && _nodeLevelProperty.GetValue(node) is int nodeLevel)
                level = Mathf.Max(1, nodeLevel);

            totalLevel += level;
        }

        return true;
    }

    private static void Resolve()
    {
        if (_resolved)
            return;

        _resolved = true;

        Type webViewType = ResolveType("Esper.SkillWeb.UI.UGUI.WebViewUGUI");
        if (webViewType != null)
        {
            _activeWebViewProperty = webViewType.GetProperty("Active", BindingFlags.Public | BindingFlags.Static);
            _webField = webViewType.GetField("web", BindingFlags.Public | BindingFlags.Instance)
                        ?? webViewType.BaseType?.GetField("web", BindingFlags.Public | BindingFlags.Instance);
            if (_webField?.FieldType != null)
                _getSkillNodesMethod = _webField.FieldType.GetMethod("GetSkillNodes", new[] { typeof(string) });
        }

        Type skillNodeType = ResolveType("Esper.SkillWeb.Graph.SkillNode");
        if (skillNodeType != null)
        {
            _nodeLevelProperty = skillNodeType.GetProperty("Level", BindingFlags.Public | BindingFlags.Instance);
            _nodeIsObtainedProperty = skillNodeType.GetProperty("IsObtained", BindingFlags.Public | BindingFlags.Instance);
        }
    }

    private static Type ResolveType(string fullTypeName)
    {
        string[] candidates = { fullTypeName + ", SkillWeb", fullTypeName + ", SkillWeb.Player" };
        for (int i = 0; i < candidates.Length; i++)
        {
            Type type = Type.GetType(candidates[i], false);
            if (type != null)
                return type;
        }

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType(fullTypeName, false);
            if (type != null)
                return type;
        }

        return null;
    }
}
