using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 대장간 강화 UI
/// - 인벤토리를 스캔해 강화 가능한 옵션만 표시
/// - 레이아웃: [최고등급 박스] + [재료 박스] +N => [결과 박스]
/// - 하단에 스탯 비교 표시 (데미지, 연사속도)
/// - 전체 행 클릭으로 강화 실행
/// - 마우스 휠 스크롤 / ESC 닫기
/// </summary>
public class AnvilUI : MonoBehaviour
{
    [Header("UI References (Optional — 없으면 자동 생성)")]
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private RectTransform contentParent;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Button closeButton;
    [SerializeField] private ScrollRect scrollRect;

    [Header("Style")]
    [SerializeField] private Color panelColor = new Color(0.03f, 0.06f, 0.1f, 0.92f);
    [SerializeField] private Color rowColor = new Color(0.06f, 0.1f, 0.14f, 0.85f);
    [SerializeField] private Color rowHoverColor = new Color(0.1f, 0.18f, 0.22f, 0.9f);
    [SerializeField] private Color itemBoxColor = new Color(0.04f, 0.08f, 0.12f, 0.95f);
    [SerializeField] private Color itemBoxBorderColor = new Color(0.35f, 0.55f, 0.65f, 0.5f);
    [SerializeField] private Color resultBoxColor = new Color(0.08f, 0.14f, 0.08f, 0.95f);
    [SerializeField] private Color resultBoxBorderColor = new Color(0.7f, 0.6f, 0.25f, 0.6f);
    [SerializeField] private Color statUpColor = new Color(0.3f, 1f, 0.4f, 0.95f);

    private AnvilInteraction _anvil;
    private InventoryManager _inventoryManager;
    private CurrencyManager _currencyManager;
    private bool _isOpen;

    private readonly List<GameObject> _recipeRows = new();

    public bool IsOpen => _isOpen;

    #region Open / Close

    public void Open(AnvilInteraction anvil, InventoryManager inventoryManager, CurrencyManager currencyManager)
    {
        _anvil = anvil;
        _inventoryManager = inventoryManager;
        _currencyManager = currencyManager;

        EnsureUIStructure();
        gameObject.SetActive(true);

        RefreshRecipeList();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (panelGroup != null)
        {
            panelGroup.alpha = 1f;
            panelGroup.blocksRaycasts = true;
            panelGroup.interactable = true;
        }

        _isOpen = true;
    }

