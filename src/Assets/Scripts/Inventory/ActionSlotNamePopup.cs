using UnityEngine;
using TMPro;
using PurrNet;
using System.Collections;

/// <summary>
/// ActionSlot 아이템 교체 시 슬롯 위에 아이템 이름이 떴다 사라지는 팝업
/// - ActionSlot.OnSlotActivated 이벤트 구독
/// - 슬롯 위에 이름 표시 → 1.5초 후 페이드 아웃
/// - InventoryCanvas 프리팹에 고정 배치
/// </summary>
public class ActionSlotNamePopup : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InventoryTheme theme;
    [SerializeField] private TMP_Text popupText;
    [SerializeField] private CanvasGroup popupCanvasGroup;
    [SerializeField] private RectTransform popupRect;
    [SerializeField] private Canvas parentCanvas;

    [Header("Settings")]
    [SerializeField] private bool showOnSlotActivated;
    [SerializeField] private float fadeInDuration = 0.12f;
    [SerializeField] private float displayDuration = 0.85f;
    [SerializeField] private float fadeOutDuration = 0.24f;
    [SerializeField] private float yOffset = 66f;
    [SerializeField] private float floatUpDistance = 10f;

    private Coroutine _activeCoroutine;

    private InventoryManager _inventoryManager;

    private void Awake()
    {
        if (theme != null && popupText != null)
            popupText.color = theme.textPrimary;

        if (popupCanvasGroup != null)
        {
            popupCanvasGroup.alpha = 0f;
            popupCanvasGroup.blocksRaycasts = false;
            popupCanvasGroup.interactable = false;
        }
    }

    private void Start()
    {
        if (!showOnSlotActivated)
            return;

        ActionSlot.OnSlotActivated += HandleSlotActivated;

        if (!InstanceHandler.TryGetInstance(out _inventoryManager))
            _inventoryManager = FindObjectOfType<InventoryManager>();
    }

    private void OnDestroy()
    {
        if (showOnSlotActivated)
            ActionSlot.OnSlotActivated -= HandleSlotActivated;
    }

    private void HandleSlotActivated(ActionSlot actionSlot)
    {
        if (_inventoryManager == null)
        {
            if (!InstanceHandler.TryGetInstance(out _inventoryManager))
                return;
        }

        var inventorySlot = actionSlot.GetComponent<InventorySlot>();
        if (inventorySlot == null || inventorySlot.isEmpty) return;

        // 슬롯의 InventoryItem에서 표시 이름 가져오기
        var invItem = inventorySlot.GetComponentInChildren<InventoryItem>();
        if (invItem == null) return;

        string displayName = invItem.DisplayName;
        if (string.IsNullOrEmpty(displayName)) return;

        // 슬롯 위치 기준으로 팝업 표시
        ShowPopup(displayName, actionSlot.transform as RectTransform);
    }

    private void ShowPopup(string itemName, RectTransform slotRect)
    {
        if (popupText == null || popupCanvasGroup == null || popupRect == null)
            return;

        if (_activeCoroutine != null)
            StopCoroutine(_activeCoroutine);

        if (theme != null)
            popupText.color = theme.textPrimary;

        popupText.text = itemName;
        popupCanvasGroup.alpha = 0f;

        // 슬롯 위에 위치 설정
        if (slotRect != null && parentCanvas != null)
        {
            Vector3 worldPos = slotRect.position;
            popupRect.position = worldPos + Vector3.up * yOffset;
        }

        _activeCoroutine = StartCoroutine(PopupAnimation());
    }

    private IEnumerator PopupAnimation()
    {
        Vector3 startPos = popupRect.position;

        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / Mathf.Max(0.01f, fadeInDuration)));
            popupCanvasGroup.alpha = t;
            popupRect.position = startPos + Vector3.up * Mathf.Lerp(-6f, 0f, t);
            yield return null;
        }

        popupCanvasGroup.alpha = 1f;
        popupRect.position = startPos;

        elapsed = 0f;
        while (elapsed < displayDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeOutDuration;

            popupCanvasGroup.alpha = 1f - t;
            popupRect.position = startPos + Vector3.up * (floatUpDistance * t);

            yield return null;
        }

        popupCanvasGroup.alpha = 0f;
        _activeCoroutine = null;
    }
}
