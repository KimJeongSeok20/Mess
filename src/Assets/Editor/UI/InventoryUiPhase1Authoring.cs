using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class InventoryUiPhase1Authoring
{
    private const string InventoryFolder = "Assets/UI/Inventory";
    private const string ThemeAssetPath = InventoryFolder + "/InventoryTheme.asset";
    private const string InventoryCanvasPath = InventoryFolder + "/InventoryCanvas.prefab";
    private const string SourceCanvasPath = "Assets/Test/Canvas.prefab";
    private const string CellPrefabPath = "Assets/Scripts/Inventory/Cell.prefab";
    private const string UiItemPrefabPath = "Assets/Scripts/Inventory/UI/UIitem.prefab";

    /// <summary>
    /// ⚠️ 일회성 생성기. 현재는 <see cref="InventoryUiPhase3Authoring.Author"/>로 위임하며,
    /// 그쪽에서 덮어쓰기 확인 다이얼로그를 띄운다. 자세한 내용은 Phase 3 생성기 주석 참고.
    /// 프리팹이 단일 진실 원천이므로, 디자인 변경은 프리팹/InventoryTheme.asset에서 할 것.
    /// </summary>
    [MenuItem("Tools/UI/Author Inventory Phase 1 (one-shot, overwrites prefabs)")]
    public static void Author()
    {
        InventoryUiPhase3Authoring.Author();
    }

    private static InventoryTheme CreateOrUpdateTheme()
    {
        InventoryTheme theme = AssetDatabase.LoadAssetAtPath<InventoryTheme>(ThemeAssetPath);
        if (theme == null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(ThemeAssetPath) != null)
                throw new InvalidOperationException($"{ThemeAssetPath} exists but is not an InventoryTheme asset.");

            theme = ScriptableObject.CreateInstance<InventoryTheme>();
            AssetDatabase.CreateAsset(theme, ThemeAssetPath);
        }

        theme.dim = Hex("0A0B0D", 0.66f);
        theme.surface0 = Hex("111315", 0.98f);
        theme.surface1 = Hex("191C1F");
        theme.surface2 = Hex("22262A");
        theme.surface3 = Hex("2A2F34");
        theme.surfaceLocked = Hex("0E1012");

        theme.borderSubtle = Hex("2B3035");
        theme.borderStrong = Hex("3A4046");

        theme.textPrimary = Hex("ECEEF0");
        theme.textSecondary = Hex("98A0A7");
        theme.textDisabled = Hex("575F66");

        theme.accent = Hex("4C8DFF");
        theme.accentDim = Hex("4C8DFF", 0.24f);
        theme.danger = Hex("E0574A");
        theme.success = Hex("4FB477");

        theme.rarityCommon = Hex("98A0A7");
        theme.rarityUncommon = Hex("4FB477");
        theme.rarityRare = Hex("4C8DFF");
        theme.rarityEpic = Hex("A067E0");
        theme.rarityLegendary = Hex("E0A046");

        theme.spacing4 = 4f;
        theme.spacing8 = 8f;
        theme.spacing12 = 12f;
        theme.spacing16 = 16f;
        theme.spacing24 = 24f;
        theme.spacing32 = 32f;

        theme.slotCornerRadius = 6f;
        theme.panelCornerRadius = 10f;
        theme.buttonCornerRadius = 6f;

        theme.titleSize = 20f;
        theme.labelSize = 13f;
        theme.bodySize = 14f;
        theme.captionSize = 11f;
        theme.numericSize = 12f;
        theme.titleLetterSpacing = 4f;
        theme.titleWeight = FontWeight.SemiBold;
        theme.labelWeight = FontWeight.Medium;
        theme.bodyWeight = FontWeight.Regular;
        theme.captionWeight = FontWeight.Medium;
        theme.numericWeight = FontWeight.SemiBold;

        theme.panelFade = 0.160f;
        theme.panelScale = 0.160f;
        theme.panelScaleFrom = 0.98f;
        theme.panelScaleTo = 1f;
        theme.slotState = 0.120f;
        theme.slotStagger = 0.008f;
        theme.slotStaggerMax = 0.200f;

        theme.panelSprite = LoadRequiredSprite("ui_panel_r10.png");
        theme.slotSprite = LoadRequiredSprite("ui_slot_r6.png");
        theme.slotOutlineSprite = LoadRequiredSprite("ui_slot_outline_r6.png");
        theme.buttonSprite = LoadRequiredSprite("ui_button_r6.png");
        theme.dividerSprite = LoadRequiredSprite("ui_divider_1px.png");

        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        return theme;
    }

    private static void EnsureInventoryCanvasCopy()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(InventoryCanvasPath) != null)
            return;

        if (AssetDatabase.LoadAssetAtPath<GameObject>(SourceCanvasPath) == null)
            throw new InvalidOperationException($"Required source prefab is missing: {SourceCanvasPath}");

        if (!AssetDatabase.CopyAsset(SourceCanvasPath, InventoryCanvasPath))
            throw new InvalidOperationException($"Could not copy {SourceCanvasPath} to {InventoryCanvasPath}.");
    }

    private static void AuthorInventoryCanvas(GameObject root, InventoryTheme theme)
    {
        Canvas parentCanvas = root.GetComponent<Canvas>() ?? root.GetComponentInChildren<Canvas>(true);
        if (parentCanvas == null)
            throw new InvalidOperationException($"{InventoryCanvasPath} has no Canvas component.");

        Transform inventory = FindRequired(root.transform, "inventory");
        Transform bg = FindRequiredDirect(inventory, "BG");
        Transform grid = FindRequiredDirect(bg, "Grid");
        Transform actionPanel = FindRequiredDirect(inventory, "ActionPanel");
        InventoryManager manager = inventory.GetComponent<InventoryManager>();
        if (manager == null)
            throw new InvalidOperationException($"{InventoryCanvasPath}/inventory has no InventoryManager.");

        RectTransform bgRect = RequireRect(bg);
        bgRect.anchorMin = new Vector2(0.22f, 0.17f);
        bgRect.anchorMax = new Vector2(0.78f, 0.72f);
        bgRect.anchoredPosition = Vector2.zero;
        bgRect.sizeDelta = Vector2.zero;
        bgRect.pivot = new Vector2(0.5f, 0.5f);

        Image bgImage = EnsureComponent<Image>(bg.gameObject);
        ConfigureImage(bgImage, theme.panelSprite, theme.surface0, Image.Type.Sliced, true);

        RectTransform actionPanelRect = RequireRect(actionPanel);
        actionPanelRect.anchorMin = new Vector2(0.5f, 0f);
        actionPanelRect.anchorMax = new Vector2(0.5f, 0f);
        actionPanelRect.pivot = new Vector2(0.5f, 0.5f);
        actionPanelRect.anchoredPosition = new Vector2(0f, 64f);
        actionPanelRect.sizeDelta = new Vector2(496f, 72f);

        Image actionPanelImage = EnsureComponent<Image>(actionPanel.gameObject);
        ConfigureImage(actionPanelImage, theme.panelSprite, theme.surface0, Image.Type.Sliced, false);
        actionPanelImage.enabled = false;

        RectTransform dimRect = EnsureRectChild(inventory, "DimOverlay");
        Stretch(dimRect);
        dimRect.SetAsFirstSibling();
        Image dimImage = EnsureComponent<Image>(dimRect.gameObject);
        ConfigureImage(dimImage, theme.dividerSprite, theme.dim, Image.Type.Simple, false);
        CanvasGroup dimGroup = EnsureComponent<CanvasGroup>(dimRect.gameObject);
        dimGroup.alpha = 0f;
        dimGroup.interactable = false;
        dimGroup.blocksRaycasts = false;

        RectTransform header = EnsureRectChild(bg, "Header");
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.anchoredPosition = Vector2.zero;
        header.sizeDelta = new Vector2(0f, 40f);
        header.SetAsLastSibling();

        RectTransform stripRect = EnsureRectChild(header, "Strip");
        Stretch(stripRect);
        stripRect.SetAsFirstSibling();
        Image headerStrip = EnsureComponent<Image>(stripRect.gameObject);
        ConfigureImage(headerStrip, theme.slotSprite, theme.surface1, Image.Type.Sliced, false);

        RectTransform dividerRect = EnsureRectChild(header, "Divider");
        dividerRect.anchorMin = new Vector2(0f, 0f);
        dividerRect.anchorMax = new Vector2(1f, 0f);
        dividerRect.pivot = new Vector2(0.5f, 0.5f);
        dividerRect.anchoredPosition = Vector2.zero;
        dividerRect.sizeDelta = new Vector2(0f, 1f);
        Image headerDivider = EnsureComponent<Image>(dividerRect.gameObject);
        ConfigureImage(headerDivider, theme.dividerSprite, theme.borderSubtle, Image.Type.Simple, false);

        RectTransform slotCountRect = EnsureRectChild(header, "SlotCountText");
        slotCountRect.anchorMin = new Vector2(1f, 0f);
        slotCountRect.anchorMax = new Vector2(1f, 1f);
        slotCountRect.pivot = new Vector2(1f, 0.5f);
        slotCountRect.anchoredPosition = new Vector2(-theme.spacing16, 0f);
        slotCountRect.sizeDelta = new Vector2(160f, 0f);
        TMP_Text slotCountText = EnsureText(slotCountRect, "4 / 36", theme.labelSize, theme.textSecondary);
        slotCountText.alignment = TextAlignmentOptions.MidlineRight;
        slotCountText.fontWeight = theme.labelWeight;

        ActionSlot[] actionSlots = inventory.GetComponentsInChildren<ActionSlot>(true)
            .OrderBy(slot => slot.Index)
            .ToArray();
        if (actionSlots.Length != 4)
            throw new InvalidOperationException($"Expected four ActionSlot components, found {actionSlots.Length}.");

        InventorySlot[] actionInventorySlots = new InventorySlot[actionSlots.Length];
        for (int i = 0; i < actionSlots.Length; i++)
        {
            ActionSlot actionSlot = actionSlots[i];
            actionInventorySlots[i] = AuthorActionCell(actionSlot, theme);
        }

        InventoryTooltip tooltip = AuthorTooltip(root.transform, parentCanvas, theme);
        ActionSlotNamePopup popup = AuthorActionSlotNamePopup(root.transform, parentCanvas, theme);

        var managerObject = new SerializedObject(manager);
        managerObject.UpdateIfRequiredOrScript();
        SetObject(managerObject, "theme", theme);
        SetObject(managerObject, "_panelRect", bgRect);
        SetObject(managerObject, "_actionPanelRect", actionPanelRect);
        SetObject(managerObject, "_overlayCanvasGroup", dimGroup);
        SetObject(managerObject, "_overlayImage", dimImage);
        SetObjectArray(managerObject, "slots", actionInventorySlots.Cast<UnityEngine.Object>().ToArray());
        ResizeArray(managerObject, "_inventoryData", 4);
        managerObject.ApplyModifiedPropertiesWithoutUndo();

        InventoryManagerExtensions extensions = inventory.GetComponent<InventoryManagerExtensions>()
            ?? inventory.gameObject.AddComponent<InventoryManagerExtensions>();
        var extensionsObject = new SerializedObject(extensions);
        extensionsObject.UpdateIfRequiredOrScript();
        SetInt(extensionsObject, "actionSlotCount", 4);
        SetInt(extensionsObject, "defaultCellSlots", 0);
        SetInt(extensionsObject, "absoluteMaxCells", 32);
        SetObject(extensionsObject, "slotPrefab", LoadRequiredAsset<GameObject>(CellPrefabPath));
        SetObject(extensionsObject, "inventoryContainer", grid);
        SetObject(extensionsObject, "inventoryManager", manager);
        SetObject(extensionsObject, "theme", theme);
        SetObject(extensionsObject, "tacticalProgressText", slotCountText);
        SetObject(extensionsObject, "_headerStrip", headerStrip);
        SetObject(extensionsObject, "_headerDivider", headerDivider);
        extensionsObject.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(extensions);
        EditorUtility.SetDirty(tooltip);
        EditorUtility.SetDirty(popup);
    }

    private static InventorySlot AuthorActionCell(ActionSlot actionSlot, InventoryTheme theme)
    {
        GameObject cell = actionSlot.gameObject;
        Image slotImage = EnsureComponent<Image>(cell);
        ConfigureImage(slotImage, theme.slotSprite, theme.surface1, Image.Type.Sliced, true);

        Outline outline = EnsureSlotOutline(cell, theme);
        InventorySlotVisual visual = EnsureComponent<InventorySlotVisual>(cell);

        RectTransform hotkeyRect = EnsureRectChild(cell.transform, "HotkeyLabel");
        Canvas nestedCanvas = hotkeyRect.GetComponent<Canvas>();
        if (nestedCanvas != null)
            UnityEngine.Object.DestroyImmediate(nestedCanvas);
        GraphicRaycaster nestedRaycaster = hotkeyRect.GetComponent<GraphicRaycaster>();
        if (nestedRaycaster != null)
            UnityEngine.Object.DestroyImmediate(nestedRaycaster);

        hotkeyRect.anchorMin = Vector2.zero;
        hotkeyRect.anchorMax = Vector2.zero;
        hotkeyRect.pivot = Vector2.zero;
        hotkeyRect.anchoredPosition = new Vector2(theme.spacing8, 7f);
        hotkeyRect.sizeDelta = new Vector2(34f, 26f);
        TMP_Text hotkeyLabel = EnsureText(
            hotkeyRect,
            actionSlot.Index.ToString(),
            18f,
            theme.textPrimary);
        hotkeyLabel.alignment = TextAlignmentOptions.Left;
        hotkeyLabel.fontStyle = FontStyles.Normal;
        hotkeyLabel.fontWeight = theme.numericWeight;
        hotkeyLabel.transform.SetAsLastSibling();
        Outline hotkeyOutline = EnsureComponent<Outline>(hotkeyRect.gameObject);
        hotkeyOutline.effectColor = theme.surfaceLocked;
        hotkeyOutline.effectDistance = new Vector2(1f, -1f);
        hotkeyOutline.useGraphicAlpha = true;

        var visualObject = new SerializedObject(visual);
        visualObject.UpdateIfRequiredOrScript();
        SetObject(visualObject, "theme", theme);
        SetObject(visualObject, "slotImage", slotImage);
        SetObject(visualObject, "stateLabel", null);
        SetObject(visualObject, "outline", outline);
        visualObject.ApplyModifiedPropertiesWithoutUndo();

        var actionObject = new SerializedObject(actionSlot);
        actionObject.UpdateIfRequiredOrScript();
        SetObject(actionObject, "theme", theme);
        SetObject(actionObject, "slotImage", slotImage);
        SetObject(actionObject, "hotkeyLabel", hotkeyLabel);
        actionObject.ApplyModifiedPropertiesWithoutUndo();

        InventorySlot inventorySlot = cell.GetComponent<InventorySlot>();
        if (inventorySlot == null)
            throw new InvalidOperationException($"{cell.name} has no InventorySlot component.");

        return inventorySlot;
    }

    private static void AuthorCellPrefab(GameObject root, InventoryTheme theme)
    {
        Image slotImage = EnsureComponent<Image>(root);
        ConfigureImage(slotImage, theme.slotSprite, theme.surface1, Image.Type.Sliced, true);
        EnsureComponent<CanvasGroup>(root);

        Outline outline = EnsureSlotOutline(root, theme);
        InventorySlotVisual visual = EnsureComponent<InventorySlotVisual>(root);

        RectTransform stateRect = EnsureRectChild(root.transform, "StateLabel");
        Stretch(stateRect);
        TMP_Text stateLabel = EnsureText(stateRect, string.Empty, theme.captionSize, theme.textDisabled);
        stateLabel.alignment = TextAlignmentOptions.Center;
        stateLabel.fontStyle = FontStyles.Normal;
        stateLabel.fontWeight = theme.captionWeight;

        var visualObject = new SerializedObject(visual);
        visualObject.UpdateIfRequiredOrScript();
        SetObject(visualObject, "theme", theme);
        SetObject(visualObject, "slotImage", slotImage);
        SetObject(visualObject, "stateLabel", stateLabel);
        SetObject(visualObject, "outline", outline);
        visualObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(visual);
    }

    private static void AuthorUiItemPrefab(GameObject root, InventoryTheme theme)
    {
        InventoryItem item = root.GetComponent<InventoryItem>();
        if (item == null)
            throw new InvalidOperationException($"{UiItemPrefabPath} has no InventoryItem component.");

        TMP_Text priceText = null;
        SerializedProperty existingPrice = new SerializedObject(item).FindProperty("priceText");
        if (existingPrice != null)
            priceText = existingPrice.objectReferenceValue as TMP_Text;
        if (priceText == null)
        {
            Transform priceTransform = FindOptional(root.transform, "Price");
            if (priceTransform != null)
                priceText = priceTransform.GetComponent<TMP_Text>();
        }
        if (priceText == null)
            throw new InvalidOperationException($"{UiItemPrefabPath} has no authored price TMP text.");

        priceText.color = theme.textSecondary;
        priceText.fontSize = theme.captionSize;
        priceText.fontStyle = FontStyles.Normal;
        priceText.fontWeight = theme.captionWeight;

        RectTransform upgradeRect = EnsureRectChild(root.transform, "UpgradeLabel");
        upgradeRect.anchorMin = Vector2.one;
        upgradeRect.anchorMax = Vector2.one;
        upgradeRect.pivot = Vector2.one;
        upgradeRect.anchoredPosition = new Vector2(-2f, -2f);
        upgradeRect.sizeDelta = new Vector2(44f, 26f);
        TMP_Text upgradeLabel = EnsureText(upgradeRect, string.Empty, 18f, theme.success);
        upgradeLabel.alignment = TextAlignmentOptions.TopRight;
        upgradeLabel.fontStyle = FontStyles.Normal;
        upgradeLabel.fontWeight = theme.numericWeight;
        Outline labelOutline = EnsureComponent<Outline>(upgradeRect.gameObject);
        labelOutline.effectColor = theme.surfaceLocked;
        labelOutline.effectDistance = new Vector2(1f, -1f);
        labelOutline.useGraphicAlpha = true;
        upgradeRect.gameObject.SetActive(false);

        var itemObject = new SerializedObject(item);
        itemObject.UpdateIfRequiredOrScript();
        SetObject(itemObject, "theme", theme);
        SetObject(itemObject, "priceText", priceText);
        SetObject(itemObject, "upgradeLabel", upgradeLabel);
        itemObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(item);
    }

    private static InventoryTooltip AuthorTooltip(Transform root, Canvas parentCanvas, InventoryTheme theme)
    {
        RectTransform tooltipRect = EnsureRectChild(root, "Tooltip");
        tooltipRect.anchorMin = new Vector2(0.5f, 0.5f);
        tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
        tooltipRect.pivot = Vector2.zero;
        tooltipRect.anchoredPosition = Vector2.zero;
        tooltipRect.sizeDelta = new Vector2(200f, 60f);
        tooltipRect.SetAsLastSibling();

        Image background = EnsureComponent<Image>(tooltipRect.gameObject);
        ConfigureImage(background, theme.panelSprite, theme.surface0, Image.Type.Sliced, false);
        Outline outline = EnsureComponent<Outline>(tooltipRect.gameObject);
        outline.effectColor = theme.borderStrong;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        CanvasGroup canvasGroup = EnsureComponent<CanvasGroup>(tooltipRect.gameObject);
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        VerticalLayoutGroup layout = EnsureComponent<VerticalLayoutGroup>(tooltipRect.gameObject);
        layout.padding = new RectOffset(10, 10, 6, 6);
        layout.spacing = 2f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = EnsureComponent<ContentSizeFitter>(tooltipRect.gameObject);
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform nameRect = EnsureRectChild(tooltipRect, "NameText");
        nameRect.sizeDelta = new Vector2(180f, 22f);
        TMP_Text nameText = EnsureText(nameRect, "ITEM NAME", theme.bodySize, theme.textPrimary);
        nameText.fontStyle = FontStyles.Normal;
        nameText.fontWeight = theme.titleWeight;
        nameText.alignment = TextAlignmentOptions.Left;

        RectTransform infoRect = EnsureRectChild(tooltipRect, "InfoText");
        infoRect.sizeDelta = new Vector2(180f, 18f);
        TMP_Text infoText = EnsureText(infoRect, "$0", theme.labelSize, theme.textSecondary);
        infoText.alignment = TextAlignmentOptions.Left;
        infoText.richText = true;
        infoText.fontWeight = theme.labelWeight;

        InventoryTooltip tooltip = EnsureComponent<InventoryTooltip>(tooltipRect.gameObject);
        var tooltipObject = new SerializedObject(tooltip);
        tooltipObject.UpdateIfRequiredOrScript();
        SetObject(tooltipObject, "theme", theme);
        SetObject(tooltipObject, "tooltipRect", tooltipRect);
        SetObject(tooltipObject, "tooltipCanvasGroup", canvasGroup);
        SetObject(tooltipObject, "nameText", nameText);
        SetObject(tooltipObject, "infoText", infoText);
        SetObject(tooltipObject, "parentCanvas", parentCanvas);
        SetVector2(tooltipObject, "pointerOffset", new Vector2(theme.spacing16, theme.spacing16));
        tooltipObject.ApplyModifiedPropertiesWithoutUndo();
        return tooltip;
    }

    private static ActionSlotNamePopup AuthorActionSlotNamePopup(
        Transform root,
        Canvas parentCanvas,
        InventoryTheme theme)
    {
        RectTransform popupRect = EnsureRectChild(root, "ActionSlotNamePopup");
        popupRect.anchorMin = new Vector2(0.5f, 0.5f);
        popupRect.anchorMax = new Vector2(0.5f, 0.5f);
        popupRect.pivot = new Vector2(0.5f, 0.5f);
        popupRect.anchoredPosition = Vector2.zero;
        popupRect.sizeDelta = new Vector2(200f, 32f);
        popupRect.SetAsLastSibling();

        CanvasGroup canvasGroup = EnsureComponent<CanvasGroup>(popupRect.gameObject);
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        RectTransform textRect = EnsureRectChild(popupRect, "PopupText");
        Stretch(textRect);
        TMP_Text popupText = EnsureText(textRect, string.Empty, 18f, theme.textPrimary);
        popupText.fontStyle = FontStyles.Normal;
        popupText.fontWeight = theme.titleWeight;
        popupText.alignment = TextAlignmentOptions.Center;
        Outline outline = EnsureComponent<Outline>(textRect.gameObject);
        outline.effectColor = theme.surfaceLocked;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        ActionSlotNamePopup popup = EnsureComponent<ActionSlotNamePopup>(popupRect.gameObject);
        var popupObject = new SerializedObject(popup);
        popupObject.UpdateIfRequiredOrScript();
        SetObject(popupObject, "theme", theme);
        SetObject(popupObject, "popupText", popupText);
        SetObject(popupObject, "popupCanvasGroup", canvasGroup);
        SetObject(popupObject, "popupRect", popupRect);
        SetObject(popupObject, "parentCanvas", parentCanvas);
        popupObject.ApplyModifiedPropertiesWithoutUndo();
        return popup;
    }

    private static Outline EnsureSlotOutline(GameObject target, InventoryTheme theme)
    {
        Outline outline = EnsureComponent<Outline>(target);
        outline.effectColor = theme.borderSubtle;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;
        outline.enabled = true;
        return outline;
    }

    private static TMP_Text EnsureText(
        RectTransform rect,
        string content,
        float fontSize,
        Color color)
    {
        TMP_Text text = rect.GetComponent<TMP_Text>();
        if (text == null)
            text = rect.gameObject.AddComponent<TextMeshProUGUI>();

        if (text.font == null && TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        text.text = content;
        text.fontSize = fontSize;
        text.color = color;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        return text;
    }

    private static void ConfigureImage(
        Image image,
        Sprite sprite,
        Color color,
        Image.Type type,
        bool raycastTarget)
    {
        image.sprite = sprite;
        image.color = color;
        image.type = type;
        image.raycastTarget = raycastTarget;
        image.preserveAspect = false;
    }

    private static RectTransform EnsureRectChild(Transform parent, string name)
    {
        Transform existing = FindDirectChild(parent, name);
        if (existing != null)
            return RequireRect(existing);

        var child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    private static RectTransform RequireRect(Transform transform)
    {
        RectTransform rect = transform as RectTransform;
        if (rect == null)
            throw new InvalidOperationException($"{transform.name} is not a RectTransform.");
        return rect;
    }

    private static Transform FindRequired(Transform root, string name)
    {
        Transform found = FindOptional(root, name);
        if (found == null)
            throw new InvalidOperationException($"Could not find '{name}' under '{root.name}'.");
        return found;
    }

    private static Transform FindRequiredDirect(Transform parent, string name)
    {
        Transform found = FindDirectChild(parent, name);
        if (found == null)
            throw new InvalidOperationException($"Could not find direct child '{name}' under '{parent.name}'.");
        return found;
    }

    private static Transform FindOptional(Transform root, string name)
    {
        if (root.name == name)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindOptional(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    private static Transform FindDirectChild(Transform parent, string name)
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
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            throw new InvalidOperationException($"Required prefab is missing: {path}");

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved)
                throw new InvalidOperationException($"Could not save authored prefab: {path}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void SetObject(SerializedObject serializedObject, string fieldName, UnityEngine.Object value)
    {
        SerializedProperty property = RequireProperty(serializedObject, fieldName);
        property.objectReferenceValue = value;
    }

    private static void SetInt(SerializedObject serializedObject, string fieldName, int value)
    {
        SerializedProperty property = RequireProperty(serializedObject, fieldName);
        property.intValue = value;
    }

    private static void SetVector2(SerializedObject serializedObject, string fieldName, Vector2 value)
    {
        SerializedProperty property = RequireProperty(serializedObject, fieldName);
        property.vector2Value = value;
    }

    private static void SetObjectArray(
        SerializedObject serializedObject,
        string fieldName,
        UnityEngine.Object[] values)
    {
        SerializedProperty property = RequireProperty(serializedObject, fieldName);
        if (!property.isArray)
            throw new InvalidOperationException($"{serializedObject.targetObject.GetType().Name}.{fieldName} is not an array/list.");

        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void ResizeArray(SerializedObject serializedObject, string fieldName, int size)
    {
        SerializedProperty property = RequireProperty(serializedObject, fieldName);
        if (!property.isArray)
            throw new InvalidOperationException($"{serializedObject.targetObject.GetType().Name}.{fieldName} is not an array/list.");
        property.arraySize = size;
    }

    private static SerializedProperty RequireProperty(SerializedObject serializedObject, string fieldName)
    {
        SerializedProperty property = serializedObject.FindProperty(fieldName);
        if (property == null)
        {
            throw new InvalidOperationException(
                $"Missing serialized field contract: {serializedObject.targetObject.GetType().Name}.{fieldName}");
        }

        return property;
    }

    private static Sprite LoadRequiredSprite(string fileName)
    {
        return LoadRequiredAsset<Sprite>($"{NineSliceBaker.GeneratedFolder}/{fileName}");
    }

    private static T LoadRequiredAsset<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            throw new InvalidOperationException($"Required asset is missing: {path}");
        return asset;
    }

    private static Color Hex(string rgb, float alpha = 1f)
    {
        if (!ColorUtility.TryParseHtmlString($"#{rgb}", out Color color))
            throw new ArgumentException($"Invalid inventory theme color: {rgb}", nameof(rgb));
        color.a = alpha;
        return color;
    }

    private static void EnsureAssetFolder(string folder)
    {
        string[] parts = folder.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
            throw new ArgumentException($"Asset folder must start with Assets/: {folder}", nameof(folder));

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