    public void Close()
    {
        _isOpen = false;

        if (panelGroup != null)
        {
            panelGroup.alpha = 0f;
            panelGroup.blocksRaycasts = false;
            panelGroup.interactable = false;
        }

        gameObject.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    #endregion

    #region Update

    private void Update()
    {
        if (!_isOpen) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            Close();
    }

    #endregion

    #region Recipe List

    public void RefreshRecipeList()
    {
        foreach (var row in _recipeRows)
        {
            if (row != null) Destroy(row);
        }
        _recipeRows.Clear();

        if (_anvil == null || _inventoryManager == null) return;

        var options = _anvil.DetectAvailableUpgrades();

        if (options.Count == 0)
        {
            CreateInfoRow("No upgrades available.");
            return;
        }

        foreach (var option in options)
        {
            CreateRecipeRow(option);
        }
    }

    /// <summary>
    /// 레시피 행 생성
    /// ┌─────────────────────────────────────────────────────────┐
    /// │ [■Main]  +  [■Material] +N  =>  [■Result]             │
    /// │ AK +5      AK               =>  AK +6                 │
    /// │          DMG: 55 → 60  |  RPM: 900 → 945              │
    /// └─────────────────────────────────────────────────────────┘
    /// </summary>
    private void CreateRecipeRow(UpgradeOption option)
    {
        var recipe = option.recipe;

        // --- 행 컨테이너 ---
        var row = CreateUIObject("RecipeRow", contentParent);
        _recipeRows.Add(row);

        var rowRect = row.GetComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(0f, 300f); // 스탯 줄 추가로 높이 증가

        var rowImage = row.AddComponent<Image>();
        rowImage.color = rowColor;
        rowImage.raycastTarget = true;

        var rowOutline = row.AddComponent<Outline>();
        rowOutline.effectColor = new Color(0.4f, 0.6f, 0.7f, 0.3f);
        rowOutline.effectDistance = new Vector2(1.5f, -1.5f);

        // 전체 행 버튼
        var rowBtn = row.AddComponent<Button>();
        rowBtn.targetGraphic = rowImage;

        var colors = rowBtn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        rowBtn.colors = colors;

        var capturedOption = option;
        rowBtn.onClick.AddListener(() => OnRowClicked(capturedOption));

        // 행 내부: 세로 레이아웃 (아이템행 + 스탯행)
        var rowVertLayout = row.AddComponent<VerticalLayoutGroup>();
        rowVertLayout.spacing = 4f;
        rowVertLayout.padding = new RectOffset(8, 8, 4, 4);
        rowVertLayout.childAlignment = TextAnchor.MiddleCenter;
        rowVertLayout.childControlWidth = true;
        rowVertLayout.childControlHeight = false;
        rowVertLayout.childForceExpandWidth = true;
        rowVertLayout.childForceExpandHeight = false;

        // === 아이템 행 ===
        var itemRow = CreateUIObject("ItemRow", row.transform);
        var itemRowRect = itemRow.GetComponent<RectTransform>();
        itemRowRect.sizeDelta = new Vector2(0f, 240f);

        var itemRowLayout = itemRow.AddComponent<HorizontalLayoutGroup>();
        itemRowLayout.spacing = 10f;
        itemRowLayout.padding = new RectOffset(8, 8, 4, 4);
        itemRowLayout.childAlignment = TextAnchor.MiddleCenter;
        itemRowLayout.childControlWidth = false;
        itemRowLayout.childControlHeight = false;
        itemRowLayout.childForceExpandWidth = false;

        // 1. 메인 아이템 박스 (최고 등급)
        string mainName = recipe.GetTierDisplayName(option.highestOwnedTier);
        Sprite baseIcon = recipe.GetBaseIcon();
        CreateItemBox(itemRow.transform, baseIcon, mainName, itemBoxColor, itemBoxBorderColor);

        // 2. "+" 구분자
        CreateSeparatorText(itemRow.transform, "+");

        // 3. 재료 박스 + 개수 (클릭하면 재료 사용 토글 — 성공률 보너스)
        string matName = option.materialCount > 0 ? recipe.baseItemName : "no material";
        int materialBoxChildIndex = itemRow.transform.childCount;
        CreateMaterialBoxWithCount(itemRow.transform, baseIcon, matName, option.materialCount);
        if (option.materialCount > 0 && itemRow.transform.childCount > materialBoxChildIndex)
        {
            var materialBox = itemRow.transform.GetChild(materialBoxChildIndex).gameObject;
            var materialBtn = materialBox.GetComponent<Button>() ?? materialBox.AddComponent<Button>();
            var capturedForMaterial = option;
            materialBtn.onClick.AddListener(() => ToggleMaterial(capturedForMaterial));
            var materialOutline = materialBox.GetComponent<Outline>() ?? materialBox.AddComponent<Outline>();
            materialOutline.effectColor = UseMaterialFor(option) ? new Color(1f, 0.82f, 0.4f, 0.9f) : new Color(0f, 0f, 0f, 0f);
            materialOutline.effectDistance = new Vector2(2f, -2f);
        }

        // 4. "=>" 구분자
        CreateSeparatorText(itemRow.transform, "=>");

        // 5. 결과 박스
        string resultName = recipe.GetTierDisplayName(option.resultTier);
        CreateItemBox(itemRow.transform, baseIcon, resultName, resultBoxColor, resultBoxBorderColor);

        // === 스탯 비교 행 ===
        CreateStatComparisonRow(row.transform, option);
    }

    /// <summary>
    /// 스탯 비교 행: DMG: 25 → 28  |  RPM: 600 → 630
    /// </summary>
    private void CreateStatComparisonRow(Transform parent, UpgradeOption option)
    {
        var recipe = option.recipe;
        var weaponData = recipe.GetBaseWeaponData();
        if (weaponData == null) return;

        var statRow = CreateUIObject("StatRow", parent);
        var statRowRect = statRow.GetComponent<RectTransform>();
        statRowRect.sizeDelta = new Vector2(0f, 36f);

        var statLayout = statRow.AddComponent<HorizontalLayoutGroup>();
        statLayout.spacing = 20f;
        statLayout.padding = new RectOffset(40, 40, 0, 0);
        statLayout.childAlignment = TextAnchor.MiddleCenter;
        statLayout.childControlWidth = false;
        statLayout.childControlHeight = true;
        statLayout.childForceExpandWidth = false;

        // 데미지 비교
        int curDmg = recipe.CalcDamage(weaponData.damage, option.highestOwnedTier);
        int resDmg = recipe.CalcDamage(weaponData.damage, option.resultTier);
        CreateStatText(statRow.transform, "DMG", curDmg.ToString(), resDmg.ToString());

        // 구분선
        CreateStatSeparator(statRow.transform);

        // 연사속도 비교
        float curRpm = recipe.CalcFireRate(weaponData.serverFireRateRpm, option.highestOwnedTier);
        float resRpm = recipe.CalcFireRate(weaponData.serverFireRateRpm, option.resultTier);
        CreateStatText(statRow.transform, "RPM", curRpm.ToString("F0"), resRpm.ToString("F0"));

        // 탄창 / 발당 총알 수 (강화 프로필 확장)
        var curStats = recipe.CalcStats(weaponData, option.highestOwnedTier);
        var resStats = recipe.CalcStats(weaponData, option.resultTier);

        var statRow2 = CreateUIObject("StatRow2", parent);
        statRow2.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 36f);
        var statLayout2 = statRow2.AddComponent<HorizontalLayoutGroup>();
        statLayout2.spacing = 20f;
        statLayout2.padding = new RectOffset(40, 40, 0, 0);
        statLayout2.childAlignment = TextAnchor.MiddleCenter;
        statLayout2.childControlWidth = false;
        statLayout2.childControlHeight = true;
        statLayout2.childForceExpandWidth = false;

        CreateStatText(statRow2.transform, "MAG", curStats.magazineSize.ToString(), resStats.magazineSize.ToString());
        CreateStatSeparator(statRow2.transform);
        int curShots = Mathf.Max(1, weaponData.pellets) + curStats.extraShotsPerTrigger;
        int resShots = Mathf.Max(1, weaponData.pellets) + resStats.extraShotsPerTrigger;
        CreateStatText(statRow2.transform, "SHOTS", curShots.ToString(), resShots.ToString());

        // 확률 / 실패 결과 / 비용 (스타포스식)
        bool useMaterial = UseMaterialFor(option);
        float chance = _anvil != null ? _anvil.PreviewSuccessChance(option, useMaterial) : recipe.GetBaseSuccessChance(option.resultTier);
        string failText = recipe.DowngradesOnFail(option.resultTier) && option.highestOwnedTier > 0
            ? $"<color=#FF6B6B>fail: -1</color>"
            : "<color=#8899AA>fail: keep</color>";
        string materialText = option.materialCount > 0
            ? (useMaterial
                ? $"  <color=#FFD166>[material ON +{recipe.materialSuccessBonus:P0}]</color>"
                : "  <color=#8899AA>[click material box to add +" + recipe.materialSuccessBonus.ToString("P0") + "]</color>")
            : string.Empty;

        var rollObj = CreateUIObject("RollRow", parent);
        rollObj.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 26f);
        var rollTmp = rollObj.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
            rollTmp.font = TMP_Settings.defaultFontAsset;
        rollTmp.text = $"<color=#66FFAA>{chance:P0}</color> success · {failText} · ${recipe.GetUpgradeCost(option.resultTier):N0}{materialText}";
        rollTmp.fontSize = 17f;
        rollTmp.fontStyle = FontStyles.Bold;
        rollTmp.alignment = TextAlignmentOptions.Center;

