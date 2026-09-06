using System;
using System.Linq;
using ChocDino.UIFX;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ⚠️ 일회성 생성기 (ONE-SHOT GENERATOR) — 이미 실행되어 프리팹이 만들어진 상태다.
///
/// 이 도구는 프리팹 계층·수치·색을 **통째로 다시 씁니다.** 지금 시점의 단일 진실 원천(source of truth)은
/// 이 스크립트가 아니라 생성된 프리팹 자산입니다:
///   - Assets/UI/Inventory/InventoryCanvas.prefab
///   - Assets/Scripts/Inventory/Cell.prefab
///   - Assets/Scripts/Inventory/UI/UIitem.prefab
///
/// 따라서 Inspector에서 프리팹을 수정한 뒤 이 메뉴를 다시 실행하면 그 수정은 사라집니다.
/// 디자인 변경은 프리팹과 InventoryTheme.asset에서 하고, 이 스크립트는 처음부터 다시 만들 때만 쓰세요.
/// </summary>
public static class InventoryUiPhase3Authoring
{
    private const string ThemePath = "Assets/UI/Inventory/InventoryTheme.asset";
    private const string CanvasPath = "Assets/UI/Inventory/InventoryCanvas.prefab";
    private const string CellPath = "Assets/Scripts/Inventory/Cell.prefab";
    private const string ItemPath = "Assets/Scripts/Inventory/UI/UIitem.prefab";

    [MenuItem("Tools/UI/Author Inventory Phase 3 (one-shot, overwrites prefabs)")]
    public static void Author()
    {
        if (!EditorUtility.DisplayDialog(
                "Inventory UI — 일회성 생성기",
                "이 도구는 InventoryCanvas / Cell / UIitem 프리팹을 통째로 다시 씁니다.\n\n"
                + "Inspector에서 직접 수정한 내용은 모두 사라집니다.\n계속할까요?",
                "덮어쓰기",
                "취소"))
        {
            return;
        }

        InventoryTheme theme = LoadRequired<InventoryTheme>(ThemePath);
        EditPrefab(CellPath, root => AuthorSlot(root, theme, false));
        EditPrefab(ItemPath, root => AuthorItem(root, theme));
        EditPrefab(CanvasPath, root => AuthorCanvas(root, theme));
        AssetDatabase.SaveAssets();
        Debug.Log("[Inventory UI] Phase 3 layout, slot states, item cell, and detail panel authored.");
    }

    private static void AuthorCanvas(GameObject root, InventoryTheme theme)
    {
        Canvas canvas = root.GetComponent<Canvas>() ?? root.GetComponentInChildren<Canvas>(true);
        Transform inventory = FindRequired(root.transform, "inventory");
        InventoryManager manager = inventory.GetComponent<InventoryManager>();
        InventoryManagerExtensions extensions = inventory.GetComponent<InventoryManagerExtensions>();
        if (canvas == null || manager == null || extensions == null)
            throw new InvalidOperationException("InventoryCanvas is missing its Canvas/manager/extension contract.");

        Transform panel = FindDirect(inventory, "Panel") ?? FindRequiredDirect(inventory, "BG");
        panel.name = "Panel";
        Transform quickSlotBar = FindDirect(inventory, "QuickSlotBar") ?? FindRequiredDirect(inventory, "ActionPanel");
        quickSlotBar.name = "QuickSlotBar";
        Transform grid = FindRequired(panel, "Grid");
        Transform header = FindDirect(panel, "Header") ?? EnsureRectChild(panel, "Header");
        Transform currency = FindDirect(inventory, "Currency") ?? FindRequired(inventory, "Currency");
        Transform dimOverlay = FindRequiredDirect(inventory, "DimOverlay");

        RectTransform panelRect = RequireRect(panel);
        SetRect(panelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240f, 584f));
        panelRect.localScale = Vector3.one;
        Image panelImage = EnsureComponent<Image>(panel.gameObject);
        ConfigureImage(panelImage, theme.panelSprite, theme.surface0, Image.Type.Sliced, true);
        CanvasGroup panelGroup = EnsureComponent<CanvasGroup>(panel.gameObject);

