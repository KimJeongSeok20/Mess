using System;
using System.Collections;
using PurrNet;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 상점 구매 아이템의 공통 피드백 경로.
/// - 구매 대기 토큰 관리
/// - PromptPresenter 메시지 공통화
/// - 성공 사운드 / 구매 이펙트 / 디스폰 타이밍 공통화
/// </summary>
public abstract class ShopPurchaseItemBase : Item
{
    private static string _lastPurchaseAudioDebug = "none";

    [Header("Purchase Feedback")]
    [SerializeField] private GameObject purchaseEffectPrefab;
    [SerializeField] private float despawnDelay = 0.3f;
    [SerializeField] private AudioClip buySuccessClip;
    [SerializeField] private AudioClip buyFailClip;
    [SerializeField] private AudioMixerGroup purchaseMixerGroup;

    protected PromptPresenter PromptPresenterInstance { get; private set; }
    protected string PendingPurchaseToken { get; private set; }
    protected CurrencyManager PurchaseCurrencyManager { get; private set; }
    protected InventoryManager PurchaseInventoryManager { get; private set; }
    private AudioSource _purchaseAudioSource;

    protected override void OnSpawned()
    {
        base.OnSpawned();
        TryResolvePurchaseManagers();
    }

    protected bool TryResolvePurchaseManagers()
    {
        bool hasCurrencyManager = TryResolveCurrencyManager();
        bool hasInventoryManager = TryResolveInventoryManager();
        return hasCurrencyManager && hasInventoryManager;
    }

    protected bool TryResolveCurrencyManager()
    {
        if (PurchaseCurrencyManager == null &&
            InstanceHandler.TryGetInstance(out CurrencyManager currencyManager))
        {
            PurchaseCurrencyManager = currencyManager;
        }

        return PurchaseCurrencyManager != null;
    }

    protected bool TryResolveInventoryManager()
    {
        if (PurchaseInventoryManager == null &&
            InstanceHandler.TryGetInstance(out InventoryManager inventoryManager))
        {
            PurchaseInventoryManager = inventoryManager;
        }

        return PurchaseInventoryManager != null;
    }

    protected bool TryBeginPurchaseRequest()
    {
        if (!string.IsNullOrEmpty(PendingPurchaseToken))
            return false;

        PendingPurchaseToken = Guid.NewGuid().ToString("N");
        return true;
    }

    protected bool TryResolvePurchaseToken(string purchaseToken)
    {
        if (PendingPurchaseToken != purchaseToken)
            return false;

        PendingPurchaseToken = null;
        return true;
    }

    protected void CancelPendingPurchaseRequest()
    {
        PendingPurchaseToken = null;
    }

    protected void EnsurePromptPresenter()
    {
        if (PromptPresenterInstance == null)
            PromptPresenterInstance = PromptPresenter.Instance ?? FindObjectOfType<PromptPresenter>();
    }

    protected void ShowInsufficientFunds(int requiredAmount, int currentAmount)
    {
        ShowInsufficientFunds(requiredAmount, currentAmount, playAudio: true);
    }

    protected void ShowInsufficientFunds(int requiredAmount, int currentAmount, bool playAudio)
    {
        ShowPromptMessage($"Need ${requiredAmount} (You have ${currentAmount})");
        if (playAudio)
            PlayFailureAudio();
    }

    protected void ShowPurchaseFailure(string message)
    {
        ShowPurchaseFailure(message, playAudio: true);
    }

    protected void ShowPurchaseFailure(string message, bool playAudio)
    {
        ShowPromptMessage(message);
        if (playAudio)
            PlayFailureAudio();
    }

    protected void ShowPurchaseSuccess(string message)
    {
        ShowPurchaseSuccess(message, playAudio: true);
    }

    protected void ShowPurchaseSuccess(string message, bool playAudio)
    {
        ShowPromptMessage(message);
        if (playAudio)
            PlaySuccessAudio();
    }

    protected void ShowPromptMessage(string message)
    {
        EnsurePromptPresenter();
        if (PromptPresenterInstance != null)
            PromptPresenterInstance.Show(message);
    }

    /// <summary>Server-only latch: set the moment a purchase is accepted so a second request cannot buy the same entry.</summary>
    public bool SoldOnServer { get; set; }

    protected IEnumerator RemoveAfterPurchaseEffect()
    {
        PlayPurchaseEffectLocal();
        yield return new WaitForSeconds(despawnDelay);
        Destroy(gameObject);
    }

    protected virtual void PlaySuccessAudio()
    {
        if (buySuccessClip == null)
            return;

        EnsurePurchaseAudioSource();
        if (_purchaseAudioSource != null)
            _purchaseAudioSource.PlayOneShot(buySuccessClip);
    }

    protected virtual void PlayFailureAudio()
    {
        if (buyFailClip == null)
            return;

        EnsurePurchaseAudioSource();
        if (_purchaseAudioSource != null)
            _purchaseAudioSource.PlayOneShot(buyFailClip);
    }

    public void PlayPurchaseAudioFromNetwork(bool success)
    {
        _lastPurchaseAudioDebug = $"{name}|success={success}|pos={transform.position}";
        Debug.Log($"[ShopPurchaseAudio] {_lastPurchaseAudioDebug}");

        if (success)
            PlaySuccessAudio();
        else
            PlayFailureAudio();
    }

    public static string GetLastPurchaseAudioDebug()
    {
        return _lastPurchaseAudioDebug;
    }

    private void EnsurePurchaseAudioSource()
    {
        if (_purchaseAudioSource != null)
            return;

        _purchaseAudioSource = GetComponent<AudioSource>();
        if (_purchaseAudioSource == null)
            _purchaseAudioSource = gameObject.AddComponent<AudioSource>();

        _purchaseAudioSource.playOnAwake = false;
        _purchaseAudioSource.spatialBlend = 1f;
        _purchaseAudioSource.minDistance = 1f;
        _purchaseAudioSource.maxDistance = 12f;

        if (purchaseMixerGroup != null)
            _purchaseAudioSource.outputAudioMixerGroup = purchaseMixerGroup;
    }

    private void PlayPurchaseEffectLocal()
    {
        if (purchaseEffectPrefab != null)
        {
            GameObject effect = Instantiate(purchaseEffectPrefab, transform.position + Vector3.up * 0.5f, Quaternion.identity);
            Destroy(effect, 3f);
        }

        foreach (var renderer in GetComponentsInChildren<Renderer>())
            renderer.enabled = false;

        foreach (var collider in GetComponentsInChildren<Collider>())
            collider.enabled = false;
    }
}
