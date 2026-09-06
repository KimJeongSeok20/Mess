using System;
using PurrNet;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class ActionSlot : MonoBehaviour
{
    // ============ 이벤트 시스템 ============
    public static event Action<ActionSlot> OnSlotActivated;
    
    [SerializeField] private int slotIndex;
    [SerializeField] private InventoryTheme theme;
    [SerializeField] private Image slotImage;
    [SerializeField] private Image hotkeyPlate;
    [SerializeField] private TMP_Text hotkeyLabel;

    private Color _originalColor;
    private InventorySlotVisual _slotVisual;
    private bool _isActive;
    public int Index => slotIndex;  // <- 추가


    private void Awake()
    {
        if (slotImage != null)
            _originalColor = slotImage.color;

        ApplyThemeDefaults();
    }

    public void ApplyThemeDefaults()
    {
        _slotVisual = GetComponent<InventorySlotVisual>();
        if (_slotVisual != null)
        {
            _slotVisual.Initialize(slotImage, true);
            _slotVisual.SetOccupied(GetComponent<InventorySlot>()?.isEmpty == false);
        }

        if (hotkeyLabel != null)
        {
            if (theme != null)
                hotkeyLabel.color = theme.textSecondary;

            hotkeyLabel.text = slotIndex.ToString();
            hotkeyLabel.transform.SetAsLastSibling();
        }

        if (hotkeyPlate != null)
            hotkeyPlate.transform.SetAsLastSibling();

        ApplyHotkeyState(_isActive);
    }

    public void ApplyTacticalDefaults()
    {
        ApplyThemeDefaults();
    }

    public void OnActionSlot1(InputValue value)
    {
        if (slotIndex != 1)
            return;
        RequestActiveSlot();
    }

    public void OnActionSlot2(InputValue value)
    {
        if (slotIndex != 2)
            return;
        RequestActiveSlot();
    }
    public void OnActionSlot3(InputValue value)
    {
        if (slotIndex != 3)
            return;
        RequestActiveSlot();
    }
    public void OnActionSlot4(InputValue value)
    {
        if (slotIndex != 4)
            return;
        RequestActiveSlot();
    }

    public void RequestActiveSlot()
    {
        InstanceHandler.GetInstance<InventoryManager>().SetActionSlotActive(this);
    }

    public void ToggleActive(bool toggle)
    {
        _isActive = toggle;

        if (_slotVisual != null)
            _slotVisual.SetSelected(toggle);
        else if (slotImage != null)
            slotImage.color = toggle && theme != null ? theme.accentDim : _originalColor;

        ApplyHotkeyState(toggle);
        
        // 활성화될 때 이벤트 발생
        if (toggle)
        {
            //Seok
            OnSlotActivated?.Invoke(this);
        }

    }

    private void ApplyHotkeyState(bool active)
    {
        if (theme == null)
            return;

        if (hotkeyPlate != null)
            hotkeyPlate.color = active ? theme.accent : theme.surface0;

        if (hotkeyLabel != null)
        {
            hotkeyLabel.color = active ? theme.surface0 : theme.textSecondary;
            hotkeyLabel.transform.SetAsLastSibling();
        }
    }
}
