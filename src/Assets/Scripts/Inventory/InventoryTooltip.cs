using UnityEngine;
using TMPro;

/// <summary>
/// 인벤토리 아이템 호버 툴팁
/// - 마우스 올리면 아이템 이름, 가격, 강화 정보 표시
/// - InventoryCanvas 프리팹에 고정 배치
/// - InventoryItem.OnPointerEnter/Exit에서 호출
/// </summary>
public class InventoryTooltip : MonoBehaviour
{
    private static InventoryTooltip _instance;

    [Header("References")]
    [SerializeField] private InventoryTheme theme;
    [SerializeField] private RectTransform tooltipRect;
    [SerializeField] private CanvasGroup tooltipCanvasGroup;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text infoText;
    [SerializeField] private Canvas parentCanvas;
    [SerializeField] private Vector2 pointerOffset;

    private bool _isShowing;

    private void Awake()
    {
        _instance = this;

        if (theme != null)
        {
            if (nameText != null)
                nameText.color = theme.textPrimary;
            if (infoText != null)
                infoText.color = theme.textSecondary;
        }

        if (tooltipCanvasGroup != null)
        {
            tooltipCanvasGroup.alpha = 0f;
            tooltipCanvasGroup.blocksRaycasts = false;
            tooltipCanvasGroup.interactable = false;
        }
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        if (_isShowing)
        {
            FollowMouse();
        }
    }

    private void FollowMouse()
    {
        if (parentCanvas == null || tooltipRect == null)
            return;

        Vector2 mousePos = Input.mousePosition;

        // 마우스 오른쪽 위에 표시 (겹치지 않도록)
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentCanvas.transform as RectTransform,
            mousePos + pointerOffset,
            parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera,
            out Vector2 localPoint);

        tooltipRect.localPosition = localPoint;
    }

    // ═══════════ Static API ═══════════

    public static void Show(string displayName, int price, int upgradeTier, Vector2 screenPos)
    {
        if (_instance == null)
            return;

        _instance.ShowInternal(displayName, price, upgradeTier);
    }

    public static void Hide()
    {
        if (_instance == null) return;
        _instance.HideInternal();
    }

    // ═══════════ Internal ═══════════

    private void ShowInternal(string displayName, int price, int upgradeTier)
    {
        if (tooltipCanvasGroup == null)
            return;

        if (nameText != null)
        {
            if (theme != null)
                nameText.color = theme.textPrimary;
            nameText.text = displayName;
        }

        // 정보 텍스트
        string info = $"${price}";
        if (upgradeTier > 0)
        {
            info += theme != null
                ? $"  <color=#{ColorUtility.ToHtmlStringRGB(theme.success)}>Tier +{upgradeTier}</color>"
                : $"  Tier +{upgradeTier}";
        }

        if (infoText != null)
        {
            if (theme != null)
                infoText.color = theme.textSecondary;
            infoText.text = info;
        }

        tooltipCanvasGroup.alpha = 1f;
        _isShowing = true;
    }

    private void HideInternal()
    {
        if (tooltipCanvasGroup != null)
            tooltipCanvasGroup.alpha = 0f;
        _isShowing = false;
    }
}
