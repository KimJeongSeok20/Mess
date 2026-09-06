using PurrNet;
using UnityEngine;
using UnityEngine.EventSystems;

public class InventorySlot : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    private InventoryItem _item;
    private InventorySlotVisual _visual;

    public bool isEmpty => _item == null;
    public InventoryItem Item => _item;

    private InventorySlotVisual Visual
    {
        get
        {
            if (_visual == null)
                _visual = GetComponent<InventorySlotVisual>();

            return _visual;
        }
    }

    private void Awake()
    {
        _visual = GetComponent<InventorySlotVisual>();
    }

    public void SetItem(InventoryItem item)
    {
        _item = item;

        if (Visual != null)
            Visual.SetOccupied(_item != null);

        if (_item == null)
        {
            RefreshActionSlotPresentation();
            return;
        }

        RectTransform rect = _item.GetComponent<RectTransform>();
        rect.SetParent(transform);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        if (transform is RectTransform slotRect)
            rect.sizeDelta = slotRect.rect.size;
        rect.anchoredPosition = Vector2.zero;
        RefreshActionSlotPresentation();
    }

    private void RefreshActionSlotPresentation()
    {
        ActionSlot actionSlot = GetComponent<ActionSlot>();
        if (actionSlot != null)
            actionSlot.ApplyThemeDefaults();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!TryGetDraggedItem(eventData, out InventoryItem inventoryItem) || Visual == null)
            return;

        Visual.SetDropFeedback(CanAcceptDrop(inventoryItem));
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (Visual != null)
            Visual.ClearDropFeedback();
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (!TryGetDraggedItem(eventData, out InventoryItem inventoryItem))
        {
            if (Visual != null)
                Visual.SetDropFeedback(false);
            return;
        }

        bool canAcceptDrop = CanAcceptDrop(inventoryItem);
        if (Visual != null)
            Visual.SetDropFeedback(canAcceptDrop);

        // 잠긴 슬롯은 invalid 피드백만 받고, 아이템 이동은 수행하지 않는다.
        if (!canAcceptDrop)
            return;

        inventoryItem.SetAvailiable();

        if (!InstanceHandler.TryGetInstance(out InventoryManager inventoryManager))
        {
            Debug.Log("Failed to get inventory manager!");
            if (Visual != null)
                Visual.ClearDropFeedback();
            return;
        }

        inventoryManager.ItemMoved(inventoryItem, this);

        if (Visual != null)
            Visual.ClearDropFeedback();
    }

    private bool CanAcceptDrop(InventoryItem inventoryItem)
    {
        InventorySlotVisual visual = Visual;
        return inventoryItem != null && (visual == null || !visual.IsLocked);
    }

    private static bool TryGetDraggedItem(PointerEventData eventData, out InventoryItem inventoryItem)
    {
        inventoryItem = null;

        if (eventData == null || eventData.pointerDrag == null)
            return false;

        inventoryItem = eventData.pointerDrag.GetComponent<InventoryItem>();
        return inventoryItem != null;
    }
}
