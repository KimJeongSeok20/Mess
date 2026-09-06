using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Small menu cues, with one Inspector-assigned shared 2D source per menu.</summary>
[DisallowMultipleComponent]
public sealed class SelectableFeedbackAudio : MonoBehaviour, IPointerEnterHandler, ISelectHandler
{
    [SerializeField] private AudioSource source;
    [SerializeField] private AudioClip focusClip;
    [SerializeField] private AudioClip clickClip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.3f;
    private UnityEngine.UI.Selectable _selectable;
    private UnityEngine.UI.Button _button;
    private UnityEngine.UI.Toggle _toggle;
    private UnityEngine.UI.Slider _slider;
    private float _nextFocus;
    private float _nextChange;

    private void Awake()
    {
        _selectable = GetComponent<UnityEngine.UI.Selectable>();
        _button = GetComponent<UnityEngine.UI.Button>();
        _toggle = GetComponent<UnityEngine.UI.Toggle>();
        _slider = GetComponent<UnityEngine.UI.Slider>();
        if (_button != null) _button.onClick.AddListener(PlayClick);
        if (_toggle != null) _toggle.onValueChanged.AddListener(OnToggle);
        if (_slider != null) _slider.onValueChanged.AddListener(OnSlider);
    }

    public void OnPointerEnter(PointerEventData eventData) => PlayFocus();
    public void OnSelect(BaseEventData eventData) => PlayFocus();

    private void PlayFocus()
    {
        if (_selectable == null || !_selectable.IsInteractable() || Time.unscaledTime < _nextFocus) return;
        _nextFocus = Time.unscaledTime + 0.15f;
        if (source != null && focusClip != null) source.PlayOneShot(focusClip, volume * 0.45f);
    }

    private void PlayClick()
    {
        if (source != null && clickClip != null) source.PlayOneShot(clickClip, volume);
    }

    private void OnToggle(bool value) => PlayValueChange();
    private void OnSlider(float value) => PlayValueChange();

    private void PlayValueChange()
    {
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject != gameObject ||
            Time.unscaledTime < _nextChange) return;
        _nextChange = Time.unscaledTime + 0.15f;
        PlayClick();
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(PlayClick);
        if (_toggle != null) _toggle.onValueChanged.RemoveListener(OnToggle);
        if (_slider != null) _slider.onValueChanged.RemoveListener(OnSlider);
    }
}
