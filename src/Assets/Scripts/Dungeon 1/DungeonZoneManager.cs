using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using System.Collections.Generic;
using OccaSoftware.Altos.Runtime;

[DefaultExecutionOrder(10000)] // Altos ���� �� ���� ����ᵵ, �츮�� �������� �ٽ� �������
public class DungeonZoneManager : MonoBehaviour
{
    [Header("Rendering Layer Names (Project Settings > Tags and Layers > Rendering Layers)")]
    public string dungeonName = "Dungeon";
    public string defaultName = "Default";

    [Header("Indoor Ambient Override (Altos �̱��)")]
    public bool overrideIndoorAmbient = true;
    public AmbientMode indoorAmbientMode = AmbientMode.Flat;
    public Color indoorAmbientLight = new Color(0.09f, 0.09f, 0.09f, 1f);
    public float indoorAmbientIntensity = 0.9f;

    [Header("Optional: Indoor Skybox Override")]
    public bool overrideIndoorSkybox = false;
    public Material indoorSkybox; // �ʿ� ������ ����ΰ� overrideIndoorSkybox=false

    [Header("Environment Fog")]
    [SerializeField] private bool controlFogOnDungeonTransition = true;
    [SerializeField] private bool fogEnabledInsideDungeon = false;
    [SerializeField] private bool fogEnabledOutsideDungeon = true;

    [Header("Exterior Light Isolation")]
    public bool isolateExteriorDirectionalLights = true;
    public string exteriorRenderingLayerName = "Default";
    [Tooltip("If enabled, directional lights will drop Dungeon rendering layer while player is in dungeon.")]
    public bool removeDungeonLayerFromExteriorLights = true;
    [Tooltip("Fallback option when rendering layer isolation is unavailable.")]
    public bool disableExteriorDirectionalLightsInDungeon = false;
    [SerializeField] private Light[] exteriorDirectionalLights;

    [Header("Cloud Shadow Isolation")]
    [Tooltip("Optional weather/cloud shadow behaviours to disable while in dungeon (e.g., Altos cloud shadow components).")]
    [SerializeField] private Behaviour[] cloudShadowBehaviours;
    [Tooltip("Also disable Altos cloud shadow flags while in dungeon.")]
    [SerializeField] private bool disableAltosCloudShadowsInDungeon = true;

    [Header("Outer Environment Audio")]
    [SerializeField] private AudioMixerGroup outerEnvironmentMixerGroup;

    public bool IsInDungeon { get; private set; }

    private Transform _playerRoot;

    private uint _maskDungeon;
    private uint _maskDefault;

    // ������ ��� (Reflection ���� ����!)
    private Material _origSkybox;
    private AmbientMode _origAmbientMode;
    private Color _origAmbientLight;
    private float _origAmbientIntensity;

    private readonly Dictionary<Light, int> _originalLightRenderingLayerMask = new Dictionary<Light, int>();
    private readonly Dictionary<Light, bool> _originalLightEnabled = new Dictionary<Light, bool>();
    private readonly Dictionary<Behaviour, bool> _originalBehaviourEnabled = new Dictionary<Behaviour, bool>();
    private readonly Dictionary<AudioSource, bool> _originalOuterAudioMute = new Dictionary<AudioSource, bool>();
    private StartMapTerrainAmbienceController _terrainAmbienceController;

    private bool _hasAltosCloudShadowBackup;
    private bool _origAltosCastShadowsEnabled;
    private bool _origAltosScreenShadows;

    private uint _maskExterior;

    private void Awake()
    {
        _maskDungeon = Mask(dungeonName);
        _maskDefault = Mask(defaultName);
        _maskExterior = Mask(exteriorRenderingLayerName);

        BackupRenderSettings();
    }

    private uint Mask(string name)
    {
        int idx = RenderingLayerMask.NameToRenderingLayer(name);
        if (idx < 0)
        {
            Debug.LogError($"[DungeonZoneManager] Rendering Layer '{name}' not found. (Project Settings > Tags and Layers > Rendering Layers)");
            return 0;
        }
        return 1u << idx;
    }

    private void BackupRenderSettings()
    {
        _origSkybox = RenderSettings.skybox;
        _origAmbientMode = RenderSettings.ambientMode;
        _origAmbientLight = RenderSettings.ambientLight;
        _origAmbientIntensity = RenderSettings.ambientIntensity;
    }

    private void RestoreRenderSettingsOnce()
    {
        RenderSettings.skybox = _origSkybox;
        RenderSettings.ambientMode = _origAmbientMode;
        RenderSettings.ambientLight = _origAmbientLight;
        RenderSettings.ambientIntensity = _origAmbientIntensity;
    }

    public void EnterDungeon(Transform playerRoot)
    {
        IsInDungeon = true;
        _playerRoot = playerRoot;

        EnsureTracker(playerRoot);
        ApplyPlayerMaskNow();
        ApplyExteriorIsolation(true);
        ApplyFogState(true);
        ApplyOuterEnvironmentAudioMute(true);
    }

