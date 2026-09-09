using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Inspector-authored upgrade station: weapon selection, +0 material and Star Catch.</summary>
public class AnvilUI : MonoBehaviour
{
    [Serializable]
    private class WeaponSelector
    {
        public ItemUpgradeRecipe recipe;
        public Button button;
        public Image border;
        public TMP_Text label;
    }

    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private WeaponSelector[] weapons;
    [SerializeField] private Image weaponImage;
    [SerializeField] private TMP_Text weaponName;
    [SerializeField] private TMP_Text currentTier;
    [SerializeField] private TMP_Text nextTier;
    [SerializeField] private TMP_Text damageText;
    [SerializeField] private TMP_Text rpmText;
    [SerializeField] private TMP_Text magazineText;
    [SerializeField] private TMP_Text shotsText;
    [SerializeField] private TMP_Text shotsLabel;
    [SerializeField] private TMP_Text successText;
    [SerializeField] private TMP_Text materialText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text currencyText;
    [SerializeField] private RectTransform currencyIcon;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text milestoneText;
    [SerializeField] private Button upgradeButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject starCatchPanel;
    [SerializeField] private RectTransform starCatchMarker;
    [SerializeField] private TMP_Text starCatchHint;
    [SerializeField] private Button starCatchStop;
    [SerializeField] private Color selectedColor = new Color(0.95f, 0.54f, 0.2f);
    [SerializeField] private Color idleColor = new Color(0.32f, 0.32f, 0.33f, 0.8f);

    private AnvilInteraction _anvil;
    private InventoryManager _inventory;
    private CurrencyManager _currency;
    private UpgradeOption _option;
    private UpgradeOption _attempt;
    private int _selected;
    private bool _isOpen;
    private bool _timing;
    private bool _initialized;
    private float _timingStart;
    private PlayerInput _playerInput;
    private bool _inputWasActive;
    private string _previousActionMap;
    private CursorLockMode _previousCursorLock;
    private bool _previousCursorVisible;
    private bool _controlsCaptured;
    private static AnvilUI _activeUi;
    private static int _closedFrame = -1;
    public static bool BlocksGameplayInput => _activeUi != null || _closedFrame == Time.frameCount;
    public bool IsOpen => _isOpen;

    private void Awake() => InitializeView();

    private void OnRectTransformDimensionsChange() => FitPanel();

    private void FitPanel()
    {
        var parent = transform.parent as RectTransform;
        if (parent == null) return;
        var panel = (RectTransform)transform;
        float scale = Mathf.Clamp(Mathf.Min((parent.rect.width - 48f) / panel.rect.width,
            (parent.rect.height - 48f) / panel.rect.height), 0.05f, 1f);
        panel.localScale = Vector3.one * scale;
    }

    private void InitializeView()
    {
        if (_initialized) return;
        _initialized = true;
        for (int i = 0; i < weapons.Length; i++)
        {
            int index = i;
            weapons[i].button.onClick.AddListener(() => SelectWeapon(index));
        }
        upgradeButton.onClick.AddListener(BeginUpgrade);
        closeButton.onClick.AddListener(Close);
        starCatchStop.onClick.AddListener(StopStarCatch);
    }

