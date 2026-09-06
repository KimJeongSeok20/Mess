using System;
using ChocDino.UIFX;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Deterministic authoring pass for the field-kit inventory and persistent quick-access HUD.
/// It only touches the redesign prefab/assets; Assets/Test/Canvas.prefab remains untouched.
/// </summary>
public static class InventoryAaaAuthoring
{
    private const string ThemePath = "Assets/UI/Inventory/InventoryTheme.asset";
    private const string CanvasPath = "Assets/UI/Inventory/InventoryCanvas.prefab";
    private const string CellPath = "Assets/Scripts/Inventory/Cell.prefab";
    private const string ItemPath = "Assets/Scripts/Inventory/UI/UIitem.prefab";
    private const string CurrencyTexturePath = "Assets/UI/Inventory/Textures/CurrencySalvageWashers.png";
    private const string BarlowPath = "Assets/Resources/UI/Fonts/Vitals/BarlowCondensed-SemiBold SDF.asset";
    private const string InterPath = "Assets/Runtime Debugger Toolkit/Resources/Font/Inter SDF.asset";

    [MenuItem("Tools/UI/Inventory: Apply Field Kit Redesign")]
    public static void ApplyFieldKitRedesign()
    {
        InventoryTheme theme = AssetDatabase.LoadAssetAtPath<InventoryTheme>(ThemePath);
        if (theme == null)
            throw new InvalidOperationException($"Inventory theme is missing: {ThemePath}");

        ApplyTheme(theme);
        EditPrefab(CanvasPath, root => StyleCanvas(root, theme));
        EditPrefab(CellPath, root => StyleSlot(root, theme, false));
        EditPrefab(ItemPath, root => StyleItem(root, theme));

        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[InventoryAaaAuthoring] Field-kit inventory and quick-access HUD applied.");
    }

    [MenuItem("Tools/UI/Inventory: Apply Detail Price Currency")]
    public static void ApplyDetailPriceCurrency()
    {
        InventoryTheme theme = AssetDatabase.LoadAssetAtPath<InventoryTheme>(ThemePath);
        if (theme == null)
            throw new InvalidOperationException($"Inventory theme is missing: {ThemePath}");

        EditPrefab(CanvasPath, root =>
        {
            Transform detail = FindRequired(root.transform, "DetailPanel");
            StyleDetailPriceCurrency(detail, theme);
        });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[InventoryAaaAuthoring] Inventory detail weight replaced with item price currency row.");
    }

    private static void ApplyTheme(InventoryTheme theme)
    {
        theme.dim = Rgba("030607", 0.80f);
        theme.surface0 = Rgba("091014", 0.985f);
        theme.surface1 = Rgba("10191E", 0.98f);
        theme.surface2 = Rgba("18252C", 1f);
        theme.surface3 = Rgba("21343D", 1f);
        theme.surfaceLocked = Rgba("091014", 0.28f);

        theme.borderSubtle = Rgba("34444B", 0.72f);
        theme.borderStrong = Rgba("718B95", 0.92f);
        theme.borderLocked = Rgba("34444B", 0.22f);

        theme.textPrimary = Rgba("E9F0F1", 1f);
        theme.textSecondary = Rgba("91A1A7", 1f);
        theme.textDisabled = Rgba("526168", 1f);

        theme.accent = Rgba("93D4E3", 1f);
        theme.accentDim = Rgba("4DAFC5", 0.18f);
        theme.danger = Rgba("E35A5D", 1f);
        theme.success = Rgba("73D4A2", 1f);

        theme.rarityCommon = Rgba("A5B0B3", 1f);
        theme.rarityUncommon = Rgba("63D39B", 1f);
        theme.rarityRare = Rgba("5DA9F6", 1f);
        theme.rarityEpic = Rgba("B47CF2", 1f);
        theme.rarityLegendary = Rgba("E6AD4B", 1f);

        theme.titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BarlowPath);
        theme.bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(InterPath);
        theme.titleSize = 34f;
        theme.labelSize = 15f;
        theme.bodySize = 16f;
        theme.captionSize = 13f;
        theme.numericSize = 17f;
        theme.titleLetterSpacing = 1.6f;
        theme.titleWeight = FontWeight.SemiBold;
        theme.labelWeight = FontWeight.Medium;
        theme.bodyWeight = FontWeight.Regular;
        theme.captionWeight = FontWeight.Medium;
        theme.numericWeight = FontWeight.SemiBold;

