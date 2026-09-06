using System;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DungeonTileLightSwitch : AInteractable
{
    private enum SwitchMode
    {
        ToggleP100P0,
        CyclePowerLevels,
        SetFixedPower
    }

    [Header("Targets")]
    [SerializeField] private DungeonTileLightmapSwitcher[] switchTargets = Array.Empty<DungeonTileLightmapSwitcher>();
    [SerializeField] private Transform targetRoot;
    [SerializeField] private bool includeInactiveTargets = true;
    [SerializeField] private bool autoFindTargets = true;

    [Header("State")]
    [SerializeField] private DungeonTileLightmapSwitcher.PowerLevel startsPowerLevel = DungeonTileLightmapSwitcher.PowerLevel.P100;
    [SerializeField] private SwitchMode switchMode = SwitchMode.ToggleP100P0;
    [SerializeField] private bool cycleIncludesP0 = true;
    [SerializeField] private DungeonTileLightmapSwitcher.PowerLevel fixedPowerLevel = DungeonTileLightmapSwitcher.PowerLevel.P100;
    [SerializeField] private SyncVar<int> syncedPowerLevel = new((int)DungeonTileLightmapSwitcher.PowerLevel.P100);

    [Header("Prompt Text")]
    [SerializeField] private string turnOffText = "[F] Turn lights off";
    [SerializeField] private string turnOnText = "[F] Turn lights on";
    [SerializeField] private string cyclePowerText = "[F] Cycle power";
    [SerializeField] private string setFixedPowerText = "[F] Set power";

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string isOnBoolParam = "IsOn";

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip switchOnClip;
    [SerializeField] private AudioClip switchOffClip;

    [Header("Debug Preview")]
    [SerializeField] private bool livePreviewInPlayMode;
    [SerializeField] private DungeonTileLightmapSwitcher.PowerLevel livePreviewPowerLevel = DungeonTileLightmapSwitcher.PowerLevel.P100;

    private PromptPresenter _promptPresenter;
    private DungeonTileLightmapSwitcher.PowerLevel _lastAppliedPowerLevel = DungeonTileLightmapSwitcher.PowerLevel.P100;
    private bool _hasAppliedState;

    private void Awake()
    {
        CacheReferences();
        RefreshTargets();

        if (Application.isPlaying)
            ApplyState(GetInitialPowerLevel(), false);
    }

    protected override void OnSpawned()
    {
        base.OnSpawned();

        CacheReferences();
        RefreshTargets();

        syncedPowerLevel.onChanged -= OnPowerLevelChanged;

        if (isServer)
            syncedPowerLevel.value = (int)GetInitialPowerLevel();

        syncedPowerLevel.onChanged += OnPowerLevelChanged;
        ApplyState(ToPowerLevel(syncedPowerLevel.value), false);
    }

    private void OnDestroy()
    {
        syncedPowerLevel.onChanged -= OnPowerLevelChanged;
    }

    private void OnValidate()
    {
        if (!Application.isPlaying || !livePreviewInPlayMode)
            return;

        RefreshTargets();

        if (isServer)
            SetPowerLevelState(livePreviewPowerLevel);
        else
            ApplyState(livePreviewPowerLevel, false);
    }

    public override void Interact()
    {
        // DunGen creates its runtime tiles locally. Their interactables are not
        // PurrNet-spawned objects, so an RPC cannot be sent from this instance.
        // Keep spawned switches server-authoritative, but let local dungeon
        // switches drive their own tile lighting state.
        if (!isSpawned)
        {
            // Route through the scene's networked dungeon controller so the whole room's power
            // state replicates to every peer (audit H10). Fall back to local-only when there is none.
            NetworkDungeonController controller = FindFirstObjectByType<NetworkDungeonController>();
            if (controller != null && controller.IsDungeonActive)
            {
                controller.DebugToggleRoomPowerAtPosition(transform.position);
                return;
            }

            var next = ResolveNextPowerLevel(GetCurrentPowerLevel());
            ApplyState(next, true);
            return;
        }

        if (!isServer)
        {
            InteractServerRpc();
            return;
        }

        ApplyInteractAction();
    }

    [ServerRpc(requireOwnership: false)]
    private void InteractServerRpc()
    {
        ApplyInteractAction();
    }

    [ServerRpc(requireOwnership: false)]
    private void SetPowerServerRpc(int rawPowerLevel)
    {
        SetPowerLevelState(ToPowerLevel(rawPowerLevel));
    }

    public override void OnHover()
    {
        base.OnHover();

        if (_promptPresenter == null)
            _promptPresenter = PromptPresenter.Instance ?? FindObjectOfType<PromptPresenter>();

        if (_promptPresenter == null)
            return;

        _promptPresenter.Show(GetPromptText());
    }

    public override void OnStopHover()
    {
        base.OnStopHover();

        if (_promptPresenter == null)
            _promptPresenter = PromptPresenter.Instance ?? FindObjectOfType<PromptPresenter>();

        if (_promptPresenter != null)
            _promptPresenter.Hide();
    }

    public override bool CanInteract()
    {
        return base.CanInteract() && gameObject.activeInHierarchy;
    }

    public void ApplyExternalPowerLevel(DungeonTileLightmapSwitcher.PowerLevel level, bool playAudio)
    {
        var normalized = ToPowerLevel((int)level);
        if (isSpawned && isServer)
        {
            SetPowerLevelState(normalized);
            if (syncedPowerLevel.value == (int)normalized)
                ApplyState(normalized, playAudio);
            return;
        }

        ApplyState(normalized, playAudio);
    }

    [ContextMenu("Find Targets From Root")]
    private void FindTargets()
    {
        var candidates = new List<DungeonTileLightmapSwitcher>();
        AddTargetsFromRoot(candidates, ResolveTargetSearchRoot());
        switchTargets = BuildStableTargetArray(candidates);
    }

    [ContextMenu("Apply Power P100")]
    private void ApplyPower100Context() => ApplyContextPower(DungeonTileLightmapSwitcher.PowerLevel.P100);

    [ContextMenu("Apply Power P0")]
    private void ApplyPower0Context() => ApplyContextPower(DungeonTileLightmapSwitcher.PowerLevel.P0);

    private void ApplyContextPower(DungeonTileLightmapSwitcher.PowerLevel level)
    {
        if (!Application.isPlaying)
        {
            ApplyState(level, false);
            return;
        }

        if (isServer)
        {
            SetPowerLevelState(level);
            return;
        }

        SetPowerServerRpc((int)level);
    }

    private void ApplyInteractAction()
    {
        var current = ToPowerLevel(syncedPowerLevel.value);
        var next = ResolveNextPowerLevel(current);
        SetPowerLevelState(next);
    }

    private void SetPowerLevelState(DungeonTileLightmapSwitcher.PowerLevel level)
    {
        if (!isServer)
            return;

        int normalized = (int)ToPowerLevel((int)level);
        if (syncedPowerLevel.value == normalized)
        {
            ApplyState(ToPowerLevel(normalized), false);
            return;
        }

        syncedPowerLevel.value = normalized;
    }

    private void OnPowerLevelChanged(int newValue)
    {
        ApplyState(ToPowerLevel(newValue), true);
    }

    private void ApplyState(DungeonTileLightmapSwitcher.PowerLevel level, bool playAudio)
    {
        RefreshTargets();

        bool lightsOn = level != DungeonTileLightmapSwitcher.PowerLevel.P0;
        bool shouldPlayAudio = playAudio && _hasAppliedState && _lastAppliedPowerLevel != level;

        if (animator != null && !string.IsNullOrEmpty(isOnBoolParam))
            animator.SetBool(isOnBoolParam, lightsOn);

        var targets = switchTargets ?? Array.Empty<DungeonTileLightmapSwitcher>();
        for (int i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            if (target != null)
                target.SetPowerLevel(level);
        }

        _lastAppliedPowerLevel = level;
        _hasAppliedState = true;

        if (shouldPlayAudio && audioSource != null)
        {
            var clip = lightsOn ? switchOnClip : switchOffClip;
            if (clip != null)
                audioSource.PlayOneShot(clip);
        }
    }

    private DungeonTileLightmapSwitcher.PowerLevel GetInitialPowerLevel()
    {
        return ToPowerLevel((int)startsPowerLevel);
    }

    private DungeonTileLightmapSwitcher.PowerLevel ResolveNextPowerLevel(DungeonTileLightmapSwitcher.PowerLevel current)
    {
        switch (switchMode)
        {
            case SwitchMode.SetFixedPower:
                return ToPowerLevel((int)fixedPowerLevel);

            case SwitchMode.CyclePowerLevels:
                var cycleLevels = BuildCycleLevels();
                if (cycleLevels.Count == 0)
                    return DungeonTileLightmapSwitcher.PowerLevel.P100;

                int index = cycleLevels.IndexOf(current);
                if (index < 0)
                    return cycleLevels[0];

                return cycleLevels[(index + 1) % cycleLevels.Count];

            default:
                return current == DungeonTileLightmapSwitcher.PowerLevel.P0
                    ? DungeonTileLightmapSwitcher.PowerLevel.P100
                    : DungeonTileLightmapSwitcher.PowerLevel.P0;
        }
    }

    private List<DungeonTileLightmapSwitcher.PowerLevel> BuildCycleLevels()
    {
        var levels = new List<DungeonTileLightmapSwitcher.PowerLevel>(2)
        {
            DungeonTileLightmapSwitcher.PowerLevel.P100
        };

        if (cycleIncludesP0)
            levels.Add(DungeonTileLightmapSwitcher.PowerLevel.P0);

        if (levels.Count == 1)
            levels.Add(DungeonTileLightmapSwitcher.PowerLevel.P0);

        return levels;
    }

    private string GetPromptText()
    {
        var current = GetCurrentPowerLevel();

        switch (switchMode)
        {
            case SwitchMode.CyclePowerLevels:
                return string.IsNullOrWhiteSpace(cyclePowerText) ? turnOnText : cyclePowerText;
            case SwitchMode.SetFixedPower:
                return string.IsNullOrWhiteSpace(setFixedPowerText)
                    ? turnOnText
                    : $"{setFixedPowerText} ({fixedPowerLevel})";
            default:
                return current == DungeonTileLightmapSwitcher.PowerLevel.P0 ? turnOnText : turnOffText;
        }
    }

    private static DungeonTileLightmapSwitcher.PowerLevel ToPowerLevel(int rawValue)
    {
        rawValue = Mathf.Clamp(rawValue, 0, 1);
        return (DungeonTileLightmapSwitcher.PowerLevel)rawValue;
    }

    private DungeonTileLightmapSwitcher.PowerLevel GetCurrentPowerLevel()
    {
        if (!isSpawned && _hasAppliedState)
            return _lastAppliedPowerLevel;

        return ToPowerLevel(syncedPowerLevel.value);
    }

    private void RefreshTargets()
    {
        var candidates = new List<DungeonTileLightmapSwitcher>();
        AddAssignedTargets(candidates);

        if (autoFindTargets)
            AddTargetsFromRoot(candidates, ResolveTargetSearchRoot());

        switchTargets = BuildStableTargetArray(candidates);
    }

    private void AddAssignedTargets(List<DungeonTileLightmapSwitcher> candidates)
    {
        if (candidates == null || switchTargets == null)
            return;

        for (int i = 0; i < switchTargets.Length; i++)
        {
            var target = switchTargets[i];
            if (target != null)
                candidates.Add(target);
        }
    }

    private void AddTargetsFromRoot(List<DungeonTileLightmapSwitcher> candidates, Transform root)
    {
        if (candidates == null || root == null)
            return;

        var foundTargets = root.GetComponentsInChildren<DungeonTileLightmapSwitcher>(includeInactiveTargets);
        for (int i = 0; i < foundTargets.Length; i++)
        {
            var target = foundTargets[i];
            if (target != null)
                candidates.Add(target);
        }
    }

    private DungeonTileLightmapSwitcher[] BuildStableTargetArray(List<DungeonTileLightmapSwitcher> candidates)
    {
        if (candidates == null || candidates.Count == 0)
            return Array.Empty<DungeonTileLightmapSwitcher>();

        var uniqueTargets = new List<DungeonTileLightmapSwitcher>(candidates.Count);
        var seenIds = new HashSet<int>();
        bool hasAssignedBakeData = false;

        for (int i = 0; i < candidates.Count; i++)
        {
            var target = candidates[i];
            if (target == null)
                continue;

            if (!seenIds.Add(target.GetInstanceID()))
                continue;

            if (target.HasAssignedBakeData)
                hasAssignedBakeData = true;

            uniqueTargets.Add(target);
        }

        if (!hasAssignedBakeData)
            return uniqueTargets.ToArray();

        for (int i = uniqueTargets.Count - 1; i >= 0; i--)
        {
            var target = uniqueTargets[i];
            if (target == null || !target.HasAssignedBakeData)
                uniqueTargets.RemoveAt(i);
        }

        return uniqueTargets.ToArray();
    }

    private Transform ResolveTargetSearchRoot()
    {
        if (targetRoot != null)
            return targetRoot;

        var nearestSwitcher = GetComponentInParent<DungeonTileLightmapSwitcher>(true);
        if (nearestSwitcher != null)
            return nearestSwitcher.transform;

        var nearestTile = FindClosestParentComponentTransform("DunGen.Tile", "Tile");
        if (nearestTile != null)
            return nearestTile;

        return transform.root;
    }

    private Transform FindClosestParentComponentTransform(string fullTypeName, string shortTypeName)
    {
        var components = GetComponentsInParent<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component == null)
                continue;

            var type = component.GetType();
            if (type == null)
                continue;

            if (string.Equals(type.FullName, fullTypeName, StringComparison.Ordinal) ||
                string.Equals(type.Name, shortTypeName, StringComparison.Ordinal))
                return component.transform;
        }

        return null;
    }

    private void CacheReferences()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (_promptPresenter == null)
            _promptPresenter = PromptPresenter.Instance ?? FindObjectOfType<PromptPresenter>();
    }
}