    public void Open(AnvilInteraction anvil, InventoryManager inventory, CurrencyManager currency)
    {
        DetachEvents();
        _anvil = anvil;
        _inventory = inventory;
        _currency = currency;
        _activeUi = this;
        _previousCursorLock = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
        _playerInput = inventory.GameplayInput;
        _controlsCaptured = true;
        if (_playerInput != null)
        {
            _inputWasActive = _playerInput.inputIsActive;
            _previousActionMap = _playerInput.currentActionMap?.name;
            _playerInput.DeactivateInput();
            _playerInput.GetComponent<Demo.Scripts.Runtime.Character.FPSController>()?.ClearGameplayInputForModal();
        }
        _anvil.OnStarCatchStarted += StartStarCatch;
        _anvil.OnRollResolved += ShowOutcome;
        _inventory.InventoryChanged += RefreshRecipeList;
        CurrencyManager.OnCurrencyChanged += RefreshCurrency;
        gameObject.SetActive(true);
        FitPanel();
        InitializeView();
        _isOpen = true;
        _timing = false;
        starCatchPanel.SetActive(false);
        statusText.text = string.Empty;
        // Retain the player's selection on reopen; choose an owned weapon on first use.
        if (_inventory.CountItemsByName(weapons[_selected].recipe.baseItemName) == 0)
            for (int i = 0; i < weapons.Length; i++)
                if (_inventory.CountItemsByName(weapons[i].recipe.baseItemName) > 0) { _selected = i; break; }
        RefreshRecipeList();
        panelGroup.alpha = 1f;
        panelGroup.blocksRaycasts = true;
        panelGroup.interactable = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Close()
    {
        _anvil?.CancelStarCatch();
        _timing = false;
        _isOpen = false;
        starCatchPanel.SetActive(false);
        panelGroup.alpha = 0f;
        panelGroup.blocksRaycasts = false;
        panelGroup.interactable = false;
        DetachEvents();
        gameObject.SetActive(false);
        RestoreControls();
    }

    private void OnDisable()
    {
        if (_isOpen) _anvil?.CancelStarCatch();
        _isOpen = false;
        _timing = false;
        DetachEvents();
        RestoreControls();
    }

    private void RestoreControls()
    {
        if (!_controlsCaptured) return;
        _controlsCaptured = false;
        if (_activeUi == this) _activeUi = null;
        _closedFrame = Time.frameCount;
        if (_playerInput != null && _inputWasActive && _playerInput.enabled)
        {
            _playerInput.ActivateInput();
            if (!string.IsNullOrEmpty(_previousActionMap) && _playerInput.currentActionMap?.name != _previousActionMap)
                _playerInput.SwitchCurrentActionMap(_previousActionMap);
        }
        Cursor.lockState = _previousCursorLock;
        Cursor.visible = _previousCursorVisible;
    }

    private void DetachEvents()
    {
        if (_anvil != null)
        {
            _anvil.OnStarCatchStarted -= StartStarCatch;
            _anvil.OnRollResolved -= ShowOutcome;
        }
        if (_inventory != null) _inventory.InventoryChanged -= RefreshRecipeList;
        CurrencyManager.OnCurrencyChanged -= RefreshCurrency;
    }

    private void Update()
    {
        if (!_isOpen) return;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (_timing)
            {
                _timing = false;
                _anvil.CancelStarCatch();
                starCatchPanel.SetActive(false);
                statusText.text = "Cancelled. No items or currency spent.";
                RefreshRecipeList();
            }
            else Close();
            return;
        }
        if (!_timing) return;
        float elapsed = Time.unscaledTime - _timingStart;
        float position = AnvilInteraction.StarCatchPosition(elapsed);
        starCatchMarker.anchorMin = new Vector2(position, 0f);
        starCatchMarker.anchorMax = new Vector2(position, 1f);
        starCatchHint.text = $"SPACE / CLICK TO STOP     {Mathf.Max(0f, AnvilInteraction.StarCatchDuration - elapsed):F1}s";
        if (elapsed >= AnvilInteraction.StarCatchDuration) FinishStarCatch(-1f);
        else if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) StopStarCatch();
    }

    private void SelectWeapon(int index)
    {
        if (_timing || (_anvil != null && _anvil.IsUpgradePending)) return;
        _selected = index;
        statusText.text = string.Empty;
        RefreshRecipeList();
    }

    public void RefreshRecipeList()
    {
        if (_inventory == null || _anvil == null) return;
        var recipe = weapons[_selected].recipe;
        int highest = _inventory.GetHighestUpgradeTier(recipe.baseItemName);
        int baseCount = _inventory.CountItemsWithTier(recipe.baseItemName, 0);
        _option = new UpgradeOption
        {
            recipe = recipe, highestOwnedTier = highest, resultTier = highest + 1,
            materialCount = Mathf.Max(0, baseCount - (highest == 0 ? 1 : 0))
        };
        bool owned = highest >= 0;
        bool maxed = owned && highest >= recipe.EffectiveMaxTier;
        bool busy = _timing || _anvil.IsUpgradePending;
        for (int i = 0; i < weapons.Length; i++)
        {
            bool hasWeapon = _inventory.CountItemsByName(weapons[i].recipe.baseItemName) > 0;
            weapons[i].border.color = i == _selected ? selectedColor : idleColor;
            weapons[i].label.color = i == _selected ? selectedColor : hasWeapon ? Color.white : Color.gray;
            weapons[i].button.interactable = !busy;
        }
        weaponImage.sprite = recipe.GetBaseIcon();
        weaponName.text = recipe.baseItemPrefab.GetComponent<Item>().DisplayName;
        currentTier.text = owned ? $"+{highest}" : "NOT OWNED";
        nextTier.text = maxed ? "MAX" : owned ? $"+{highest + 1}" : "—";
        var data = recipe.GetBaseWeaponData();
        var current = recipe.CalcStats(data, Mathf.Max(0, highest));
        var next = recipe.CalcStats(data, maxed ? highest : Mathf.Max(0, highest + 1));
        damageText.text = Compare(current.damage, next.damage);
        rpmText.text = Compare(current.fireRateRpm, next.fireRateRpm);
        magazineText.text = Compare(current.magazineSize, next.magazineSize);
        bool projectile = data.fireSimulation == WeaponData.FireSimulationType.Projectile;
        shotsLabel.text = projectile ? "BLAST DAMAGE" : "SHOTS / TRIGGER";
        shotsText.text = projectile ? Compare(current.explosionDamage, next.explosionDamage)
            : Compare(Mathf.Max(1, data.pellets) + current.extraShotsPerTrigger, Mathf.Max(1, data.pellets) + next.extraShotsPerTrigger);
        successText.text = maxed || !owned ? "—" : $"{_anvil.PreviewSuccessChance(_option, false):P0}";
        materialText.text = $"{recipe.baseItemName} +0  × 1   /   {_option.materialCount} AVAILABLE";
        materialText.color = _option.materialCount > 0 ? new Color(0.55f, 0.86f, 0.7f) : new Color(1f, 0.55f, 0.4f);
        costText.text = maxed || !owned ? "—" : $"{recipe.GetUpgradeCost(highest + 1):N0}";
        var milestone = recipe.GetMilestoneAt(highest + 1) ?? recipe.GetNextMilestone(highest + 1);
        milestoneText.text = maxed ? "MAXIMUM UPGRADE" : milestone.HasValue ? $"+{milestone.Value.tier}  {milestone.Value.label}" : "ALL PERKS UNLOCKED";
        upgradeButton.interactable = !busy && _anvil.CanUpgrade(_option);
        if (!busy && string.IsNullOrEmpty(statusText.text))
            statusText.text = !owned ? "" : maxed ? "Maximum tier reached."
                : _option.materialCount == 0 ? "Requires one matching unenhanced (+0) weapon."
                : recipe.DowngradesOnFail(highest + 1) && highest > 0 ? "Failure: -1 tier." : "Failure: tier kept.";
        RefreshCurrency(0);
    }

    private static string Compare(int current, int next) => $"{current:N0}  <color=#89DCB3>→  {next:N0}</color>";
    private static string Compare(float current, float next) => $"{current:0.#}  <color=#89DCB3>→  {next:0.#}</color>";
    private void RefreshCurrency(int ignored)
    {
        currencyText.text = _currency != null ? $"{_currency.SharedCurrency:N0}" : "—";
        currencyIcon.anchoredPosition = new Vector2(-currencyText.preferredWidth - 12f, 0f);
    }

    private void BeginUpgrade()
    {
        if (!_anvil.CanUpgrade(_option)) { RefreshRecipeList(); return; }
        _attempt = _option;
        statusText.text = "Preparing Star Catch...";
        if (!_anvil.BeginStarCatch(_attempt)) statusText.text = "Upgrade unavailable. Check the required +0 weapon.";
        RefreshRecipeList();
    }

    private void StartStarCatch()
    {
        if (!_isOpen) { _anvil.CancelStarCatch(); return; }
        _timing = true;
        _timingStart = Time.unscaledTime;
        starCatchPanel.SetActive(true);
        starCatchMarker.anchorMin = new Vector2(0f, 0f);
        starCatchMarker.anchorMax = new Vector2(0f, 1f);
        RefreshRecipeList();
    }

    private void StopStarCatch()
    {
        if (_timing) FinishStarCatch(Time.unscaledTime - _timingStart);
    }

    private void FinishStarCatch(float elapsed)
    {
        _timing = false;
        starCatchPanel.SetActive(false);
        bool hit = AnvilInteraction.IsStarCatchHit(elapsed);
        statusText.text = hit ? "STAR CATCH! +5% success. Upgrading..." : "No timing bonus. Upgrading...";
        if (!_anvil.TryUpgrade(_attempt, elapsed)) statusText.text = "Upgrade cancelled. Check items and team funds.";
        RefreshRecipeList();
    }

    private void ShowOutcome(ItemUpgradeRecipe recipe, int resultTier, AnvilInteraction.RollOutcome outcome, float chance)
    {
        statusText.text = outcome == AnvilInteraction.RollOutcome.Success ? $"SUCCESS — {recipe.GetTierDisplayName(resultTier)}"
            : outcome == AnvilInteraction.RollOutcome.Downgrade ? $"FAILED — {recipe.GetTierDisplayName(Mathf.Max(0, resultTier - 2))}"
            : outcome == AnvilInteraction.RollOutcome.Rejected ? "Upgrade cancelled. Check items and team funds."
            : "FAILED — current tier kept.";
        RefreshRecipeList();
    }
}
