using System.Collections;
using ChocDino.UIFX;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlotVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public enum ProgressionState
    {
        Unlocked,
        Locked
    }

    public enum SlotState
    {
        Empty,
        Filled,
        Hover,
        Selected,
        Locked,
        DropValid,
        DropInvalid
    }

    private enum DropFeedback
    {
        None,
        Valid,
        Invalid
    }

    [Header("References")]
    [SerializeField] private InventoryTheme theme;
    [SerializeField] private Image slotImage;
    [SerializeField] private Image borderImage;
    [SerializeField] private Image dropOverlay;
    [SerializeField] private Image selectionRail;
    [SerializeField] private GameObject lockIconRoot;
    [SerializeField] private GlowFilter glowFilter;

    [Header("Glow")]
    [SerializeField, Range(0f, 1f)] private float selectedGlowStrength = 0.18f;
    [SerializeField, Range(0f, 1f)] private float unlockPulseGlowStrength = 0.32f;

    private bool _isHovered;
    private bool _isSelected;
    private bool _isOccupied;
    private bool _isActionSlot;
    private ProgressionState _progressionState;
    private DropFeedback _dropFeedback;
    private SlotState _currentState = SlotState.Empty;
    private Coroutine _transitionRoutine;

    public SlotState CurrentState => _currentState;
    public bool IsLocked => _progressionState == ProgressionState.Locked;

    private void Awake()
    {
        ApplyResolvedStateImmediate();
    }

    private void OnEnable()
    {
        ApplyResolvedStateImmediate();
    }

    private void OnDisable()
    {
        StopActiveTransition();
        _isHovered = false;
        _dropFeedback = DropFeedback.None;
        transform.localScale = Vector3.one;
    }

    public void Initialize(Image imageRef, bool isActionSlot)
    {
        _isActionSlot = isActionSlot;
        if (imageRef != null)
            slotImage = imageRef;

        _progressionState = ProgressionState.Unlocked;
        _dropFeedback = DropFeedback.None;
        ApplyResolvedStateImmediate();
    }

    public void SetSelected(bool selected)
    {
        if (_progressionState == ProgressionState.Locked || _isSelected == selected)
            return;

        _isSelected = selected;
        RefreshState();
    }

    public void SetOccupied(bool occupied)
    {
        if (_isOccupied == occupied)
            return;

        _isOccupied = occupied;
        RefreshState();
    }

    public void SetProgressionState(ProgressionState state)
    {
        if (_progressionState == state)
        {
            UpdateLockIcon();
            return;
        }

        _progressionState = state;

        if (_progressionState == ProgressionState.Locked)
        {
            _isHovered = false;
            _isSelected = false;
            _isOccupied = false;
        }

        UpdateLockIcon();
        RefreshState();
    }

    public void SetDropFeedback(bool isValid)
    {
        DropFeedback nextFeedback = isValid ? DropFeedback.Valid : DropFeedback.Invalid;
        if (_dropFeedback == nextFeedback)
            return;

        _dropFeedback = nextFeedback;
        RefreshState();
    }

    public void ClearDropFeedback()
    {
        if (_dropFeedback == DropFeedback.None)
            return;

        _dropFeedback = DropFeedback.None;
        RefreshState();
    }

    public void PlayUnlockPulse(float duration)
    {
        if (_progressionState == ProgressionState.Locked || theme == null)
            return;

        StopActiveTransition();
        _currentState = ResolveState();

        if (!isActiveAndEnabled || duration <= Mathf.Epsilon)
        {
            ApplyVisualStyleImmediate(ResolveStyle(_currentState));
            return;
        }

        _transitionRoutine = StartCoroutine(UnlockPulseRoutine(duration));
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_isHovered)
            return;

        _isHovered = true;
        RefreshState();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_isHovered && _dropFeedback == DropFeedback.None)
            return;

        _isHovered = false;
        _dropFeedback = DropFeedback.None;
        RefreshState();
    }

    private void RefreshState()
    {
        SlotState nextState = ResolveState();
        if (_currentState == nextState)
            return;

        _currentState = nextState;
        StopActiveTransition();

        if (theme == null)
        {
            DisableOptionalEffects();
            return;
        }

        VisualStyle targetStyle = ResolveStyle(_currentState);
        float duration = Mathf.Max(0f, theme.slotState);

        if (!isActiveAndEnabled || duration <= Mathf.Epsilon)
        {
            ApplyVisualStyleImmediate(targetStyle);
            return;
        }

        _transitionRoutine = StartCoroutine(TransitionToStyle(targetStyle, duration));
    }

    private void ApplyResolvedStateImmediate()
    {
        StopActiveTransition();
        transform.localScale = Vector3.one;
        _currentState = ResolveState();
        UpdateLockIcon();

        if (theme == null)
        {
            DisableOptionalEffects();
            return;
        }

        ApplyVisualStyleImmediate(ResolveStyle(_currentState));
    }

    private void UpdateLockIcon()
    {
        if (lockIconRoot != null)
            lockIconRoot.SetActive(_progressionState == ProgressionState.Locked);
    }

    private SlotState ResolveState()
    {
        if (_progressionState == ProgressionState.Locked)
            return SlotState.Locked;

        if (_dropFeedback == DropFeedback.Invalid)
            return SlotState.DropInvalid;

        if (_dropFeedback == DropFeedback.Valid)
            return SlotState.DropValid;

        if (_isSelected)
            return SlotState.Selected;

        if (_isHovered)
            return SlotState.Hover;

        return _isOccupied ? SlotState.Filled : SlotState.Empty;
    }

    private VisualStyle ResolveStyle(SlotState state)
    {
        float hoverScale = _isActionSlot ? 1.025f : 1.012f;
        // The approved compact extraction layout uses a precise hairline selection frame.
        // Scaling a selected cell breaks the even 6x4 rhythm and creates a fake glow halo.
        float selectedScale = 1f;

        switch (state)
        {
            case SlotState.Filled:
                return new VisualStyle(theme.surface2, theme.borderSubtle, Color.clear, Color.clear, 0f, 1f);
            case SlotState.Hover:
                return new VisualStyle(theme.surface3, theme.borderStrong, Color.clear,
                    WithAlpha(theme.accent, 0.55f), 0f, hoverScale);
            case SlotState.Selected:
                return new VisualStyle(theme.surface2, theme.accent, Color.clear,
                    theme.accent, selectedGlowStrength, selectedScale);
            // 잠금 슬롯은 채운 사각형이 아니라 거의 사라지는 윤곽으로만 남긴다.
            // 32칸이 전부 잠긴 초기 상태에서 그리드가 "벽"으로 보이지 않게 하기 위함.
            case SlotState.Locked:
                return new VisualStyle(theme.surfaceLocked, theme.borderLocked, Color.clear,
                    Color.clear, 0f, 1f);
            case SlotState.DropValid:
                return new VisualStyle(theme.surface2, theme.success, theme.accentDim,
                    theme.success, 0f, hoverScale);
            case SlotState.DropInvalid:
                return new VisualStyle(theme.surface2, theme.danger, Color.clear,
                    theme.danger, 0f, 1f);
            default:
                return new VisualStyle(theme.surface1, theme.borderSubtle, Color.clear,
                    Color.clear, 0f, 1f);
        }
    }

    private IEnumerator TransitionToStyle(VisualStyle targetStyle, float duration)
    {
        Color startSlotColor = slotImage != null ? slotImage.color : targetStyle.SlotColor;
        Color startBorderColor = borderImage != null ? borderImage.color : targetStyle.BorderColor;
        Color startOverlayColor = dropOverlay != null ? dropOverlay.color : targetStyle.OverlayColor;
        Color startRailColor = selectionRail != null ? selectionRail.color : targetStyle.RailColor;
        float startGlowStrength = glowFilter != null ? glowFilter.Strength : targetStyle.GlowStrength;
        Vector3 startScale = transform.localScale;

        PrepareOptionalEffects(startBorderColor, targetStyle.BorderColor, startOverlayColor, targetStyle.OverlayColor,
            startRailColor, targetStyle.RailColor, startGlowStrength, targetStyle.GlowStrength);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

            transform.localScale = Vector3.LerpUnclamped(startScale,
                Vector3.one * targetStyle.Scale, t);
            SetVisualColors(
                Color.LerpUnclamped(startSlotColor, targetStyle.SlotColor, t),
                Color.LerpUnclamped(startBorderColor, targetStyle.BorderColor, t),
                Color.LerpUnclamped(startOverlayColor, targetStyle.OverlayColor, t),
                Color.LerpUnclamped(startRailColor, targetStyle.RailColor, t),
                Mathf.LerpUnclamped(startGlowStrength, targetStyle.GlowStrength, t));

            yield return null;
        }

        ApplyVisualStyleImmediate(targetStyle);
        _transitionRoutine = null;
    }

    private IEnumerator UnlockPulseRoutine(float duration)
    {
        VisualStyle baseStyle = ResolveStyle(_currentState);
        transform.localScale = Vector3.one;

        if (slotImage != null)
            slotImage.color = baseStyle.SlotColor;

        SetImageImmediate(dropOverlay, baseStyle.OverlayColor);

        Color startBorderColor = borderImage != null ? borderImage.color : baseStyle.BorderColor;
        float startGlowStrength = glowFilter != null ? glowFilter.Strength : baseStyle.GlowStrength;
        Color pulseBorderColor = theme.accent;
        float pulseGlowStrength = Mathf.Max(selectedGlowStrength, unlockPulseGlowStrength);

        PrepareOptionalEffects(startBorderColor, pulseBorderColor, baseStyle.OverlayColor, baseStyle.OverlayColor,
            baseStyle.RailColor, theme.accent, startGlowStrength, pulseGlowStrength);

        float halfDuration = duration * 0.5f;
        float elapsed = 0f;

        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / halfDuration));
            transform.localScale = Vector3.one;
            SetBorderAndGlow(
                Color.LerpUnclamped(startBorderColor, pulseBorderColor, t),
                Mathf.LerpUnclamped(startGlowStrength, pulseGlowStrength, t));
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / halfDuration));
            transform.localScale = Vector3.one;
            SetBorderAndGlow(
                Color.LerpUnclamped(pulseBorderColor, baseStyle.BorderColor, t),
                Mathf.LerpUnclamped(pulseGlowStrength, baseStyle.GlowStrength, t));
            yield return null;
        }

        ApplyVisualStyleImmediate(baseStyle);
        _transitionRoutine = null;
    }

    private void ApplyVisualStyleImmediate(VisualStyle style)
    {
        transform.localScale = Vector3.one;
        transform.localScale = Vector3.one * style.Scale;
        SetVisualColors(style.SlotColor, style.BorderColor, style.OverlayColor, style.RailColor, style.GlowStrength);
        SetImageImmediate(borderImage, style.BorderColor);
        SetImageImmediate(dropOverlay, style.OverlayColor);
        SetImageImmediate(selectionRail, style.RailColor);

        if (glowFilter != null)
        {
            glowFilter.Color = theme.accent;
            glowFilter.Strength = style.GlowStrength;
            glowFilter.enabled = style.GlowStrength > Mathf.Epsilon;
        }
    }

    private void SetVisualColors(
        Color slotColor,
        Color borderColor,
        Color overlayColor,
        Color railColor,
        float glowStrength)
    {
        if (slotImage != null)
            slotImage.color = slotColor;

        if (borderImage != null)
            borderImage.color = borderColor;

        if (dropOverlay != null)
            dropOverlay.color = overlayColor;

        if (selectionRail != null)
            selectionRail.color = railColor;

        if (glowFilter != null)
        {
            glowFilter.Color = theme.accent;
            glowFilter.Strength = glowStrength;
        }
    }

    private void SetBorderAndGlow(Color borderColor, float glowStrength)
    {
        if (borderImage != null)
            borderImage.color = borderColor;

        if (glowFilter != null)
        {
            glowFilter.Color = theme.accent;
            glowFilter.Strength = glowStrength;
        }
    }

    private void PrepareOptionalEffects(
        Color startBorderColor,
        Color targetBorderColor,
        Color startOverlayColor,
        Color targetOverlayColor,
        Color startRailColor,
        Color targetRailColor,
        float startGlowStrength,
        float targetGlowStrength)
    {
        if (borderImage != null)
            borderImage.enabled = startBorderColor.a > Mathf.Epsilon || targetBorderColor.a > Mathf.Epsilon;

        if (dropOverlay != null)
            dropOverlay.enabled = startOverlayColor.a > Mathf.Epsilon || targetOverlayColor.a > Mathf.Epsilon;

        if (selectionRail != null)
            selectionRail.enabled = startRailColor.a > Mathf.Epsilon || targetRailColor.a > Mathf.Epsilon;

        if (glowFilter != null)
            glowFilter.enabled = startGlowStrength > Mathf.Epsilon || targetGlowStrength > Mathf.Epsilon;
    }

    private static void SetImageImmediate(Image image, Color color)
    {
        if (image == null)
            return;

        image.color = color;
        image.enabled = color.a > Mathf.Epsilon;
    }

    private void DisableOptionalEffects()
    {
        if (dropOverlay != null)
            dropOverlay.enabled = false;

        if (selectionRail != null)
            selectionRail.enabled = false;

        if (glowFilter != null)
        {
            glowFilter.Strength = 0f;
            glowFilter.enabled = false;
        }
    }

    private void StopActiveTransition()
    {
        if (_transitionRoutine == null)
            return;

        StopCoroutine(_transitionRoutine);
        _transitionRoutine = null;
    }

    private readonly struct VisualStyle
    {
        public readonly Color SlotColor;
        public readonly Color BorderColor;
        public readonly Color OverlayColor;
        public readonly Color RailColor;
        public readonly float GlowStrength;
        public readonly float Scale;

        public VisualStyle(
            Color slotColor,
            Color borderColor,
            Color overlayColor,
            Color railColor,
            float glowStrength,
            float scale)
        {
            SlotColor = slotColor;
            BorderColor = borderColor;
            OverlayColor = overlayColor;
            RailColor = railColor;
            GlowStrength = glowStrength;
            Scale = scale;
        }
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
