using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerVitals))]
public class SkillWebHealthTestBridge : MonoBehaviour
{
    [Header("Target Skill")]
    [SerializeField] private string targetSkillName = "Health Boost I";
    [SerializeField, Min(1)] private int healthBonusPerLevel = 25;
    [SerializeField] private string modifierKey = "skillweb.health_boost_test";

    [Header("Test Setup")]
    [SerializeField] private bool grantInitialSkillPoint = true;
    [SerializeField, Min(0)] private int initialSkillPoints = 1;
    [SerializeField, Min(0.1f)] private float refreshInterval = 0.25f;

    private PlayerVitals _vitals;
    private NetworkPlayer _networkPlayer;

    private Type _webViewType;
    private PropertyInfo _activeWebViewProperty;
    private FieldInfo _webField;
    private MethodInfo _getSkillNodesMethod;

    private PropertyInfo _nodeLevelProperty;
    private PropertyInfo _nodeIsObtainedProperty;

    private Type _skillWebRuntimeType;
    private FieldInfo _skillPointsField;

    private int _lastAppliedBonus = int.MinValue;
    private float _nextRefreshTime;

    private void Awake()
    {
        _vitals = GetComponent<PlayerVitals>();
        _networkPlayer = GetComponent<NetworkPlayer>();
        ResolveReflectionCache();
    }

    private void OnEnable()
    {
        TrySeedSkillPoints();
        RefreshHealthModifier();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRefreshTime)
            return;

        _nextRefreshTime = Time.unscaledTime + refreshInterval;
        RefreshHealthModifier();
    }

    private void OnDisable()
    {
        RemoveHealthModifier();
        _lastAppliedBonus = int.MinValue;
    }

    private void ResolveReflectionCache()
    {
        _webViewType = ResolveType("Esper.SkillWeb.UI.UGUI.WebViewUGUI");
        if (_webViewType != null)
        {
            _activeWebViewProperty = _webViewType.GetProperty("Active", BindingFlags.Public | BindingFlags.Static);
            _webField = _webViewType.GetField("web", BindingFlags.Public | BindingFlags.Instance) ??
                        _webViewType.BaseType?.GetField("web", BindingFlags.Public | BindingFlags.Instance);

            if (_webField?.FieldType != null)
                _getSkillNodesMethod = _webField.FieldType.GetMethod("GetSkillNodes", new[] { typeof(string) });
        }

        Type skillNodeType = ResolveType("Esper.SkillWeb.Graph.SkillNode");
        if (skillNodeType != null)
        {
            _nodeLevelProperty = skillNodeType.GetProperty("Level", BindingFlags.Public | BindingFlags.Instance);
            _nodeIsObtainedProperty = skillNodeType.GetProperty("IsObtained", BindingFlags.Public | BindingFlags.Instance);
        }

        _skillWebRuntimeType = ResolveType("Esper.SkillWeb.SkillWeb");
        if (_skillWebRuntimeType != null)
            _skillPointsField = _skillWebRuntimeType.GetField("skillPoints", BindingFlags.Public | BindingFlags.Static);
    }

    private static Type ResolveType(string fullTypeName)
    {
        string[] candidates =
        {
            fullTypeName + ", SkillWeb",
            fullTypeName + ", SkillWeb.Player"
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            var type = Type.GetType(candidates[i], false);
            if (type != null)
                return type;
        }

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            var type = assemblies[i].GetType(fullTypeName, false);
            if (type != null)
                return type;
        }

        return null;
    }

    private void RefreshHealthModifier()
    {
        if (_vitals == null)
            return;

        // PlayerPerks now owns every SkillWeb → stat mapping (including Health Boost); this test
        // bridge only stays for scenes without it, otherwise the bonus would be applied twice.
        if (GetComponent<PlayerPerks>() != null)
        {
            RemoveHealthModifier();
            return;
        }

        if (!IsLocalPlayer())
        {
            RemoveHealthModifier();
            return;
        }

        TrySeedSkillPoints();

        if (string.IsNullOrWhiteSpace(targetSkillName) || string.IsNullOrWhiteSpace(modifierKey) || healthBonusPerLevel <= 0)
        {
            RemoveHealthModifier();
            return;
        }

        if (!TryGetObtainedLevelTotal(out int totalObtainedLevel))
            return;

        if (totalObtainedLevel <= 0)
        {
            RemoveHealthModifier();
            return;
        }

        int healthBonus = totalObtainedLevel * healthBonusPerLevel;
        if (_lastAppliedBonus == healthBonus)
            return;

        _vitals.SetSkillWebModifier(modifierKey, healthBonus, 0f, 0f, 0f);
        _lastAppliedBonus = healthBonus;
    }

    private bool TryGetObtainedLevelTotal(out int totalObtainedLevel)
    {
        totalObtainedLevel = 0;

        if (_activeWebViewProperty == null || _webField == null || _getSkillNodesMethod == null)
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
            nodesObject = _getSkillNodesMethod.Invoke(web, new object[] { targetSkillName });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SkillWebHealthTestBridge] Failed to query skill nodes: {ex.Message}");
            return false;
        }

        if (nodesObject is not IEnumerable nodes)
            return false;

        int levelSum = 0;

        foreach (object node in nodes)
        {
            if (node == null || _nodeLevelProperty == null || _nodeIsObtainedProperty == null)
                continue;

            bool obtained;
            int level;

            try
            {
                object obtainedObject = _nodeIsObtainedProperty.GetValue(node);
                obtained = obtainedObject is bool value && value;
                object levelObject = _nodeLevelProperty.GetValue(node);
                level = levelObject is int levelValue ? levelValue : 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SkillWebHealthTestBridge] Failed to read node state: {ex.Message}");
                continue;
            }

            if (!obtained)
                continue;

            levelSum += Mathf.Max(0, level);
        }

        totalObtainedLevel = levelSum;
        return true;
    }

    private void RemoveHealthModifier()
    {
        if (_vitals == null)
            return;

        if (_lastAppliedBonus == 0)
            return;

        _vitals.RemoveSkillWebModifier(modifierKey);
        _lastAppliedBonus = 0;
    }

    private void TrySeedSkillPoints()
    {
        if (!grantInitialSkillPoint || !IsLocalPlayer())
            return;

        if (_skillPointsField == null)
            return;

        try
        {
            int current = (int)_skillPointsField.GetValue(null);
            if (current < initialSkillPoints)
                _skillPointsField.SetValue(null, initialSkillPoints);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SkillWebHealthTestBridge] Failed to seed skill points: {ex.Message}");
        }
    }

    private bool IsLocalPlayer()
    {
        if (_networkPlayer != null && _networkPlayer.isOwner)
            return true;

        Camera ownerCamera = GetComponentInChildren<Camera>(true);
        return ownerCamera != null && ownerCamera.isActiveAndEnabled;
    }
}