    public void ExitDungeon(Transform playerRoot)
    {
        IsInDungeon = false;
        _playerRoot = playerRoot;

        EnsureTracker(playerRoot);
        ApplyPlayerMaskNow();
        ApplyExteriorIsolation(false);
        ApplyFogState(false);
        ApplyOuterEnvironmentAudioMute(false);
        RestoreRenderSettingsOnce();
    }

    private void ApplyOuterEnvironmentAudioMute(bool mute)
    {
        if (outerEnvironmentMixerGroup == null)
            return;

        if (mute)
        {
            CacheAndMuteOuterEnvironmentSources();
            NotifyTerrainAmbienceMute(true);
            return;
        }

        RestoreOuterEnvironmentSourceMute();
        NotifyTerrainAmbienceMute(false);
    }

    private void CacheAndMuteOuterEnvironmentSources()
    {
        var allAudioSources = FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < allAudioSources.Length; i++)
        {
            var source = allAudioSources[i];
            if (!IsOuterEnvironmentSource(source))
                continue;

            if (!_originalOuterAudioMute.ContainsKey(source))
                _originalOuterAudioMute[source] = source.mute;

            source.mute = true;
        }
    }

    private void RestoreOuterEnvironmentSourceMute()
    {
        foreach (var pair in _originalOuterAudioMute)
        {
            if (pair.Key != null)
                pair.Key.mute = pair.Value;
        }

        _originalOuterAudioMute.Clear();
    }

    private void NotifyTerrainAmbienceMute(bool mute)
    {
        if (_terrainAmbienceController == null)
            _terrainAmbienceController = FindFirstObjectByType<StartMapTerrainAmbienceController>();

        if (_terrainAmbienceController != null)
            _terrainAmbienceController.SetOutdoorMuted(mute);
    }

    private bool IsOuterEnvironmentSource(AudioSource source)
    {
        if (source == null || source.outputAudioMixerGroup == null || outerEnvironmentMixerGroup == null)
            return false;

        if (source.outputAudioMixerGroup == outerEnvironmentMixerGroup)
            return true;

        if (source.outputAudioMixerGroup.audioMixer != outerEnvironmentMixerGroup.audioMixer)
            return false;

        return source.outputAudioMixerGroup.name == outerEnvironmentMixerGroup.name;
    }

    private void ApplyFogState(bool inDungeon)
    {
        if (!controlFogOnDungeonTransition)
            return;

        RenderSettings.fog = inDungeon ? fogEnabledInsideDungeon : fogEnabledOutsideDungeon;
    }

    private void ApplyExteriorIsolation(bool inDungeon)
    {
        if (!isolateExteriorDirectionalLights)
            return;

        if (inDungeon)
        {
            ApplyDirectionalLightIsolation();
            ApplyCloudShadowIsolation();
            return;
        }

        RestoreDirectionalLights();
        RestoreCloudShadowBehaviours();
    }

    private void ApplyDirectionalLightIsolation()
    {
        Light[] targetLights = GetExteriorDirectionalLights();
        if (targetLights == null || targetLights.Length == 0)
            return;

        for (int i = 0; i < targetLights.Length; i++)
        {
            var lightComponent = targetLights[i];
            if (lightComponent == null)
                continue;

            if (!_originalLightRenderingLayerMask.ContainsKey(lightComponent))
                _originalLightRenderingLayerMask[lightComponent] = lightComponent.renderingLayerMask;

            if (!_originalLightEnabled.ContainsKey(lightComponent))
                _originalLightEnabled[lightComponent] = lightComponent.enabled;

            if (removeDungeonLayerFromExteriorLights)
            {
                int targetMask = GetLightRenderingLayerMask(exteriorRenderingLayerName, defaultName);
                if (lightComponent.renderingLayerMask != targetMask)
                    lightComponent.renderingLayerMask = targetMask;
            }

            if (disableExteriorDirectionalLightsInDungeon && lightComponent.type == LightType.Directional)
                lightComponent.enabled = false;
        }
    }

    private Light[] GetExteriorDirectionalLights()
    {
        if (exteriorDirectionalLights != null && exteriorDirectionalLights.Length > 0)
            return exteriorDirectionalLights;

        var allLights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var directionalLights = new List<Light>();

        for (int i = 0; i < allLights.Length; i++)
        {
            var lightComponent = allLights[i];
            if (lightComponent == null || lightComponent.type != LightType.Directional)
                continue;

            directionalLights.Add(lightComponent);
        }

        return directionalLights.ToArray();
    }

    private void RestoreDirectionalLights()
    {
        foreach (var pair in _originalLightRenderingLayerMask)
        {
            if (pair.Key != null)
                pair.Key.renderingLayerMask = pair.Value;
        }

        foreach (var pair in _originalLightEnabled)
        {
            if (pair.Key != null)
                pair.Key.enabled = pair.Value;
        }

        _originalLightRenderingLayerMask.Clear();
        _originalLightEnabled.Clear();
    }

    private static int GetLightRenderingLayerMask(string primaryLayerName, string fallbackLayerName)
    {
        int primaryIndex = RenderingLayerMask.NameToRenderingLayer(primaryLayerName);
        if (primaryIndex >= 0)
            return 1 << primaryIndex;

        int fallbackIndex = RenderingLayerMask.NameToRenderingLayer(fallbackLayerName);
        if (fallbackIndex >= 0)
            return 1 << fallbackIndex;

        return 0;
    }

    private void ApplyCloudShadowIsolation()
    {
        ApplyAltosCloudShadowIsolation();

        if (cloudShadowBehaviours == null || cloudShadowBehaviours.Length == 0)
            return;

        for (int i = 0; i < cloudShadowBehaviours.Length; i++)
        {
            var behaviour = cloudShadowBehaviours[i];
            if (behaviour == null)
                continue;

            if (!_originalBehaviourEnabled.ContainsKey(behaviour))
                _originalBehaviourEnabled[behaviour] = behaviour.enabled;

            behaviour.enabled = false;
        }
    }

    private void RestoreCloudShadowBehaviours()
    {
        RestoreAltosCloudShadowIsolation();

        foreach (var pair in _originalBehaviourEnabled)
        {
            if (pair.Key != null)
                pair.Key.enabled = pair.Value;
        }

        _originalBehaviourEnabled.Clear();
    }

    private void ApplyAltosCloudShadowIsolation()
    {
        if (!disableAltosCloudShadowsInDungeon)
            return;

        var skyDirector = AltosSkyDirector.Instance;
        if (skyDirector == null || skyDirector.cloudDefinition == null)
            return;

        if (!_hasAltosCloudShadowBackup)
        {
            _origAltosCastShadowsEnabled = skyDirector.cloudDefinition.castShadowsEnabled;
            _origAltosScreenShadows = skyDirector.cloudDefinition.screenShadows;
            _hasAltosCloudShadowBackup = true;
        }

        skyDirector.cloudDefinition.castShadowsEnabled = false;
        skyDirector.cloudDefinition.screenShadows = false;
    }

    private void RestoreAltosCloudShadowIsolation()
    {
        if (!_hasAltosCloudShadowBackup)
            return;

        var skyDirector = AltosSkyDirector.Instance;
        if (skyDirector == null || skyDirector.cloudDefinition == null)
            return;

        skyDirector.cloudDefinition.castShadowsEnabled = _origAltosCastShadowsEnabled;
        skyDirector.cloudDefinition.screenShadows = _origAltosScreenShadows;
        _hasAltosCloudShadowBackup = false;
    }

    private void EnsureTracker(Transform root)
    {
        var tracker = root.GetComponent<PlayerRenderLayerTracker>();
        if (tracker == null) tracker = root.gameObject.AddComponent<PlayerRenderLayerTracker>();
        tracker.manager = this;
    }

    public void ApplyPlayerMaskNow()
    {
        if (_playerRoot == null) return;

        // ���� ��: Dungeon�� (�¾� Default ����)
        // ���� ��: Default��
        uint target = IsInDungeon ? _maskDungeon : _maskDefault;

        int total = 0, changed = 0;
        foreach (var r in _playerRoot.GetComponentsInChildren<Renderer>(true))
        {
            total++;
            if (r.renderingLayerMask != target)
            {
                r.renderingLayerMask = target;
                changed++;
            }
        }

        Debug.Log($"[DungeonZoneManager] Player rendering mask -> {(IsInDungeon ? dungeonName : defaultName)} (changed {changed}/{total})");
    }

    private void LateUpdate()
    {
        if (!IsInDungeon) return;

        // Altos�� ��� RenderSettings�� �ٲٴ�, ���� �ȿ����� �츮�� �� ������ �ٽ� ����
        if (overrideIndoorAmbient)
        {
            RenderSettings.ambientMode = indoorAmbientMode;
            RenderSettings.ambientLight = indoorAmbientLight;
            RenderSettings.ambientIntensity = indoorAmbientIntensity;
        }

        if (overrideIndoorSkybox && indoorSkybox != null)
        {
            RenderSettings.skybox = indoorSkybox;
        }

        if (controlFogOnDungeonTransition)
            RenderSettings.fog = fogEnabledInsideDungeon;
    }
}

public class PlayerRenderLayerTracker : MonoBehaviour
{
    public DungeonZoneManager manager;

    private void OnEnable()
    {
        manager?.ApplyPlayerMaskNow();
    }

    private void OnTransformChildrenChanged()
    {
        // ���� ��ü/������ �������� �ڽ� Renderer�� �ٲ�� �ڵ� ������
        manager?.ApplyPlayerMaskNow();
    }
}
