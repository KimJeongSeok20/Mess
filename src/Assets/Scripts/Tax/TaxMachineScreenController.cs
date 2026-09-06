using UnityEngine;

/// <summary>
/// Owns only the extracted upper-monitor renderer of a tax machine.  The machine supplies the
/// synchronized runtime state; the optional Inspector preview is deliberately local/editor-facing.
/// </summary>
[DisallowMultipleComponent]
public sealed class TaxMachineScreenController : MonoBehaviour
{
    [Header("Dedicated Display Renderer")]
    [SerializeField] private Renderer screenRenderer;
    [SerializeField] private Material days2Material;
    [SerializeField] private Material days1Material;
    [SerializeField] private Material payMaterial;
    [SerializeField] private Material frownMaterial;
    [SerializeField] private Material smileMaterial;

    [Header("Inspector Preview / Override")]
    [Tooltip("In Edit Mode, Manual Preview State always renders for easy asset review. In Play Mode, enable this to hold a local override instead of the synchronized runtime state.")]
    [SerializeField] private bool useManualPreview;
    [SerializeField] private TaxMachineScreenState manualPreviewState = TaxMachineScreenState.Frown;
    [Tooltip("D-2, D-1, and PAY alternate with the frown at this interval during Play Mode.")]
    [SerializeField, Min(0.1f)] private float alternateSeconds = 2f;

    private TaxMachineScreenState _runtimeState = TaxMachineScreenState.Days2;
    private TaxMachineScreenState _lastSelectedState = (TaxMachineScreenState)(-1);
    private float _selectedStateChangedAt;

    public TaxMachineScreenState RuntimeState => _runtimeState;

    /// <summary>
    /// Called by prefab authoring to wire the one extracted renderer and its five state materials.
    /// </summary>
    public void ConfigureAuthoring(
        Renderer configuredScreenRenderer,
        Material configuredDays2Material,
        Material configuredDays1Material,
        Material configuredPayMaterial,
        Material configuredFrownMaterial,
        Material configuredSmileMaterial)
    {
        screenRenderer = configuredScreenRenderer;
        days2Material = configuredDays2Material;
        days1Material = configuredDays1Material;
        payMaterial = configuredPayMaterial;
        frownMaterial = configuredFrownMaterial;
        smileMaterial = configuredSmileMaterial;
        ApplyVisibleState();
    }

    /// <summary>
    /// Applies a state selected by the server-owned machine SyncVar. This touches only the
    /// dedicated screen renderer's material; no body, lamps, or scene state are changed.
    /// </summary>
    public void SetRuntimeState(TaxMachineScreenState state)
    {
        _runtimeState = state;
        ApplyVisibleState();
    }

    private void OnEnable()
    {
        _lastSelectedState = (TaxMachineScreenState)(-1);
        ApplyVisibleState();
    }

    private void OnValidate()
    {
        alternateSeconds = Mathf.Max(0.1f, alternateSeconds);
        ApplyVisibleState();
    }

    private void Update()
    {
        if (Application.isPlaying)
            ApplyVisibleState();
    }

    private void ApplyVisibleState()
    {
        if (screenRenderer == null)
            return;

        TaxMachineScreenState selectedState = !Application.isPlaying || useManualPreview
            ? manualPreviewState
            : _runtimeState;
        if (selectedState != _lastSelectedState)
        {
            _lastSelectedState = selectedState;
            _selectedStateChangedAt = Time.unscaledTime;
        }

        TaxMachineScreenState visibleState = Application.isPlaying
            ? ResolveAlternatingState(selectedState, Time.unscaledTime - _selectedStateChangedAt, alternateSeconds)
            : selectedState;
        Material material = GetMaterial(visibleState);
        if (material != null && screenRenderer.sharedMaterial != material)
            screenRenderer.sharedMaterial = material;
    }

    public static TaxMachineScreenState ResolveAlternatingState(
        TaxMachineScreenState selectedState,
        float elapsedSeconds,
        float intervalSeconds)
    {
        bool alternatesWithFrown = selectedState == TaxMachineScreenState.Days2
            || selectedState == TaxMachineScreenState.Days1
            || selectedState == TaxMachineScreenState.Pay;
        if (!alternatesWithFrown)
            return selectedState;

        float interval = Mathf.Max(0.1f, intervalSeconds);
        bool showFrown = Mathf.FloorToInt(Mathf.Max(0f, elapsedSeconds) / interval) % 2 == 1;
        return showFrown ? TaxMachineScreenState.Frown : selectedState;
    }

    private Material GetMaterial(TaxMachineScreenState state)
    {
        return state switch
        {
            TaxMachineScreenState.Days2 => days2Material,
            TaxMachineScreenState.Days1 => days1Material,
            TaxMachineScreenState.Pay => payMaterial,
            TaxMachineScreenState.Frown => frownMaterial,
            TaxMachineScreenState.Smile => smileMaterial,
            _ => frownMaterial,
        };
    }
}
