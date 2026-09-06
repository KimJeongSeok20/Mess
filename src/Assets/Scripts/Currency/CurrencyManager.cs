using UnityEngine;
using PurrNet;
using System;

/// <summary>
/// 공유 자금 관리 시스템 (4명이 하나의 자금 풀 공유)
/// Host가 권한을 가지며 모든 클라이언트에 동기화
/// </summary>
public class CurrencyManager : NetworkBehaviour
{
    [Header("Currency Settings")]
    [SerializeField] private int startingCurrency = 0;

    // 네트워크 동기화되는 공유 자금
    private readonly SyncVar<int> _sharedCurrency = new();

    // 외부 접근용 프로퍼티
    public int SharedCurrency => _sharedCurrency.value;

    // 이벤트: 자금 변경 시 UI 업데이트용
    public static event Action<int> OnCurrencyChanged;

    #region Lifecycle

    private void Awake()
    {
        // 싱글톤 등록
        InstanceHandler.RegisterInstance(this);
    }

    protected override void OnSpawned()
    {
        base.OnSpawned();

        // 서버(Host)에서만 초기화
        if (isServer)
        {
            _sharedCurrency.value = startingCurrency;
            Debug.Log($"[CurrencyManager] Initialized with {startingCurrency} currency");
        }

        // SyncVar 변경 감지 (모든 클라이언트)
        _sharedCurrency.onChanged += OnCurrencyValueChanged;
        
        // 현재 값으로 UI 초기화
        OnCurrencyChanged?.Invoke(_sharedCurrency.value);
    }

    protected override void OnDespawned()
    {
        base.OnDespawned();
        
        // 이벤트 구독 해제
        _sharedCurrency.onChanged -= OnCurrencyValueChanged;
    }

    private void OnDestroy()
    {
        // 싱글톤 해제
        InstanceHandler.UnregisterInstance<CurrencyManager>();
    }

    #endregion

    #region Currency Operations

    /// <summary>
    /// 자금 추가 (아이템 판매 시 사용)
    /// </summary>
    public void AddCurrency(int amount)
    {
        if (amount <= 0)
        {
            Debug.LogWarning($"[CurrencyManager] Cannot add non-positive amount: {amount}");
            return;
        }

        if (isSpawned && !isServer) return;
        if (CanCredit(amount)) _sharedCurrency.value += amount;
    }

    public bool CanCredit(int amount) => amount > 0 && (long)_sharedCurrency.value + amount <= int.MaxValue;

    /// <summary>
    /// 자금 소비 (구매 시 사용)
    /// </summary>
    public bool TrySpendCurrency(int amount)
    {
        if ((isSpawned && !isServer) || amount <= 0 || _sharedCurrency.value < amount) return false;
        _sharedCurrency.value -= amount;
        return true;
    }

    /// <summary>
    /// 서버에서 즉시 자금을 소비한다.
    /// 상점처럼 서버 승인 후 상태를 적용해야 하는 흐름에서 사용한다.
    /// </summary>
    public bool TrySpendCurrencyImmediateOnServer(int amount)
    {
        if (!isServer)
        {
            Debug.LogWarning("[CurrencyManager] TrySpendCurrencyImmediateOnServer called on non-server");
            return false;
        }

        if (amount <= 0)
        {
            Debug.LogWarning($"[CurrencyManager] Cannot spend non-positive amount: {amount}");
            return false;
        }

        if (_sharedCurrency.value < amount)
        {
            Debug.Log($"[CurrencyManager] Not enough currency. Have: {_sharedCurrency.value}, Need: {amount}");
            return false;
        }

        int oldValue = _sharedCurrency.value;
        _sharedCurrency.value -= amount;
        Debug.Log($"[CurrencyManager] Spent {amount} currency immediately on server. {oldValue} → {_sharedCurrency.value}");
        return true;
    }

    /// <summary>
    /// 서버에서 공유 자금을 즉시 특정 값으로 설정한다 (세금 미납 퇴거 등 런 리셋용).
    /// </summary>
    public bool SetCurrencyImmediateOnServer(int value)
    {
        if (!isServer)
        {
            Debug.LogWarning("[CurrencyManager] SetCurrencyImmediateOnServer called on non-server");
            return false;
        }

        int oldValue = _sharedCurrency.value;
        _sharedCurrency.value = Mathf.Max(0, value);
        Debug.Log($"[CurrencyManager] Currency set on server. {oldValue} → {_sharedCurrency.value}");
        return true;
    }

    /// <summary>
    /// 현재 자금으로 구매 가능한지 확인
    /// </summary>
    public bool CanAfford(int amount)
    {
        return _sharedCurrency.value >= amount;
    }

    /// <summary>
    /// 디버그용: 자금 직접 설정 (테스트용)
    /// </summary>
    [ContextMenu("Add 100 Currency (Debug)")]
    public void DebugAddCurrency()
    {
        AddCurrency(100);
    }

    [ContextMenu("Spend 50 Currency (Debug)")]
    public void DebugSpendCurrency()
    {
        TrySpendCurrency(50);
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// SyncVar 값 변경 시 호출 (모든 클라이언트)
    /// </summary>
    private void OnCurrencyValueChanged(int newValue)
    {
        Debug.Log($"[CurrencyManager] Currency changed → {newValue}");
        OnCurrencyChanged?.Invoke(newValue);
    }

    #endregion

    #region Public Getters

    /// <summary>
    /// 현재 자금 반환 (읽기 전용)
    /// </summary>
    public int GetCurrentCurrency()
    {
        return _sharedCurrency.value;
    }

    #endregion
}