        RectTransform dimRect = RequireRect(dimOverlay);
        Stretch(dimRect);
        dimRect.SetAsFirstSibling();
        Image dimImage = EnsureComponent<Image>(dimOverlay.gameObject);
        ConfigureImage(dimImage, theme.dividerSprite, theme.dim, Image.Type.Simple, false);
        CanvasGroup dimGroup = EnsureComponent<CanvasGroup>(dimOverlay.gameObject);
        dimGroup.interactable = false;
        dimGroup.blocksRaycasts = false;

        AuthorHeader(panel, header, manager, theme, out TMP_Text slotCount, out Image headerStrip,
            out Image headerDivider);

        RectTransform body = EnsureRectChild(panel, "Body");
        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.offsetMin = new Vector2(0f, 40f);
        body.offsetMax = new Vector2(0f, -57f);
        HorizontalLayoutGroup bodyLayout = EnsureComponent<HorizontalLayoutGroup>(body.gameObject);
        bodyLayout.padding = new RectOffset(24, 24, 24, 24);
        bodyLayout.spacing = 24f;
        bodyLayout.childAlignment = TextAnchor.MiddleLeft;
        bodyLayout.childControlWidth = false;
        bodyLayout.childControlHeight = false;
        bodyLayout.childForceExpandWidth = false;
        bodyLayout.childForceExpandHeight = false;

        RectTransform viewport = EnsureRectChild(body, "GridViewport");
        LayoutElement viewportLayout = EnsureComponent<LayoutElement>(viewport.gameObject);
        viewportLayout.preferredWidth = 824f;
        viewportLayout.preferredHeight = 408f;
        viewportLayout.flexibleWidth = 0f;
        viewportLayout.flexibleHeight = 0f;
        SetRect(viewport, Vector2.zero, Vector2.zero, new Vector2(0f, 1f), Vector2.zero,
            new Vector2(824f, 408f));

        grid.SetParent(viewport, false);
        RectTransform gridRect = RequireRect(grid);
        Stretch(gridRect);
        GridLayoutGroup gridLayout = EnsureComponent<GridLayoutGroup>(grid.gameObject);
        gridLayout.cellSize = new Vector2(96f, 96f);
        gridLayout.spacing = new Vector2(8f, 8f);
        gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
        gridLayout.childAlignment = TextAnchor.UpperLeft;
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = 8;
        gridLayout.padding = new RectOffset();

        InventorySlot[] authoredGridSlots = grid.GetComponentsInChildren<InventorySlot>(true)
            .Where(slot => slot.transform.parent == grid)
            .ToArray();
        for (int i = 0; i < authoredGridSlots.Length; i++)
            AuthorSlot(authoredGridSlots[i].gameObject, theme, false);

        RectTransform detailRect = EnsureRectChild(body, "DetailPanel");
        LayoutElement detailLayout = EnsureComponent<LayoutElement>(detailRect.gameObject);
        detailLayout.preferredWidth = 344f;
        detailLayout.preferredHeight = 408f;
        detailLayout.flexibleWidth = 0f;
        detailLayout.flexibleHeight = 0f;
        SetRect(detailRect, Vector2.zero, Vector2.zero, new Vector2(0f, 1f), Vector2.zero,
            new Vector2(344f, 408f));
        Image detailBackground = EnsureComponent<Image>(detailRect.gameObject);
        ConfigureImage(detailBackground, theme.slotSprite, theme.surface1, Image.Type.Sliced, false);
        InventoryDetailPanel detailPanel = AuthorDetailPanel(detailRect, theme);

        RectTransform footer = EnsureRectChild(panel, "Footer");
        footer.anchorMin = Vector2.zero;
        footer.anchorMax = new Vector2(1f, 0f);
        footer.pivot = new Vector2(0.5f, 0f);
        footer.anchoredPosition = Vector2.zero;
        footer.sizeDelta = new Vector2(0f, 40f);
        Image footerImage = EnsureComponent<Image>(footer.gameObject);
        ConfigureImage(footerImage, theme.dividerSprite, theme.surface1, Image.Type.Simple, false);

