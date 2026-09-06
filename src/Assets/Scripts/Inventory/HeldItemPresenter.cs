using System.Collections;
using PurrNet;
using UnityEngine;

public sealed class HeldItemPresenter : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Transform heldItemAnchor;
    [SerializeField] private HeldItemVisualCatalog catalog;

    [Header("Transition")]
    [SerializeField, Min(0f)] private float transitionDuration = 0.15f;
    [SerializeField] private Vector3 loweredLocalOffset = new(0f, -0.15f, 0f);

    private readonly SyncVar<string> _netHeldItemName = new(string.Empty, ownerAuth: false);

    private GameObject _currentVisual;
    private Coroutine _transitionRoutine;
    private string _desiredItemName = string.Empty;
    private string _displayedItemName = string.Empty;
    private bool _subscribed;

    public string DisplayedItemName => _displayedItemName;

    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);
        SubscribeToNetworkState();
        ApplyLocalTarget(_netHeldItemName.value);
    }

    public void Show(string itemName)
    {
        string validatedName = ValidateItemName(itemName);
        if (string.IsNullOrEmpty(validatedName))
        {
            Clear();
            return;
        }

        ApplyLocalTarget(validatedName);
        ReplicateTarget(validatedName);
    }

    public void Clear()
    {
        ApplyLocalTarget(string.Empty);
        ReplicateTarget(string.Empty);
    }

    private string ValidateItemName(string itemName)
    {
        if (catalog != null && catalog.TryGet(itemName, out HeldItemVisualCatalog.Entry entry)
            && entry != null && entry.visualPrefab != null)
        {
            return itemName;
        }

        if (!string.IsNullOrWhiteSpace(itemName))
            Debug.LogWarning($"[HeldItemPresenter] No held visual registered for '{itemName}'.", this);

        return string.Empty;
    }

    private void ReplicateTarget(string itemName)
    {
        if (!isSpawned)
            return;

        if (isServer)
            _netHeldItemName.value = itemName;
        else
            SetHeldItemServerRpc(itemName);
    }

    [ServerRpc]
    private void SetHeldItemServerRpc(string itemName)
    {
        _netHeldItemName.value = ValidateItemName(itemName);
    }

    private void SubscribeToNetworkState()
    {
        if (_subscribed)
            return;

        _netHeldItemName.onChanged += HandleNetHeldItemChanged;
        _subscribed = true;
    }

    private void HandleNetHeldItemChanged(string itemName)
    {
        ApplyLocalTarget(ValidateItemName(itemName));
    }

    private void ApplyLocalTarget(string itemName)
    {
        _desiredItemName = itemName ?? string.Empty;

        if (_transitionRoutine == null && _desiredItemName != _displayedItemName)
            _transitionRoutine = StartCoroutine(TransitionLoop());
    }

    private IEnumerator TransitionLoop()
    {
        while (_desiredItemName != _displayedItemName)
        {
            string targetName = _desiredItemName;
            float halfDuration = transitionDuration * 0.5f;

            if (_currentVisual != null)
            {
                Vector3 shownPosition = _currentVisual.transform.localPosition;
                yield return AnimatePosition(_currentVisual.transform, shownPosition,
                    shownPosition + loweredLocalOffset, halfDuration);

                Destroy(_currentVisual);
                _currentVisual = null;
            }

            _displayedItemName = string.Empty;

            if (!string.IsNullOrEmpty(targetName) && TrySpawnVisual(targetName, out GameObject visual))
            {
                _currentVisual = visual;
                _displayedItemName = targetName;

                Vector3 shownPosition = visual.transform.localPosition;
                visual.transform.localPosition = shownPosition + loweredLocalOffset;
                yield return AnimatePosition(visual.transform, visual.transform.localPosition,
                    shownPosition, halfDuration);
            }

            // A newer slot request may have arrived while the old item was moving.
            if (_desiredItemName == targetName && _displayedItemName != targetName)
                _desiredItemName = string.Empty;
        }

        _transitionRoutine = null;
    }

    private bool TrySpawnVisual(string itemName, out GameObject visual)
    {
        visual = null;

        if (heldItemAnchor == null)
        {
            Debug.LogError("[HeldItemPresenter] Held item anchor is not assigned.", this);
            return false;
        }

        if (catalog == null || !catalog.TryGet(itemName, out HeldItemVisualCatalog.Entry entry)
            || entry == null || entry.visualPrefab == null)
        {
            Debug.LogWarning($"[HeldItemPresenter] Cannot spawn held visual for '{itemName}'.", this);
            return false;
        }

        GameObject pivot = new($"Held_{itemName}");
        pivot.transform.SetParent(heldItemAnchor, false);

        GameObject instance = Instantiate(entry.visualPrefab, pivot.transform, false);
        instance.name = "Visual";
        HeldItemVisualPlacement.Apply(pivot.transform, instance.transform, entry);

        if (!HeldItemVisualPlacement.TryGetWorldRendererBounds(instance.transform, out _))
            Debug.LogWarning($"[HeldItemPresenter] '{instance.name}' has no visible renderer bounds.", this);

        visual = pivot;
        return true;
    }

    private static IEnumerator AnimatePosition(Transform target, Vector3 from, Vector3 to, float duration)
    {
        if (target == null)
            yield break;

        if (duration <= 0f)
        {
            target.localPosition = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration && target != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);
            target.localPosition = Vector3.LerpUnclamped(from, to, t);
            yield return null;
        }

        if (target != null)
            target.localPosition = to;
    }

    private void OnDestroy()
    {
        if (_subscribed)
            _netHeldItemName.onChanged -= HandleNetHeldItemChanged;
    }
}
