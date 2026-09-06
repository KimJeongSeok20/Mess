using UnityEngine;
using TMPro;
using PurrNet;

/// <summary>
/// 공유 자금을 UI에 표시하는 컴포넌트
/// CurrencyManager의 이벤트를 구독하여 실시간 업데이트
/// </summary>
public class CurrencyUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text currencyText;

    [Header("Display Settings")]
    [SerializeField] private string currencyPrefix = "$";
    [SerializeField] private bool useThousandsSeparator = true;

    private void OnEnable()
    {
        // CurrencyManager 이벤트 구독
        CurrencyManager.OnCurrencyChanged += UpdateCurrencyDisplay;
    }

    private void OnDisable()
    {
        // 이벤트 구독 해제 (메모리 누수 방지)
        CurrencyManager.OnCurrencyChanged -= UpdateCurrencyDisplay;
    }

    private void Start()
    {
        // 초기 표시 (CurrencyManager가 있으면)
        if (InstanceHandler.TryGetInstance(out CurrencyManager currencyManager))
        {
            UpdateCurrencyDisplay(currencyManager.SharedCurrency);
        }
        else
        {
            // CurrencyManager가 아직 없으면 0으로 표시
            UpdateCurrencyDisplay(0);
        }
    }

    /// <summary>
    /// 자금 변경 시 UI 업데이트
    /// </summary>
    private void UpdateCurrencyDisplay(int newAmount)
    {
        if (currencyText == null)
        {
            Debug.LogWarning("[CurrencyUI] CurrencyText is not assigned!", this);
            return;
        }

        // 천 단위 구분자 적용
        string formattedAmount = useThousandsSeparator 
            ? newAmount.ToString("N0")  // 1,500
            : newAmount.ToString();      // 1500

        currencyText.text = $"{currencyPrefix}{formattedAmount}";
    }

    #region Debug Methods

    /// <summary>
    /// Inspector에서 수동으로 테스트용
    /// </summary>
    [ContextMenu("Test Update UI (1500)")]
    private void TestUpdateUI()
    {
        UpdateCurrencyDisplay(1500);
    }

    #endregion
}