        RectTransform totalWeightRect = EnsureRectChild(footer, "TotalWeightText");
        SetRect(totalWeightRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
            new Vector2(24f, 0f), new Vector2(220f, 0f));
        TMP_Text totalWeight = EnsureText(totalWeightRect, "TOTAL WEIGHT  0 kg", theme.captionSize,
            theme.textSecondary);
        totalWeight.alignment = TextAlignmentOptions.MidlineLeft;
        totalWeight.fontWeight = theme.captionWeight;

        currency.SetParent(footer, false);
        AuthorCurrency(currency, theme);

        RectTransform quickRect = RequireRect(quickSlotBar);
        SetRect(quickRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, 64f), new Vector2(420f, 96f));
        quickRect.localScale = Vector3.one;
        Image quickImage = EnsureComponent<Image>(quickSlotBar.gameObject);
        quickImage.enabled = false;
        HorizontalLayoutGroup quickLayout = EnsureComponent<HorizontalLayoutGroup>(quickSlotBar.gameObject);
        quickLayout.padding = new RectOffset();
        quickLayout.spacing = 12f;
        quickLayout.childAlignment = TextAnchor.MiddleCenter;
        quickLayout.childControlWidth = false;
        quickLayout.childControlHeight = false;
        quickLayout.childForceExpandWidth = false;
        quickLayout.childForceExpandHeight = false;

        ActionSlot[] actionSlots = quickSlotBar.GetComponentsInChildren<ActionSlot>(true)
            .OrderBy(slot => slot.Index)
            .ToArray();
        if (actionSlots.Length != 4)
            throw new InvalidOperationException($"Expected four quick slots, found {actionSlots.Length}.");
        foreach (ActionSlot actionSlot in actionSlots)
        {
            AuthorSlot(actionSlot.gameObject, theme, true);
            RequireRect(actionSlot.transform).sizeDelta = new Vector2(96f, 96f);
            AuthorHotkey(actionSlot, theme);
        }

        TMP_Text[] canvasTexts = root.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text fontSource = canvasTexts.FirstOrDefault(text => text.font != null);
        ApplyFont(root, fontSource);

        SerializedObject managerObject = new SerializedObject(manager);
        managerObject.Update();
        SetObject(managerObject, "inventoryCanvasGroup", panelGroup);
        SetObject(managerObject, "moneyCanvasGroup", currency.GetComponent<CanvasGroup>());
        SetObject(managerObject, "theme", theme);
        SetObject(managerObject, "_panelRect", panelRect);
        SetObject(managerObject, "_actionPanelRect", quickRect);
        SetObject(managerObject, "_overlayCanvasGroup", dimGroup);
        SetObject(managerObject, "_overlayImage", dimImage);
        managerObject.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject extensionObject = new SerializedObject(extensions);
        extensionObject.Update();
        SetInt(extensionObject, "actionSlotCount", 4);
        SetInt(extensionObject, "defaultCellSlots", 0);
        SetInt(extensionObject, "absoluteMaxCells", 32);
        SetObject(extensionObject, "slotPrefab", LoadRequired<GameObject>(CellPath));
        SetObject(extensionObject, "inventoryContainer", grid);
        SetObject(extensionObject, "inventoryManager", manager);
        SetObject(extensionObject, "theme", theme);
        SetObject(extensionObject, "tacticalProgressText", slotCount);
        SetObject(extensionObject, "totalWeightText", totalWeight);
        SetObject(extensionObject, "_headerStrip", headerStrip);
        SetObject(extensionObject, "_headerDivider", headerDivider);
        extensionObject.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(extensions);
        EditorUtility.SetDirty(detailPanel);
    }

    private static void AuthorHeader(
        Transform panel,
        Transform header,
        InventoryManager manager,
        InventoryTheme theme,
        out TMP_Text slotCount,
        out Image headerStrip,
        out Image headerDivider)
    {
        RectTransform headerRect = RequireRect(header);
        SetRect(headerRect, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 56f));

        RectTransform strip = EnsureRectChild(header, "Strip");
        Stretch(strip);
        strip.SetAsFirstSibling();
        headerStrip = EnsureComponent<Image>(strip.gameObject);
        ConfigureImage(headerStrip, theme.slotSprite, theme.surface1, Image.Type.Sliced, false);

        Transform dividerTransform = FindDirect(panel, "HeaderDivider") ?? FindDirect(header, "Divider");
        RectTransform divider = dividerTransform != null
            ? RequireRect(dividerTransform)
            : EnsureRectChild(panel, "HeaderDivider");
        divider.name = "HeaderDivider";
        divider.SetParent(panel, false);
        divider.anchorMin = new Vector2(0f, 1f);
        divider.anchorMax = new Vector2(1f, 1f);
        divider.pivot = new Vector2(0.5f, 1f);
        divider.anchoredPosition = new Vector2(0f, -56f);
        divider.sizeDelta = new Vector2(-48f, 1f);
        headerDivider = EnsureComponent<Image>(divider.gameObject);
        ConfigureImage(headerDivider, theme.dividerSprite, theme.borderSubtle, Image.Type.Simple, false);

        RectTransform titleRect = EnsureRectChild(header, "TitleText");
        SetRect(titleRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
            new Vector2(24f, 0f), new Vector2(420f, 0f));
        TMP_Text title = EnsureText(titleRect, "INVENTORY", theme.titleSize, theme.textPrimary);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.fontWeight = theme.titleWeight;
        title.characterSpacing = theme.titleLetterSpacing;

        RectTransform slotRect = FindDirect(header, "SlotCountText") as RectTransform
            ?? EnsureRectChild(header, "SlotCountText");
        SetRect(slotRect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
            new Vector2(-64f, 0f), new Vector2(180f, 0f));
        slotCount = EnsureText(slotRect, "4 / 36", theme.labelSize, theme.textSecondary);
        slotCount.alignment = TextAlignmentOptions.MidlineRight;
        slotCount.fontWeight = theme.labelWeight;

        RectTransform closeRect = EnsureRectChild(header, "CloseButton");
        SetRect(closeRect, Vector2.one, Vector2.one, Vector2.one,
            new Vector2(-16f, -12f), new Vector2(32f, 32f));
        Image closeImage = EnsureComponent<Image>(closeRect.gameObject);
        ConfigureImage(closeImage, theme.buttonSprite, theme.surface2, Image.Type.Sliced, true);
        Button closeButton = EnsureComponent<Button>(closeRect.gameObject);
        ColorBlock colors = closeButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = theme.surface3;
        colors.pressedColor = theme.borderStrong;
        colors.selectedColor = Color.white;
        closeButton.colors = colors;
        while (closeButton.onClick.GetPersistentEventCount() > 0)
            UnityEventTools.RemovePersistentListener(closeButton.onClick, 0);
        UnityEventTools.AddBoolPersistentListener(closeButton.onClick, manager.ToggleInventory, false);

        RectTransform closeLabelRect = EnsureRectChild(closeRect, "Label");
        Stretch(closeLabelRect);
        TMP_Text closeLabel = EnsureText(closeLabelRect, "×", 22f, theme.textPrimary);
        closeLabel.alignment = TextAlignmentOptions.Center;
        closeLabel.raycastTarget = false;
    }

    private static InventoryDetailPanel AuthorDetailPanel(RectTransform panel, InventoryTheme theme)
    {
        InventoryDetailPanel presenter = EnsureComponent<InventoryDetailPanel>(panel.gameObject);

        RectTransform iconRect = EnsureRectChild(panel, "DetailIcon");
        SetRect(iconRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -24f), new Vector2(96f, 96f));
        Image icon = EnsureComponent<Image>(iconRect.gameObject);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.color = Color.white;

        TMP_Text nameText = CreateDetailText(panel, "DetailName", new Vector2(136f, -24f),
            new Vector2(184f, 30f), theme.titleSize, theme.textPrimary, TextAlignmentOptions.TopLeft,
            theme.titleWeight);
        TMP_Text categoryText = CreateDetailText(panel, "DetailCategory", new Vector2(136f, -58f),
            new Vector2(184f, 18f), theme.captionSize, theme.textSecondary, TextAlignmentOptions.TopLeft,
            theme.captionWeight);
        TMP_Text rarityText = CreateDetailText(panel, "DetailRarity", new Vector2(136f, -80f),
            new Vector2(184f, 18f), theme.captionSize, theme.rarityCommon, TextAlignmentOptions.TopLeft,
            theme.captionWeight);

        RectTransform divider = EnsureRectChild(panel, "DetailDivider");
        divider.anchorMin = new Vector2(0f, 1f);
        divider.anchorMax = new Vector2(1f, 1f);
        divider.pivot = new Vector2(0.5f, 1f);
        divider.anchoredPosition = new Vector2(0f, -136f);
        divider.sizeDelta = new Vector2(-48f, 1f);
        Image dividerImage = EnsureComponent<Image>(divider.gameObject);
        ConfigureImage(dividerImage, theme.dividerSprite, theme.borderSubtle, Image.Type.Simple, false);

        RectTransform stats = EnsureRectChild(panel, "DetailStats");
        SetRect(stats, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -152f), new Vector2(296f, 126f));
        VerticalLayoutGroup statsLayout = EnsureComponent<VerticalLayoutGroup>(stats.gameObject);
        statsLayout.padding = new RectOffset();
        statsLayout.spacing = 3f;
        statsLayout.childAlignment = TextAnchor.UpperLeft;
        statsLayout.childControlWidth = true;
        statsLayout.childControlHeight = false;
        statsLayout.childForceExpandWidth = true;
        statsLayout.childForceExpandHeight = false;

        GameObject[] rowRoots = new GameObject[6];
        TMP_Text[] rowLabels = new TMP_Text[6];
        TMP_Text[] rowValues = new TMP_Text[6];
        for (int i = 0; i < 6; i++)
        {
            RectTransform row = EnsureRectChild(stats, $"StatLine {i + 1}");
            row.sizeDelta = new Vector2(296f, 18f);
            LayoutElement rowLayout = EnsureComponent<LayoutElement>(row.gameObject);
            rowLayout.preferredHeight = 18f;
            HorizontalLayoutGroup layout = EnsureComponent<HorizontalLayoutGroup>(row.gameObject);
            layout.padding = new RectOffset();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            RectTransform labelRect = EnsureRectChild(row, "Label");
            labelRect.sizeDelta = new Vector2(136f, 18f);
            rowLabels[i] = EnsureText(labelRect, string.Empty, theme.captionSize, theme.textSecondary);
            rowLabels[i].alignment = TextAlignmentOptions.MidlineLeft;
            RectTransform valueRect = EnsureRectChild(row, "Value");
            valueRect.sizeDelta = new Vector2(152f, 18f);
            rowValues[i] = EnsureText(valueRect, string.Empty, theme.captionSize, theme.textPrimary);
            rowValues[i].alignment = TextAlignmentOptions.MidlineRight;
            rowRoots[i] = row.gameObject;
            row.gameObject.SetActive(false);
        }

        TMP_Text description = CreateDetailText(panel, "DetailDescription", new Vector2(24f, -286f),
            new Vector2(296f, 72f), theme.bodySize, theme.textSecondary, TextAlignmentOptions.TopLeft,
            theme.bodyWeight);
        description.enableWordWrapping = true;

        RectTransform footer = EnsureRectChild(panel, "DetailFooter");
        footer.anchorMin = new Vector2(0f, 0f);
        footer.anchorMax = new Vector2(1f, 0f);
        footer.pivot = new Vector2(0.5f, 0f);
        footer.anchoredPosition = new Vector2(0f, 18f);
        footer.sizeDelta = new Vector2(-48f, 30f);
        TMP_Text price = CreateFooterText(footer, "PriceText", true, theme.textPrimary, theme);
        TMP_Text weight = CreateFooterText(footer, "WeightText", false, theme.textSecondary, theme);

        SerializedObject presenterObject = new SerializedObject(presenter);
        presenterObject.Update();
        SetObject(presenterObject, "theme", theme);
        SetObject(presenterObject, "iconImage", icon);
        SetObject(presenterObject, "nameText", nameText);
        SetObject(presenterObject, "categoryText", categoryText);
        SetObject(presenterObject, "rarityText", rarityText);
        SetObject(presenterObject, "descriptionText", description);
        SetObject(presenterObject, "priceText", price);
        SetObject(presenterObject, "weightText", weight);
        SerializedProperty rows = RequireProperty(presenterObject, "statRows");
        rows.arraySize = 6;
        for (int i = 0; i < 6; i++)
        {
            SerializedProperty row = rows.GetArrayElementAtIndex(i);
            row.FindPropertyRelative("root").objectReferenceValue = rowRoots[i];
            row.FindPropertyRelative("labelText").objectReferenceValue = rowLabels[i];
            row.FindPropertyRelative("valueText").objectReferenceValue = rowValues[i];
        }
        presenterObject.ApplyModifiedPropertiesWithoutUndo();
        return presenter;
    }

    private static TMP_Text CreateDetailText(
        Transform parent,
        string name,
        Vector2 position,
        Vector2 size,
        float fontSize,
        Color color,
        TextAlignmentOptions alignment,
        FontWeight weight)
    {
        RectTransform rect = EnsureRectChild(parent, name);
        SetRect(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), position, size);
        TMP_Text text = EnsureText(rect, string.Empty, fontSize, color);
        text.alignment = alignment;
        text.fontWeight = weight;
        return text;
    }

    private static TMP_Text CreateFooterText(
        Transform footer,
        string name,
        bool left,
        Color color,
        InventoryTheme theme)
    {
        RectTransform rect = EnsureRectChild(footer, name);
        SetRect(rect, new Vector2(left ? 0f : 0.5f, 0f), new Vector2(left ? 0.5f : 1f, 1f),
            new Vector2(left ? 0f : 1f, 0.5f), Vector2.zero, Vector2.zero);
        TMP_Text text = EnsureText(rect, string.Empty, theme.labelSize, color);
        text.alignment = left ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;
        text.fontWeight = theme.labelWeight;
        return text;
    }

    private static void AuthorCurrency(Transform currency, InventoryTheme theme)
    {
        RectTransform currencyRect = RequireRect(currency);
        SetRect(currencyRect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
            new Vector2(-24f, 0f), new Vector2(220f, 0f));
        CanvasGroup group = EnsureComponent<CanvasGroup>(currency.gameObject);
        group.blocksRaycasts = false;
        group.interactable = false;
        Image background = currency.GetComponent<Image>();
        if (background != null)
            background.enabled = false;

        TMP_Text text = currency.GetComponentInChildren<TMP_Text>(true);
        if (text != null)
        {
            RectTransform rect = RequireRect(text.transform);
            SetRect(rect, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(-36f, 0f), new Vector2(-36f, 0f));
            text.fontSize = 18f;
            text.color = theme.textPrimary;
            text.fontWeight = theme.numericWeight;
            text.alignment = TextAlignmentOptions.MidlineRight;
        }

        RawImage icon = currency.GetComponentInChildren<RawImage>(true);
        if (icon != null)
        {
            RectTransform rect = RequireRect(icon.transform);
            SetRect(rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                Vector2.zero, new Vector2(28f, 28f));
            icon.raycastTarget = false;
        }
    }

    private static void AuthorHotkey(ActionSlot actionSlot, InventoryTheme theme)
    {
        RectTransform hotkeyRect = EnsureRectChild(actionSlot.transform, "HotkeyLabel");
        SetRect(hotkeyRect, Vector2.zero, Vector2.zero, Vector2.zero,
            new Vector2(8f, 7f), new Vector2(34f, 24f));
        TMP_Text hotkey = EnsureText(hotkeyRect, actionSlot.Index.ToString(), 16f, theme.textPrimary);
        hotkey.alignment = TextAlignmentOptions.Left;
        hotkey.fontWeight = theme.numericWeight;

        SerializedObject actionObject = new SerializedObject(actionSlot);
        actionObject.Update();
        SetObject(actionObject, "theme", theme);
        SetObject(actionObject, "slotImage", actionSlot.GetComponent<Image>());
        SetObject(actionObject, "hotkeyLabel", hotkey);
        actionObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AuthorSlot(GameObject slot, InventoryTheme theme, bool actionSlot)
    {
        RectTransform rect = RequireRect(slot.transform);
        rect.sizeDelta = new Vector2(96f, 96f);
        rect.localScale = Vector3.one;
        Image background = EnsureComponent<Image>(slot);
        ConfigureImage(background, theme.slotSprite, theme.surface1, Image.Type.Sliced, true);
        EnsureComponent<CanvasGroup>(slot);

        Outline legacyOutline = slot.GetComponent<Outline>();
        if (legacyOutline != null)
            UnityEngine.Object.DestroyImmediate(legacyOutline);
        Transform stateLabel = FindDirect(slot.transform, "StateLabel");
        if (stateLabel != null)
            UnityEngine.Object.DestroyImmediate(stateLabel.gameObject);

        RectTransform overlayRect = EnsureRectChild(slot.transform, "DropOverlay");
        Stretch(overlayRect);
        Image overlay = EnsureComponent<Image>(overlayRect.gameObject);
        ConfigureImage(overlay, theme.slotSprite, Color.clear, Image.Type.Sliced, false);
        overlay.enabled = false;

        RectTransform borderRect = EnsureRectChild(slot.transform, "Border");
        Stretch(borderRect);
        Image border = EnsureComponent<Image>(borderRect.gameObject);
        ConfigureImage(border, theme.slotOutlineSprite, theme.borderSubtle, Image.Type.Sliced, false);
        GlowFilter glow = EnsureComponent<GlowFilter>(borderRect.gameObject);
        glow.Color = theme.accent;
        glow.Strength = 0f;
        glow.MaxDistance = 12f;
        glow.Blur = 4f;
        glow.ReuseDistanceMap = true;
        glow.enabled = false;

        InventorySlotVisual visual = EnsureComponent<InventorySlotVisual>(slot);
        SerializedObject visualObject = new SerializedObject(visual);
        visualObject.Update();
        SetObject(visualObject, "theme", theme);
        SetObject(visualObject, "slotImage", background);
        SetObject(visualObject, "borderImage", border);
        SetObject(visualObject, "dropOverlay", overlay);
        SetObject(visualObject, "glowFilter", glow);
        visualObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(visual);
    }

    private static void AuthorItem(GameObject root, InventoryTheme theme)
    {
        InventoryItem item = root.GetComponent<InventoryItem>();
        if (item == null)
            throw new InvalidOperationException($"{ItemPath} has no InventoryItem.");

        RectTransform rootRect = RequireRect(root.transform);
        rootRect.sizeDelta = new Vector2(96f, 96f);
        Image raycastSurface = EnsureComponent<Image>(root);
        raycastSurface.sprite = null;
        raycastSurface.color = new Color(1f, 1f, 1f, 0f);
        raycastSurface.raycastTarget = true;
        EnsureComponent<CanvasGroup>(root);

        Transform price = FindDirect(root.transform, "Price");
        if (price != null)
            UnityEngine.Object.DestroyImmediate(price.gameObject);

        RectTransform iconRect = EnsureRectChild(root.transform, "Icon");
        SetRect(iconRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(72f, 72f));
        Image icon = EnsureComponent<Image>(iconRect.gameObject);
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        RectTransform quantityRect = EnsureRectChild(root.transform, "QuantityLabel");
        SetRect(quantityRect, Vector2.zero, Vector2.zero, Vector2.zero,
            new Vector2(7f, 5f), new Vector2(42f, 20f));
        TMP_Text quantity = EnsureText(quantityRect, string.Empty, theme.numericSize, theme.textPrimary);
        quantity.alignment = TextAlignmentOptions.BottomLeft;
        quantity.fontWeight = theme.numericWeight;
        quantity.gameObject.SetActive(false);

        RectTransform upgradeRect = EnsureRectChild(root.transform, "UpgradeLabel");
        SetRect(upgradeRect, Vector2.one, Vector2.one, Vector2.one,
            new Vector2(-5f, -5f), new Vector2(44f, 22f));
        TMP_Text upgrade = EnsureText(upgradeRect, string.Empty, 16f, theme.success);
        upgrade.alignment = TextAlignmentOptions.TopRight;
        upgrade.fontWeight = theme.numericWeight;
        upgrade.gameObject.SetActive(false);

        RectTransform rarityRect = EnsureRectChild(root.transform, "RarityBorder");
        Stretch(rarityRect);
        Image rarity = EnsureComponent<Image>(rarityRect.gameObject);
        ConfigureImage(rarity, theme.slotOutlineSprite, theme.rarityCommon, Image.Type.Sliced, false);

        SerializedObject itemObject = new SerializedObject(item);
        itemObject.Update();
        SetObject(itemObject, "theme", theme);
        SetObject(itemObject, "iconImage", icon);
        SetObject(itemObject, "quantityLabel", quantity);
        SetObject(itemObject, "upgradeLabel", upgrade);
        SetObject(itemObject, "rarityBorder", rarity);
        SetObject(itemObject, "detailPanel", null);
        itemObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(item);
    }

    private static void ApplyFont(GameObject root, TMP_Text source)
    {
        if (source == null || source.font == null)
            return;
        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i].font == null)
                texts[i].font = source.font;
        }
    }

    private static TMP_Text EnsureText(RectTransform rect, string value, float size, Color color)
    {
        TextMeshProUGUI text = EnsureComponent<TextMeshProUGUI>(rect.gameObject);
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.raycastTarget = false;
        text.enableAutoSizing = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private static void ConfigureImage(Image image, Sprite sprite, Color color, Image.Type type, bool raycast)
    {
        image.sprite = sprite;
        image.color = color;
        image.type = type;
        image.fillCenter = true;
        image.preserveAspect = false;
        image.raycastTarget = raycast;
    }

    private static void SetRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static RectTransform EnsureRectChild(Transform parent, string name)
    {
        Transform existing = FindDirect(parent, name);
        if (existing != null)
            return RequireRect(existing);
        GameObject child = new GameObject(name, typeof(RectTransform));
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static T EnsureComponent<T>(GameObject gameObject) where T : Component
    {
        return gameObject.GetComponent<T>() ?? gameObject.AddComponent<T>();
    }

    private static RectTransform RequireRect(Transform transform)
    {
        RectTransform rect = transform as RectTransform;
        if (rect == null)
            throw new InvalidOperationException($"{transform.name} requires a RectTransform.");
        return rect;
    }

    private static Transform FindRequired(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindRequiredOptional(root.GetChild(i), name);
            if (result != null)
                return result;
        }
        throw new InvalidOperationException($"Missing required transform: {name}");
    }

    private static Transform FindRequiredOptional(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindRequiredOptional(root.GetChild(i), name);
            if (result != null)
                return result;
        }
        return null;
    }

    private static Transform FindRequiredDirect(Transform parent, string name)
    {
        return FindDirect(parent, name)
            ?? throw new InvalidOperationException($"Missing {parent.name}/{name}");
    }

    private static Transform FindDirect(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name)
                return child;
        }
        return null;
    }

    private static void EditPrefab(string path, Action<GameObject> edit)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
            throw new InvalidOperationException($"Missing prefab: {path}");
        try
        {
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved)
                throw new InvalidOperationException($"Could not save prefab: {path}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static T LoadRequired<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            throw new InvalidOperationException($"Missing asset: {path}");
        return asset;
    }

    private static void SetObject(SerializedObject obj, string field, UnityEngine.Object value)
    {
        RequireProperty(obj, field).objectReferenceValue = value;
    }

    private static void SetInt(SerializedObject obj, string field, int value)
    {
        RequireProperty(obj, field).intValue = value;
    }

    private static SerializedProperty RequireProperty(SerializedObject obj, string field)
    {
        SerializedProperty property = obj.FindProperty(field);
        if (property == null)
            throw new InvalidOperationException($"Missing serialized field {obj.targetObject.GetType().Name}.{field}");
        return property;
    }
}
