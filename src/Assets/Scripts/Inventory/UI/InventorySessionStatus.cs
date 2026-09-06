using TMPro;
using UnityEngine;

public class InventorySessionStatus : MonoBehaviour
{
    private const float RefreshInterval = 0.25f;

    [SerializeField] private CanvasGroup inventoryCanvasGroup;
    [SerializeField] private SevenSegmentClockGraphic clockDisplay;
    [SerializeField] private TMP_Text civicRankText;

    private float _nextRefreshTime;

    private void OnEnable()
    {
        Refresh();
        _nextRefreshTime = Time.unscaledTime + RefreshInterval;
    }

    private void Update()
    {
        if (inventoryCanvasGroup == null || inventoryCanvasGroup.alpha <= 0f)
        {
            _nextRefreshTime = 0f;
            return;
        }

        if (Time.unscaledTime < _nextRefreshTime)
            return;

        Refresh();
        _nextRefreshTime = Time.unscaledTime + RefreshInterval;
    }

    public void Refresh()
    {
        if (civicRankText != null)
            civicRankText.text = TeamProgress.CivicRank.ToString();

        if (clockDisplay == null)
            return;

        TimeManager timeManager = TimeManager.Active;
        if (timeManager == null)
        {
            clockDisplay.SetText($"{TimeManager.CurrentDay:00} --:--");
            return;
        }

        float hours = timeManager.GetCurrentTime();
        if (float.IsNaN(hours) || float.IsInfinity(hours))
        {
            clockDisplay.SetText($"{TimeManager.CurrentDay:00} --:--");
            return;
        }

        int totalMinutes = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp(hours, 0f, 24f) * 60f), 0, 1439);
        clockDisplay.SetText($"{TimeManager.CurrentDay:00} {totalMinutes / 60:00}:{totalMinutes % 60:00}");
    }
}