        // 이번 강화로 열리는 특성 / 다음 특성 예고
        var unlocked = recipe.GetMilestoneAt(option.resultTier);
        var next = recipe.GetNextMilestone(option.resultTier);
        string perkText = unlocked.HasValue
            ? $"<color=#FFD166>★ {unlocked.Value.label}</color> unlocks at +{unlocked.Value.tier}"
            : next.HasValue
                ? $"<color=#8899AA>Next perk:</color> {next.Value.label} at +{next.Value.tier}"
                : string.Empty;

        if (!string.IsNullOrEmpty(perkText))
        {
            var perkObj = CreateUIObject("PerkRow", parent);
            perkObj.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 26f);
            var perkTmp = perkObj.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                perkTmp.font = TMP_Settings.defaultFontAsset;
            perkTmp.text = perkText;
            perkTmp.fontSize = 16f;
            perkTmp.alignment = TextAlignmentOptions.Center;
        }
    }

    /// <summary>
    /// 스탯 텍스트: "DMG: 25 → 28"
    /// </summary>
    private void CreateStatText(Transform parent, string label, string current, string result)
    {
        var obj = CreateUIObject($"Stat_{label}", parent);
        var rect = obj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(240f, 30f);

        var tmp = obj.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = $"<color=#8899AA>{label}:</color> {current} <color=#66FFAA>→ {result}</color>";
        tmp.fontSize = 18f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.richText = true;
        tmp.enableWordWrapping = false;
    }

    /// <summary>
    /// 스탯 구분선 "|"
    /// </summary>
    private void CreateStatSeparator(Transform parent)
    {
        var obj = CreateUIObject("StatSep", parent);
        var rect = obj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(20f, 30f);

        var tmp = obj.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = "|";
        tmp.fontSize = 16f;
        tmp.color = new Color(0.5f, 0.6f, 0.65f, 0.5f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
    }

    /// <summary>
    /// 아이템 네모칸: 상단 아이콘, 하단 이름
    /// </summary>
    private void CreateItemBox(Transform parent, Sprite icon, string itemName,
        Color bgColor, Color borderColor)
    {
        var box = CreateUIObject("ItemBox", parent);
        var boxRect = box.GetComponent<RectTransform>();
        boxRect.sizeDelta = new Vector2(170f, 210f);

        var boxBg = box.AddComponent<Image>();
        boxBg.color = bgColor;
        boxBg.raycastTarget = false;

        var outline = box.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(2f, -2f);

        var layout = box.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.padding = new RectOffset(8, 8, 12, 8);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // 아이콘
        CreateIconElement(box.transform, icon, 120f);

        // 이름
        var nameText = CreateTextElement(box.transform, itemName, 155f, 18f,
            TextAlignmentOptions.Center);
        if (nameText != null)
            nameText.enableWordWrapping = false;
    }

    /// <summary>
    /// 재료 박스 + 보유 개수 (+N) 표시
    /// </summary>
    private void CreateMaterialBoxWithCount(Transform parent, Sprite icon,
        string itemName, int count)
    {
        var wrapper = CreateUIObject("MaterialWrapper", parent);
        var wrapperRect = wrapper.GetComponent<RectTransform>();
        wrapperRect.sizeDelta = new Vector2(220f, 210f);

        var wrapperLayout = wrapper.AddComponent<HorizontalLayoutGroup>();
        wrapperLayout.spacing = 6f;
        wrapperLayout.childAlignment = TextAnchor.MiddleLeft;
        wrapperLayout.childControlWidth = false;
        wrapperLayout.childControlHeight = false;
        wrapperLayout.childForceExpandWidth = false;

        // 재료 아이템 박스
        CreateItemBox(wrapper.transform, icon, itemName, itemBoxColor, itemBoxBorderColor);

        // "+N" 텍스트
        var countObj = CreateUIObject("Count", wrapper.transform);
        var countRect = countObj.GetComponent<RectTransform>();
        countRect.sizeDelta = new Vector2(54f, 210f);

        var countTmp = countObj.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
            countTmp.font = TMP_Settings.defaultFontAsset;
        countTmp.text = $"+{count}";
        countTmp.fontSize = 48f;
        countTmp.fontStyle = FontStyles.Bold;
        countTmp.color = new Color(0.95f, 0.85f, 0.3f, 1f);
        countTmp.alignment = TextAlignmentOptions.Center;
        countTmp.raycastTarget = false;
    }

    /// <summary>
    /// 구분자 텍스트 (+, =>)
    /// </summary>
    private void CreateSeparatorText(Transform parent, string text)
    {
        var sepObj = CreateUIObject("Sep", parent);
        var sepRect = sepObj.GetComponent<RectTransform>();
        sepRect.sizeDelta = new Vector2(70f, 210f);

        var tmp = sepObj.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = 40f;
        tmp.enableWordWrapping = false;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = new Color(0.85f, 0.9f, 0.95f, 0.9f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
    }

    private void CreateInfoRow(string message)
    {
        var row = CreateUIObject("InfoRow", contentParent);
        _recipeRows.Add(row);

        var rowRect = row.GetComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(0f, 60f);

        var text = row.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
        text.text = message;
        text.fontSize = 16f;
        text.color = new Color(0.6f, 0.7f, 0.75f, 0.8f);
        text.alignment = TextAlignmentOptions.Center;
    }

    #endregion

    #region Upgrade Action

    private readonly System.Collections.Generic.HashSet<string> _useMaterialFor = new();

    private bool UseMaterialFor(UpgradeOption option)
    {
        return option.materialCount > 0 && option.recipe != null && _useMaterialFor.Contains(option.recipe.baseItemName);
    }

    private void ToggleMaterial(UpgradeOption option)
    {
        if (option.recipe == null) return;
        string key = option.recipe.baseItemName;
        if (!_useMaterialFor.Remove(key))
            _useMaterialFor.Add(key);
        RefreshRecipeList();
    }

    private void OnRowClicked(UpgradeOption option)
    {
        if (_anvil == null) return;

        bool success = _anvil.TryUpgrade(option, UseMaterialFor(option));

        if (success)
        {
            Debug.Log($"[AnvilUI] Upgrade successful: " +
                $"{option.recipe.GetTierDisplayName(option.resultTier)}");
        }

        RefreshRecipeList();
    }

    #endregion

    #region UI Helpers

    private GameObject CreateUIObject(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj;
    }

    private GameObject CreateIconElement(Transform parent, Sprite sprite, float size)
    {
        var iconObj = CreateUIObject("Icon", parent);
        var iconRect = iconObj.GetComponent<RectTransform>();
        iconRect.sizeDelta = new Vector2(size, size);

        var iconImage = iconObj.AddComponent<Image>();
        iconImage.sprite = sprite;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        if (sprite == null)
            iconImage.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);

        return iconObj;
    }

    private TMP_Text CreateTextElement(Transform parent, string text, float width,
        float fontSize, TextAlignmentOptions alignment)
    {
        var textObj = CreateUIObject("Text", parent);
        var textRect = textObj.GetComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(width, 24f);

        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = new Color(0.85f, 0.9f, 0.95f, 0.95f);
        tmp.alignment = alignment;
        tmp.raycastTarget = false;
        tmp.richText = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;

        return tmp;
    }

    /// <summary>
    /// Inspector 참조 없으면 기본 구조를 코드로 생성
    /// </summary>
    private void EnsureUIStructure()
    {
        // CanvasGroup
        if (panelGroup == null)
            panelGroup = GetComponent<CanvasGroup>();
        if (panelGroup == null)
            panelGroup = gameObject.AddComponent<CanvasGroup>();

        // 배경
        var bgImage = GetComponent<Image>();
        if (bgImage == null)
            bgImage = gameObject.AddComponent<Image>();
        bgImage.color = panelColor;
        bgImage.raycastTarget = true;

        // 패널 크기
        var rt = GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.12f, 0.15f);
            rt.anchorMax = new Vector2(0.88f, 0.85f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // --- 타이틀 ---
        if (titleText == null)
        {
            var existing = transform.Find("AnvilTitle");
            if (existing != null)
                titleText = existing.GetComponent<TMP_Text>();
        }

        if (titleText == null)
        {
            var titleObj = CreateUIObject("AnvilTitle", transform);
            var titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -8f);
            titleRect.sizeDelta = new Vector2(0f, 48f);

            titleText = titleObj.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                titleText.font = TMP_Settings.defaultFontAsset;
            titleText.fontSize = 26f;
            titleText.fontStyle = FontStyles.Bold;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = new Color(0.9f, 0.95f, 1f, 0.95f);
            titleText.raycastTarget = false;
        }
        titleText.text = "UPGRADE STATION";

        // --- ScrollRect ---
        if (contentParent == null)
        {
            var existingScroll = transform.Find("ScrollView");
            if (existingScroll != null)
            {
                scrollRect = existingScroll.GetComponent<ScrollRect>();
                var existingContent = existingScroll.Find("Viewport/Content");
                if (existingContent != null)
                    contentParent = existingContent as RectTransform;
            }
        }

        if (contentParent == null)
        {
            var scrollObj = CreateUIObject("ScrollView", transform);
            var scrollRt = scrollObj.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.03f, 0.06f);
            scrollRt.anchorMax = new Vector2(0.97f, 0.86f);
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;

            scrollRect = scrollObj.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 40f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            // Viewport
            var viewportObj = CreateUIObject("Viewport", scrollObj.transform);
            var viewportRt = viewportObj.GetComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;

            var viewportImage = viewportObj.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewportObj.AddComponent<RectMask2D>();

            // Content
            var contentObj = CreateUIObject("Content", viewportObj.transform);
            contentParent = contentObj.GetComponent<RectTransform>();
            contentParent.anchorMin = new Vector2(0f, 1f);
            contentParent.anchorMax = new Vector2(1f, 1f);
            contentParent.pivot = new Vector2(0.5f, 1f);
            contentParent.sizeDelta = new Vector2(0f, 0f);

            var vertLayout = contentObj.AddComponent<VerticalLayoutGroup>();
            vertLayout.spacing = 10f;
            vertLayout.padding = new RectOffset(10, 10, 10, 10);
            vertLayout.childControlWidth = true;
            vertLayout.childControlHeight = false;
            vertLayout.childForceExpandWidth = true;
            vertLayout.childForceExpandHeight = false;

            var fitter = contentObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewportRt;
            scrollRect.content = contentParent;
        }

        // --- 닫기 버튼 ---
        if (closeButton == null)
        {
            var existing = transform.Find("CloseButton");
            if (existing != null)
                closeButton = existing.GetComponent<Button>();
        }

        if (closeButton == null)
        {
            var closeBtnObj = CreateUIObject("CloseButton", transform);
            var closeBtnRect = closeBtnObj.GetComponent<RectTransform>();
            closeBtnRect.anchorMin = new Vector2(1f, 1f);
            closeBtnRect.anchorMax = new Vector2(1f, 1f);
            closeBtnRect.pivot = new Vector2(1f, 1f);
            closeBtnRect.anchoredPosition = new Vector2(-10f, -10f);
            closeBtnRect.sizeDelta = new Vector2(36f, 36f);

            var closeBtnImage = closeBtnObj.AddComponent<Image>();
            closeBtnImage.color = new Color(0.6f, 0.2f, 0.2f, 0.8f);

            closeButton = closeBtnObj.AddComponent<Button>();
            closeButton.targetGraphic = closeBtnImage;

            var closeLabelObj = CreateUIObject("Label", closeBtnObj.transform);
            var closeLabelRect = closeLabelObj.GetComponent<RectTransform>();
            closeLabelRect.anchorMin = Vector2.zero;
            closeLabelRect.anchorMax = Vector2.one;
            closeLabelRect.offsetMin = Vector2.zero;
            closeLabelRect.offsetMax = Vector2.zero;

            var closeBtnText = closeLabelObj.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                closeBtnText.font = TMP_Settings.defaultFontAsset;
            closeBtnText.text = "X";
            closeBtnText.fontSize = 20f;
            closeBtnText.fontStyle = FontStyles.Bold;
            closeBtnText.alignment = TextAlignmentOptions.Center;
            closeBtnText.color = Color.white;
            closeBtnText.raycastTarget = false;
        }

        closeButton.onClick.RemoveAllListeners();
        closeButton.onClick.AddListener(Close);
    }

    #endregion
}