        theme.panelFade = 0.18f;
        theme.panelScale = 0.22f;
        theme.panelScaleFrom = 0.985f;
        theme.panelScaleTo = 1f;
        theme.slotState = 0.11f;
        theme.slotStagger = 0.018f;
        theme.slotStaggerMax = 0.14f;
    }

    private static void StyleCanvas(GameObject root, InventoryTheme theme)
    {
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        Transform inventory = FindRequired(root.transform, "inventory");
        Transform panel = FindRequired(inventory, "Panel");
        Transform header = FindRequired(panel, "Header");
        Transform body = FindRequired(panel, "Body");
        Transform footer = FindRequired(panel, "Footer");
        Transform gridViewport = FindRequired(body, "GridViewport");
        Transform grid = FindRequired(gridViewport, "Grid");
        Transform detail = FindRequired(body, "DetailPanel");
        Transform quickBar = FindRequired(inventory, "QuickSlotBar");

        StyleDim(FindRequired(inventory, "DimOverlay"), theme);
        StylePanel(panel, header, body, footer, theme);
        StyleHeader(header, panel, theme);
        StyleGrid(gridViewport, grid, theme);
        StyleDetail(detail, theme);
        StyleFooter(footer, theme);
        StyleQuickBar(inventory, quickBar, theme);
        StyleTooltip(root.transform, theme);
        StyleActionPopup(root.transform, theme);
        Retype(root, theme);
    }

    private static void StyleDim(Transform dim, InventoryTheme theme)
    {
        RectTransform rect = RequireRect(dim);
        Stretch(rect);
        Image image = EnsureComponent<Image>(dim.gameObject);
        image.sprite = null;
        image.color = theme.dim;
        image.raycastTarget = true;
    }

    private static void StylePanel(
        Transform panel,
        Transform header,
        Transform body,
        Transform footer,
        InventoryTheme theme)
    {
        RectTransform rect = RequireRect(panel);
        SetRect(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, 38f), new Vector2(1360f, 660f));

        Image image = EnsureComponent<Image>(panel.gameObject);
        ConfigureImage(image, theme.panelSprite, theme.surface0, Image.Type.Sliced, true);

        RectTransform headerRect = RequireRect(header);
        SetRect(headerRect, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 78f));

        RectTransform footerRect = RequireRect(footer);
        SetRect(footerRect, Vector2.zero, new Vector2(1f, 0f),
            new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 50f));

        RectTransform bodyRect = RequireRect(body);
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(0f, 50f);
        bodyRect.offsetMax = new Vector2(0f, -78f);

        HorizontalLayoutGroup layout = EnsureComponent<HorizontalLayoutGroup>(body.gameObject);
        layout.enabled = true;
        layout.padding = new RectOffset(28, 28, 24, 24);
        layout.spacing = 24f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        RectTransform sheen = EnsureRectChild(panel, "SurfaceSheen");
        Stretch(sheen);
        sheen.SetAsFirstSibling();
        Image sheenImage = EnsureComponent<Image>(sheen.gameObject);
        sheenImage.sprite = theme.panelSprite;
        sheenImage.type = Image.Type.Sliced;
        sheenImage.color = new Color(0.34f, 0.67f, 0.74f, 0.010f);
        sheenImage.raycastTarget = false;

        RectTransform leftSignal = EnsureRectChild(panel, "LeftSignal");
        SetRect(leftSignal, new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(0f, 0.5f), new Vector2(1.5f, 0f), new Vector2(3f, -2f));
        Image signalImage = EnsureComponent<Image>(leftSignal.gameObject);
        signalImage.sprite = null;
        signalImage.color = WithAlpha(theme.accent, 0.88f);
        signalImage.raycastTarget = false;
    }

    private static void StyleHeader(Transform header, Transform panel, InventoryTheme theme)
    {
        Transform strip = FindRequired(header, "Strip");
        Stretch(RequireRect(strip));
        ConfigureImage(EnsureComponent<Image>(strip.gameObject), theme.panelSprite,
            Rgba("0C151A", 0.98f), Image.Type.Sliced, false);

        TMP_Text eyebrow = EnsureText(EnsureRectChild(header, "EyebrowText"),
            "FIELD LOADOUT  /  STORAGE", 12f, theme.accent, theme.titleFont);
        SetRect(RequireRect(eyebrow.transform), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(30f, -11f), new Vector2(430f, 18f));
        eyebrow.alignment = TextAlignmentOptions.TopLeft;
        eyebrow.characterSpacing = 2.2f;

        TMP_Text title = FindRequired(header, "TitleText").GetComponent<TMP_Text>();
        SetRect(RequireRect(title.transform), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(28f, -28f), new Vector2(520f, 42f));
        title.text = "FIELD INVENTORY";
        title.font = theme.titleFont;
        title.fontSize = theme.titleSize;
        title.fontWeight = theme.titleWeight;
        title.characterSpacing = theme.titleLetterSpacing;
        title.color = theme.textPrimary;
        title.alignment = TextAlignmentOptions.TopLeft;

        TMP_Text capacity = FindRequired(header, "SlotCountText").GetComponent<TMP_Text>();
        SetRect(RequireRect(capacity.transform), new Vector2(1f, 0f), new Vector2(1f, 1f),
            new Vector2(1f, 0.5f), new Vector2(-76f, 0f), new Vector2(260f, 0f));
        capacity.font = theme.titleFont;
        capacity.fontSize = 17f;
        capacity.fontWeight = FontWeight.SemiBold;
        capacity.characterSpacing = 1.1f;
        capacity.color = theme.textSecondary;
        capacity.alignment = TextAlignmentOptions.MidlineRight;

        Transform close = FindRequired(header, "CloseButton");
        SetRect(RequireRect(close), Vector2.one, Vector2.one, Vector2.one,
            new Vector2(-18f, -19f), new Vector2(36f, 36f));
        ConfigureImage(EnsureComponent<Image>(close.gameObject), theme.buttonSprite,
            theme.surface1, Image.Type.Sliced, true);
        TMP_Text closeLabel = FindRequired(close, "Label").GetComponent<TMP_Text>();
        closeLabel.text = "×";
        closeLabel.font = theme.titleFont;
        closeLabel.fontSize = 24f;
        closeLabel.color = theme.textSecondary;

        Transform divider = FindRequired(panel, "HeaderDivider");
        RectTransform dividerRect = RequireRect(divider);
        dividerRect.anchorMin = new Vector2(0f, 1f);
        dividerRect.anchorMax = new Vector2(1f, 1f);
        dividerRect.pivot = new Vector2(0.5f, 1f);
        dividerRect.anchoredPosition = new Vector2(0f, -78f);
        dividerRect.sizeDelta = new Vector2(-56f, 1f);
        ConfigureImage(EnsureComponent<Image>(divider.gameObject), theme.dividerSprite,
            theme.borderSubtle, Image.Type.Simple, false);
    }

    private static void StyleGrid(Transform viewport, Transform grid, InventoryTheme theme)
    {
        LayoutElement viewportLayout = EnsureComponent<LayoutElement>(viewport.gameObject);
        viewportLayout.preferredWidth = 824f;
        viewportLayout.minWidth = 824f;
        viewportLayout.flexibleWidth = 0f;

        Image viewportImage = EnsureComponent<Image>(viewport.gameObject);
        ConfigureImage(viewportImage, theme.panelSprite, WithAlpha(theme.surface0, 0.48f),
            Image.Type.Sliced, false);

        RectTransform outline = EnsureRectChild(viewport, "ModuleOutline");
        Stretch(outline);
        outline.SetAsLastSibling();
        LayoutElement ignore = EnsureComponent<LayoutElement>(outline.gameObject);
        ignore.ignoreLayout = true;
        ConfigureImage(EnsureComponent<Image>(outline.gameObject), theme.panelOutlineSprite,
            WithAlpha(theme.borderSubtle, 0.48f), Image.Type.Sliced, false);

        RectTransform gridRect = RequireRect(grid);
        gridRect.anchorMin = new Vector2(0f, 1f);
        gridRect.anchorMax = new Vector2(0f, 1f);
        gridRect.pivot = new Vector2(0f, 1f);
        gridRect.anchoredPosition = new Vector2(14f, -14f);
        gridRect.sizeDelta = new Vector2(796f, 408f);

        GridLayoutGroup layout = EnsureComponent<GridLayoutGroup>(grid.gameObject);
        layout.padding = new RectOffset();
        layout.cellSize = new Vector2(92f, 92f);
        layout.spacing = new Vector2(10f, 10f);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 8;
        layout.startAxis = GridLayoutGroup.Axis.Horizontal;
        layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        layout.childAlignment = TextAnchor.UpperLeft;
    }

    private static void StyleDetail(Transform detail, InventoryTheme theme)
    {
        LayoutElement detailLayout = EnsureComponent<LayoutElement>(detail.gameObject);
        detailLayout.preferredWidth = 428f;
        detailLayout.minWidth = 428f;
        detailLayout.flexibleWidth = 0f;
        ConfigureImage(EnsureComponent<Image>(detail.gameObject), theme.panelSprite,
            theme.surface1, Image.Type.Sliced, false);

        RectTransform accent = EnsureRectChild(detail, "DossierAccent");
        SetRect(accent, new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(0f, 0.5f), new Vector2(1.5f, 0f), new Vector2(3f, -2f));
        ConfigureImage(EnsureComponent<Image>(accent.gameObject), null, theme.accent,
            Image.Type.Simple, false);

        TMP_Text label = EnsureText(EnsureRectChild(detail, "DossierLabel"),
            "ITEM DOSSIER", 12f, theme.accent, theme.titleFont);
        SetRect(RequireRect(label.transform), new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), new Vector2(24f, -17f), new Vector2(-48f, 18f));
        label.alignment = TextAlignmentOptions.TopLeft;
        label.characterSpacing = 2.1f;

        RectTransform iconPlate = EnsureRectChild(detail, "IconPlate");
        SetRect(iconPlate, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(24f, -50f), new Vector2(112f, 112f));
        ConfigureImage(EnsureComponent<Image>(iconPlate.gameObject), theme.slotSprite,
            theme.surface0, Image.Type.Sliced, false);
        iconPlate.SetAsFirstSibling();

        Transform icon = FindRequired(detail, "DetailIcon");
        SetRect(RequireRect(icon), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(38f, -64f), new Vector2(84f, 84f));
        icon.SetAsLastSibling();

        StyleDetailText(detail, "DetailName", new Vector2(158f, -52f), new Vector2(242f, 34f),
            28f, theme.textPrimary, theme.titleFont, FontWeight.SemiBold);
        StyleDetailText(detail, "DetailCategory", new Vector2(158f, -91f), new Vector2(242f, 18f),
            12f, theme.textSecondary, theme.bodyFont, FontWeight.Medium);
        StyleDetailText(detail, "DetailRarity", new Vector2(158f, -115f), new Vector2(242f, 18f),
            13f, theme.rarityCommon, theme.titleFont, FontWeight.SemiBold);

        Transform divider = FindRequired(detail, "DetailDivider");
        SetRect(RequireRect(divider), new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -181f), new Vector2(-48f, 1f));
        ConfigureImage(EnsureComponent<Image>(divider.gameObject), theme.dividerSprite,
            theme.borderSubtle, Image.Type.Simple, false);

        Transform stats = FindRequired(detail, "DetailStats");
        SetRect(RequireRect(stats), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(24f, -198f), new Vector2(380f, 132f));
        foreach (TMP_Text text in stats.GetComponentsInChildren<TMP_Text>(true))
        {
            text.font = theme.bodyFont;
            text.fontSize = 13f;
            text.fontWeight = FontWeight.Medium;
        }

        StyleDetailText(detail, "DetailDescription", new Vector2(24f, -342f), new Vector2(380f, 78f),
            15f, theme.textSecondary, theme.bodyFont, FontWeight.Regular);

        Transform detailFooter = FindRequired(detail, "DetailFooter");
        SetRect(RequireRect(detailFooter), new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(-48f, 32f));
        foreach (TMP_Text text in detailFooter.GetComponentsInChildren<TMP_Text>(true))
        {
            text.font = theme.titleFont;
            text.fontSize = 17f;
            text.fontWeight = FontWeight.SemiBold;
        }

        TMP_Text empty = FindRequired(detail, "EmptyStateText").GetComponent<TMP_Text>();
        empty.font = theme.titleFont;
        empty.fontSize = 18f;
        empty.characterSpacing = 1.2f;
        empty.color = theme.textDisabled;
        empty.text = "SELECT AN ITEM\nTO INSPECT";
        empty.alignment = TextAlignmentOptions.Center;

        InventoryDetailPanel presenter = detail.GetComponent<InventoryDetailPanel>();
        SetString(presenter, "emptyStateMessage", "SELECT AN ITEM\nTO INSPECT");
        SetObjectArray(presenter, "contentOnlyObjects", divider.gameObject, iconPlate.gameObject);
        StyleDetailPriceCurrency(detail, theme);
    }

    private static void StyleDetailPriceCurrency(Transform detail, InventoryTheme theme)
    {
        Transform detailFooter = FindRequired(detail, "DetailFooter");
        Transform priceTransform = FindDeep(detailFooter, "ItemPriceText")
            ?? FindRequired(detailFooter, "WeightText");
        Transform legacyPrice = FindDeep(detailFooter, "PriceText");
        if (legacyPrice != null && legacyPrice != priceTransform)
            legacyPrice.gameObject.SetActive(false);

        priceTransform.name = "ItemPriceText";
        priceTransform.gameObject.SetActive(true);
        TMP_Text priceText = priceTransform.GetComponent<TMP_Text>();
        priceText.text = "100";
        priceText.font = theme.titleFont;
        priceText.fontSize = 17f;
        priceText.fontWeight = FontWeight.SemiBold;
        priceText.color = theme.textPrimary;
        priceText.alignment = TextAlignmentOptions.MidlineLeft;
        priceText.raycastTarget = false;

        HorizontalLayoutGroup layout = EnsureComponent<HorizontalLayoutGroup>(detailFooter.gameObject);
        layout.enabled = false;

        RectTransform priceRect = RequireRect(priceTransform);
        SetRect(priceRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), Vector2.zero, new Vector2(64f, 25f));
        ContentSizeFitter priceFitter = EnsureComponent<ContentSizeFitter>(priceTransform.gameObject);
        priceFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        priceFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        priceFitter.enabled = false;

        Texture2D currencyTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(CurrencyTexturePath);
        if (currencyTexture == null)
            throw new InvalidOperationException($"Inventory currency texture is missing: {CurrencyTexturePath}");

        RectTransform iconRect = EnsureRectChild(detailFooter, "PriceCurrencyIcon");
        SetRect(iconRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(42f, 0f), new Vector2(22f, 22f));
        UnityEngine.UI.RawImage priceIcon = EnsureComponent<UnityEngine.UI.RawImage>(iconRect.gameObject);
        priceIcon.texture = currencyTexture;
        priceIcon.color = Color.white;
        priceIcon.raycastTarget = false;
        LayoutElement iconLayout = EnsureComponent<LayoutElement>(iconRect.gameObject);
        iconLayout.minWidth = 22f;
        iconLayout.preferredWidth = 22f;
        iconLayout.minHeight = 22f;
        iconLayout.preferredHeight = 22f;
        iconLayout.flexibleWidth = 0f;
        iconLayout.flexibleHeight = 0f;

        priceTransform.SetAsFirstSibling();
        iconRect.SetSiblingIndex(1);

        InventoryDetailPanel presenter = detail.GetComponent<InventoryDetailPanel>();
        SetObject(presenter, "priceText", priceText);
        SetObject(presenter, "priceCurrencyIcon", priceIcon);
        SetObject(presenter, "weightText", null);
    }

    private static void StyleDetailText(
        Transform parent,
        string name,
        Vector2 position,
        Vector2 size,
        float fontSize,
        Color color,
        TMP_FontAsset font,
        FontWeight weight)
    {
        TMP_Text text = FindRequired(parent, name).GetComponent<TMP_Text>();
        SetRect(RequireRect(text.transform), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), position, size);
        text.font = font;
        text.fontSize = fontSize;
        text.fontWeight = weight;
        text.color = color;
        text.alignment = TextAlignmentOptions.TopLeft;
    }

    private static void StyleFooter(Transform footer, InventoryTheme theme)
    {
        ConfigureImage(EnsureComponent<Image>(footer.gameObject), theme.panelSprite,
            Rgba("0C1519", 0.98f), Image.Type.Sliced, false);

        TMP_Text weight = FindRequired(footer, "TotalWeightText").GetComponent<TMP_Text>();
        SetRect(RequireRect(weight.transform), new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(300f, 0f));
        weight.font = theme.titleFont;
        weight.fontSize = 16f;
        weight.characterSpacing = 0.8f;
        weight.color = theme.textSecondary;
        weight.alignment = TextAlignmentOptions.MidlineLeft;

        TMP_Text hint = EnsureText(EnsureRectChild(footer, "ControlHintText"),
            "DRAG TO EQUIP     •     RIGHT CLICK TO DROP", 12f, theme.textDisabled, theme.bodyFont);
        SetRect(RequireRect(hint.transform), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440f, 0f));
        hint.alignment = TextAlignmentOptions.Center;
        hint.characterSpacing = 0.6f;

        Transform currency = FindRequired(footer, "Currency");
        SetRect(RequireRect(currency), new Vector2(1f, 0f), Vector2.one,
            new Vector2(1f, 0.5f), new Vector2(-26f, 0f), new Vector2(236f, 0f));
        Image currencyBackground = currency.GetComponent<Image>();
        if (currencyBackground != null)
            currencyBackground.enabled = false;
        TMP_Text currencyText = currency.GetComponentInChildren<TMP_Text>(true);
        if (currencyText != null)
        {
            currencyText.font = theme.titleFont;
            currencyText.fontSize = 26f;
            currencyText.characterSpacing = 0.8f;
            currencyText.color = theme.textPrimary;
        }
    }

    private static void StyleQuickBar(Transform inventory, Transform quickBar, InventoryTheme theme)
    {
        RectTransform barRect = RequireRect(quickBar);
        SetRect(barRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(374f, 88f));
        ConfigureImage(EnsureComponent<Image>(quickBar.gameObject), theme.panelSprite,
            Rgba("071014", 0.90f), Image.Type.Sliced, false);

        HorizontalLayoutGroup layout = EnsureComponent<HorizontalLayoutGroup>(quickBar.gameObject);
        layout.padding = new RectOffset(11, 11, 8, 8);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        RectTransform topSignal = EnsureRectChild(quickBar, "TopSignal");
        SetRect(topSignal, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), Vector2.zero, new Vector2(96f, 2f));
        LayoutElement signalLayout = EnsureComponent<LayoutElement>(topSignal.gameObject);
        signalLayout.ignoreLayout = true;
        ConfigureImage(EnsureComponent<Image>(topSignal.gameObject), null,
            WithAlpha(theme.accent, 0.72f), Image.Type.Simple, false);

        TMP_Text quickLabel = EnsureText(EnsureRectChild(inventory, "QuickAccessLabel"),
            "QUICK ACCESS", 12f, WithAlpha(theme.textPrimary, 0.82f), theme.titleFont);
        SetRect(RequireRect(quickLabel.transform), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(0f, 114f), new Vector2(220f, 18f));
        quickLabel.alignment = TextAlignmentOptions.Center;
        quickLabel.characterSpacing = 2.4f;

        foreach (ActionSlot actionSlot in quickBar.GetComponentsInChildren<ActionSlot>(true))
            StyleSlot(actionSlot.gameObject, theme, true);
    }

    private static void StyleSlot(GameObject slot, InventoryTheme theme, bool isActionSlot)
    {
        RectTransform rect = RequireRect(slot.transform);
        rect.sizeDelta = isActionSlot ? new Vector2(82f, 72f) : new Vector2(92f, 92f);
        rect.localScale = Vector3.one;

        Image background = EnsureComponent<Image>(slot);
        ConfigureImage(background, theme.slotSprite,
            isActionSlot ? Rgba("111B20", 0.98f) : theme.surface1,
            Image.Type.Sliced, true);
        EnsureComponent<CanvasGroup>(slot);

        RectTransform overlayRect = EnsureRectChild(slot.transform, "DropOverlay");
        Stretch(overlayRect);
        Image overlay = EnsureComponent<Image>(overlayRect.gameObject);
        ConfigureImage(overlay, theme.slotSprite, Color.clear, Image.Type.Sliced, false);

        RectTransform borderRect = EnsureRectChild(slot.transform, "Border");
        Stretch(borderRect);
        Image border = EnsureComponent<Image>(borderRect.gameObject);
        ConfigureImage(border, theme.slotOutlineSprite,
            isActionSlot ? WithAlpha(theme.borderSubtle, 0.86f) : theme.borderSubtle,
            Image.Type.Sliced, false);
        GlowFilter glow = EnsureComponent<GlowFilter>(borderRect.gameObject);
        glow.Color = theme.accent;
        glow.Strength = 0f;
        glow.MaxDistance = 8f;
        glow.Blur = 3f;
        glow.ReuseDistanceMap = true;
        glow.enabled = false;

        RectTransform railRect = EnsureRectChild(slot.transform, "SelectionRail");
        if (isActionSlot)
        {
            SetRect(railRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 1f), new Vector2(42f, 3f));
        }
        else
        {
            SetRect(railRect, new Vector2(0f, 0.18f), new Vector2(0f, 0.82f),
                new Vector2(0f, 0.5f), new Vector2(1f, 0f), new Vector2(3f, 0f));
        }
        Image rail = EnsureComponent<Image>(railRect.gameObject);
        ConfigureImage(rail, null, Color.clear, Image.Type.Simple, false);
        rail.enabled = false;

        InventorySlotVisual visual = EnsureComponent<InventorySlotVisual>(slot);
        SetObject(visual, "theme", theme);
        SetObject(visual, "slotImage", background);
        SetObject(visual, "borderImage", border);
        SetObject(visual, "dropOverlay", overlay);
        SetObject(visual, "selectionRail", rail);
        SetObject(visual, "glowFilter", glow);
        SetFloat(visual, "selectedGlowStrength", isActionSlot ? 0.12f : 0.08f);
        SetFloat(visual, "unlockPulseGlowStrength", 0.22f);

        if (!isActionSlot)
            return;

        ActionSlot actionSlot = slot.GetComponent<ActionSlot>();
        if (actionSlot == null)
            return;

        RectTransform plateRect = EnsureRectChild(slot.transform, "HotkeyPlate");
        SetRect(plateRect, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(6f, -6f), new Vector2(24f, 22f));
        Image plate = EnsureComponent<Image>(plateRect.gameObject);
        ConfigureImage(plate, theme.buttonSprite, theme.surface0, Image.Type.Sliced, false);

        RectTransform hotkeyRect = EnsureRectChild(slot.transform, "HotkeyLabel");
        SetRect(hotkeyRect, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(6f, -6f), new Vector2(24f, 22f));
        TMP_Text hotkey = EnsureText(hotkeyRect, actionSlot.Index.ToString(), 17f,
            theme.textSecondary, theme.titleFont);
        hotkey.alignment = TextAlignmentOptions.Center;
        hotkey.fontWeight = FontWeight.SemiBold;
        plateRect.SetAsLastSibling();
        hotkeyRect.SetAsLastSibling();

        SetObject(actionSlot, "theme", theme);
        SetObject(actionSlot, "slotImage", background);
        SetObject(actionSlot, "hotkeyPlate", plate);
        SetObject(actionSlot, "hotkeyLabel", hotkey);
    }

    private static void StyleItem(GameObject root, InventoryTheme theme)
    {
        RectTransform rect = RequireRect(root.transform);
        rect.sizeDelta = new Vector2(92f, 92f);

        Image raycast = EnsureComponent<Image>(root);
        raycast.sprite = null;
        raycast.color = Color.clear;
        raycast.raycastTarget = true;

        Transform iconTransform = FindRequired(root.transform, "Icon");
        SetRect(RequireRect(iconTransform), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(62f, 62f));
        Image icon = iconTransform.GetComponent<Image>();
        icon.preserveAspect = true;

        Transform quantityTransform = FindRequired(root.transform, "QuantityLabel");
        SetRect(RequireRect(quantityTransform), Vector2.one, Vector2.one, Vector2.one,
            new Vector2(-6f, -5f), new Vector2(42f, 20f));
        TMP_Text quantity = quantityTransform.GetComponent<TMP_Text>();
        quantity.font = theme.titleFont;
        quantity.fontSize = 16f;
        quantity.fontWeight = FontWeight.SemiBold;
        quantity.color = theme.textPrimary;
        quantity.alignment = TextAlignmentOptions.BottomRight;

        Transform upgradeTransform = FindRequired(root.transform, "UpgradeLabel");
        SetRect(RequireRect(upgradeTransform), Vector2.one, Vector2.one, Vector2.one,
            new Vector2(-5f, -5f), new Vector2(42f, 20f));
        TMP_Text upgrade = upgradeTransform.GetComponent<TMP_Text>();
        upgrade.font = theme.titleFont;
        upgrade.fontSize = 16f;
        upgrade.fontWeight = FontWeight.SemiBold;

        Transform rarityTransform = FindRequired(root.transform, "RarityBorder");
        SetRect(RequireRect(rarityTransform), new Vector2(1f, 0.18f), new Vector2(1f, 0.82f),
            new Vector2(1f, 0.5f), new Vector2(-1f, 0f), new Vector2(3f, 0f));
        Image rarity = rarityTransform.GetComponent<Image>();
        rarity.sprite = null;
        rarity.type = Image.Type.Simple;
        rarity.raycastTarget = false;

        InventoryItem item = root.GetComponent<InventoryItem>();
        SetObject(item, "theme", theme);
        SetObject(item, "iconImage", icon);
        SetObject(item, "quantityLabel", quantity);
        SetObject(item, "upgradeLabel", upgrade);
        SetObject(item, "rarityBorder", rarity);
    }

    private static void StyleTooltip(Transform root, InventoryTheme theme)
    {
        Transform tooltip = FindDeep(root, "Tooltip");
        if (tooltip == null)
            return;

        ConfigureImage(EnsureComponent<Image>(tooltip.gameObject), theme.panelSprite,
            Rgba("091216", 0.98f), Image.Type.Sliced, false);
        RectTransform rect = RequireRect(tooltip);
        rect.sizeDelta = new Vector2(220f, 64f);

        TMP_Text name = FindRequired(tooltip, "NameText").GetComponent<TMP_Text>();
        name.font = theme.titleFont;
        name.fontSize = 18f;
        name.characterSpacing = 0.8f;
        TMP_Text info = FindRequired(tooltip, "InfoText").GetComponent<TMP_Text>();
        info.font = theme.bodyFont;
        info.fontSize = 13f;
    }

    private static void StyleActionPopup(Transform root, InventoryTheme theme)
    {
        Transform popup = FindDeep(root, "ActionSlotNamePopup");
        if (popup == null)
            return;

        RectTransform popupRect = RequireRect(popup);
        popupRect.sizeDelta = new Vector2(240f, 38f);
        ConfigureImage(EnsureComponent<Image>(popup.gameObject), theme.panelSprite,
            Rgba("091216", 0.94f), Image.Type.Sliced, false);

        TMP_Text text = FindRequired(popup, "PopupText").GetComponent<TMP_Text>();
        Stretch(RequireRect(text.transform));
        text.font = theme.titleFont;
        text.fontSize = 18f;
        text.characterSpacing = 1.1f;
        text.color = theme.textPrimary;
        text.alignment = TextAlignmentOptions.Center;

        ActionSlotNamePopup presenter = popup.GetComponent<ActionSlotNamePopup>();
        SetFloat(presenter, "fadeInDuration", 0.12f);
        SetFloat(presenter, "displayDuration", 0.85f);
        SetFloat(presenter, "fadeOutDuration", 0.24f);
        SetFloat(presenter, "yOffset", 66f);
        SetFloat(presenter, "floatUpDistance", 10f);
    }

    private static void Retype(GameObject root, InventoryTheme theme)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            bool display = text.name == "TitleText"
                || text.name == "DetailName"
                || text.name == "SlotCountText"
                || text.name == "CurrencyText"
                || text.name == "TotalWeightText"
                || text.name == "HotkeyLabel"
                || text.name == "PopupText"
                || text.name == "QuickAccessLabel"
                || text.name == "EyebrowText"
                || text.name == "DossierLabel";
            text.font = display && theme.titleFont != null ? theme.titleFont : theme.bodyFont;
        }
    }

    private static void EditPrefab(string path, Action<GameObject> edit)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            edit(root);
            if (!PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved) || !saved)
                throw new InvalidOperationException($"Failed to save prefab: {path}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static TMP_Text EnsureText(
        RectTransform rect,
        string value,
        float size,
        Color color,
        TMP_FontAsset font)
    {
        TextMeshProUGUI text = EnsureComponent<TextMeshProUGUI>(rect.gameObject);
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.font = font;
        text.raycastTarget = false;
        text.enableAutoSizing = false;
        return text;
    }

    private static RectTransform EnsureRectChild(Transform parent, string name)
    {
        Transform existing = null;
        for (int i = 0; i < parent.childCount; i++)
        {
            if (parent.GetChild(i).name == name)
            {
                existing = parent.GetChild(i);
                break;
            }
        }

        if (existing != null)
            return RequireRect(existing);

        GameObject child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child.GetComponent<RectTransform>();
    }

    private static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static Transform FindRequired(Transform root, string name)
    {
        Transform found = FindDeep(root, name);
        if (found == null)
            throw new InvalidOperationException($"Required UI object '{name}' was not found under '{root.name}'.");
        return found;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    private static RectTransform RequireRect(Transform transform)
    {
        if (transform is RectTransform rect)
            return rect;
        throw new InvalidOperationException($"'{transform.name}' has no RectTransform.");
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 sizeDelta)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        rect.localScale = Vector3.one;
    }

    private static void ConfigureImage(
        Image image,
        Sprite sprite,
        Color color,
        Image.Type type,
        bool raycast)
    {
        image.sprite = sprite;
        image.type = sprite != null ? type : Image.Type.Simple;
        image.color = color;
        image.raycastTarget = raycast;
    }

    private static void SetObject(UnityEngine.Object target, string name, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name)
            ?? throw new InvalidOperationException($"Serialized field '{name}' is missing on {target.name}.");
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void SetFloat(UnityEngine.Object target, string name, float value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name)
            ?? throw new InvalidOperationException($"Serialized field '{name}' is missing on {target.name}.");
        property.floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void SetString(UnityEngine.Object target, string name, string value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name)
            ?? throw new InvalidOperationException($"Serialized field '{name}' is missing on {target.name}.");
        property.stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void SetObjectArray(
        UnityEngine.Object target,
        string name,
        params UnityEngine.Object[] values)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name)
            ?? throw new InvalidOperationException($"Serialized field '{name}' is missing on {target.name}.");
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static Color Rgba(string hex, float alpha)
    {
        if (!ColorUtility.TryParseHtmlString("#" + hex, out Color color))
            throw new InvalidOperationException($"Invalid color: {hex}");
        color.a = alpha;
        return color;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
