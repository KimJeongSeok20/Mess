using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

public static class InventoryAaaVariantAuthoring
{
    private const string CanvasPath = "Assets/UI/Inventory/InventoryCanvas.prefab";
    private const string CellPath = "Assets/Scripts/Inventory/Cell.prefab";
    private const string ItemPath = "Assets/Scripts/Inventory/UI/UIitem.prefab";
    private const string ThemePath = "Assets/UI/Inventory/InventoryTheme.asset";
    private const string NeutralFramePath = "Assets/UI/Inventory/Textures/FrameThinNeutral.png";
    private const string CleanExtractionCurrencyTexturePath = "Assets/UI/Inventory/Textures/CurrencySalvageWashers.png";
    private const string CleanExtractionFontSourcePath = "Assets/UI/Fonts/Vitals/BarlowCondensed-SemiBold.ttf";
    private const string CleanExtractionFontFolder = "Assets/UI/Inventory/Fonts";
    private const string CleanExtractionFontPath = CleanExtractionFontFolder + "/BarlowCondensed-SemiBold Inventory SDF.asset";

    private enum Concept
    {
        GearBoard,
        SideDrawer,
        FramelessDeck,
        CleanExtraction
    }

    [MenuItem("Tools/UI/Inventory Variants/Apply A - Asymmetric Gear Board")]
    public static void ApplyGearBoard()
    {
        Apply(Concept.GearBoard);
    }

    [MenuItem("Tools/UI/Inventory Variants/Apply B - Compact Side Drawer")]
    public static void ApplySideDrawer()
    {
        Apply(Concept.SideDrawer);
    }

    [MenuItem("Tools/UI/Inventory Variants/Apply C - Frameless Equipment Deck")]
    public static void ApplyFramelessDeck()
    {
        Apply(Concept.FramelessDeck);
    }

    [MenuItem("Tools/UI/Inventory Variants/Apply D - Clean Extraction Centered")]
    public static void ApplyCleanExtraction()
    {
        Apply(Concept.CleanExtraction);
    }

    private static void Apply(Concept concept)
    {
        NormalizeCanvasHierarchy();
        InventoryAaaAuthoring.ApplyFieldKitRedesign();

        InventoryTheme theme = AssetDatabase.LoadAssetAtPath<InventoryTheme>(ThemePath);
        if (theme == null)
            throw new InvalidOperationException($"Inventory theme is missing: {ThemePath}");

        ConfigureTheme(theme, concept);
        if (concept == Concept.CleanExtraction)
        {
            TMP_FontAsset inventoryFont = GetOrCreateCleanExtractionFont();
            theme.titleFont = inventoryFont;
            theme.bodyFont = inventoryFont;

            Sprite neutralFrame = AssetDatabase.LoadAssetAtPath<Sprite>(NeutralFramePath);
            if (neutralFrame == null)
                throw new InvalidOperationException($"ImageGen neutral frame is missing: {NeutralFramePath}");
            theme.panelOutlineSprite = neutralFrame;
            theme.slotOutlineSprite = neutralFrame;
        }
        EditPrefab(CanvasPath, root =>
        {
            StyleCanvas(root, theme, concept);
            ApplyConceptFont(root.transform, theme, concept);
        });
        EditPrefab(CellPath, root =>
        {
            StyleCell(root, theme, concept);
            ApplyConceptFont(root.transform, theme, concept);
        });
        EditPrefab(ItemPath, root =>
        {
            StyleItem(root, theme, concept);
            ApplyConceptFont(root.transform, theme, concept);
        });

        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[InventoryAaaVariantAuthoring] Applied {concept}.");
    }

    private static TMP_FontAsset GetOrCreateCleanExtractionFont()
    {
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(CleanExtractionFontPath);
        if (existing != null &&
            existing.HasCharacter('E') &&
            existing.HasCharacter('0') &&
            existing.HasCharacter('['))
        {
            return existing;
        }

        if (existing != null && !AssetDatabase.DeleteAsset(CleanExtractionFontPath))
            throw new InvalidOperationException($"Could not replace incomplete inventory font: {CleanExtractionFontPath}");

        EnsureAssetFolder(CleanExtractionFontFolder);

        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(CleanExtractionFontSourcePath);
        if (sourceFont == null)
            throw new InvalidOperationException($"Barlow Condensed source font is missing: {CleanExtractionFontSourcePath}");

        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            90,
            9,
            GlyphRenderMode.SDFAA,
            1024,
            1024,
            AtlasPopulationMode.Dynamic,
            false);
        if (fontAsset == null)
            throw new InvalidOperationException($"TMP could not create an inventory font from {CleanExtractionFontSourcePath}");

        fontAsset.name = "BarlowCondensed-SemiBold Inventory SDF";
        string basicLatin = BuildBasicLatinCharacters();
        if (!fontAsset.TryAddCharacters(basicLatin, out string missingCharacters))
        {
            UnityEngine.Object.DestroyImmediate(fontAsset);
            throw new InvalidOperationException($"TMP could not add all inventory glyphs. Missing: {missingCharacters}");
        }

        fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
        fontAsset.isMultiAtlasTexturesEnabled = false;

        Texture2D atlas = fontAsset.atlasTextures[0];
        atlas.name = "BarlowCondensed-SemiBold Inventory Atlas";
        fontAsset.material.name = "BarlowCondensed-SemiBold Inventory Atlas Material";

        AssetDatabase.CreateAsset(fontAsset, CleanExtractionFontPath);
        AssetDatabase.AddObjectToAsset(atlas, fontAsset);
        AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        EditorUtility.SetDirty(fontAsset);
        return fontAsset;
    }

    private static string BuildBasicLatinCharacters()
    {
        char[] characters = new char[95];
        for (int i = 0; i < characters.Length; i++)
            characters[i] = (char)(32 + i);
        return new string(characters);
    }

    private static void ApplyConceptFont(Transform root, InventoryTheme theme, Concept concept)
    {
        if (concept != Concept.CleanExtraction || theme.titleFont == null)
            return;

        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            text.font = theme.titleFont;
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        string[] segments = folderPath.Split('/');
        string current = segments[0];

        for (int i = 1; i < segments.Length; i++)
        {
            string next = $"{current}/{segments[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[i]);
            current = next;
        }
    }

    private static void ConfigureTheme(InventoryTheme theme, Concept concept)
    {
        switch (concept)
        {
            case Concept.GearBoard:
                theme.dim = Hex("020506", 0.78f);
                theme.surface0 = Hex("071013", 0.97f);
                theme.surface1 = Hex("0D191D", 0.98f);
                theme.surface2 = Hex("14272B", 1f);
                theme.surface3 = Hex("1E373C", 1f);
                theme.borderSubtle = Hex("34545A", 0.66f);
                theme.borderStrong = Hex("8CC5C8", 0.94f);
                theme.textPrimary = Hex("ECF4F2", 1f);
                theme.textSecondary = Hex("91AAA8", 1f);
                theme.textDisabled = Hex("536967", 1f);
                theme.accent = Hex("78E0D4", 1f);
                theme.accentDim = Hex("78E0D4", 0.15f);
                break;

            case Concept.SideDrawer:
                theme.dim = Hex("010203", 0.68f);
                theme.surface0 = Hex("0B0D10", 0.985f);
                theme.surface1 = Hex("12161A", 0.99f);
                theme.surface2 = Hex("1D242A", 1f);
                theme.surface3 = Hex("29343B", 1f);
                theme.borderSubtle = Hex("4A545B", 0.60f);
                theme.borderStrong = Hex("D9B46D", 0.92f);
                theme.textPrimary = Hex("F2EEE5", 1f);
                theme.textSecondary = Hex("AAA69C", 1f);
                theme.textDisabled = Hex("66645F", 1f);
                theme.accent = Hex("E7B967", 1f);
                theme.accentDim = Hex("E7B967", 0.13f);
                break;

            case Concept.CleanExtraction:
                // Reference D is deliberately neutral graphite.  Keep every surface and
                // outline on the gray axis so outdoor lighting cannot reveal a cyan cast.
                theme.dim = Hex("030303", 0.62f);
                theme.surface0 = Hex("111214", 0.96f);
                theme.surface1 = Hex("151517", 0.98f);
                theme.surface2 = Hex("17181A", 1f);
                theme.surface3 = Hex("222325", 1f);
                theme.surfaceLocked = Hex("141517", 1f);
                theme.borderSubtle = Hex("505153", 0.80f);
                theme.borderStrong = Hex("C9C7C2", 0.88f);
                theme.borderLocked = Hex("595A5C", 0.80f);
                theme.textPrimary = Hex("F1F0EC", 1f);
                theme.textSecondary = Hex("AAA8A3", 1f);
                theme.textDisabled = Hex("686662", 1f);
                theme.accent = Hex("F28A32", 1f);
                theme.accentDim = Hex("F28A32", 0.16f);
                theme.success = Hex("96B653", 1f);
                break;

            default:
                theme.dim = Hex("020306", 0.72f);
                theme.surface0 = Hex("080A10", 0.88f);
                theme.surface1 = Hex("0D111A", 0.92f);
                theme.surface2 = Hex("151D2A", 0.98f);
                theme.surface3 = Hex("202C3B", 1f);
                theme.borderSubtle = Hex("3B4B60", 0.52f);
                theme.borderStrong = Hex("B5CEFF", 0.92f);
                theme.textPrimary = Hex("F0F4FF", 1f);
                theme.textSecondary = Hex("98A5BA", 1f);
                theme.textDisabled = Hex("566174", 1f);
                theme.accent = Hex("9CBFFF", 1f);
                theme.accentDim = Hex("769FE8", 0.14f);
                break;
        }
    }

    private static void StyleCanvas(GameObject root, InventoryTheme theme, Concept concept)
    {
        CleanupConceptObjects(root.transform);

        Transform inventory = FindRequired(root.transform, "inventory");
        Transform panel = FindRequired(inventory, "Panel");
        Transform header = FindRequired(panel, "Header");
        Transform body = FindRequired(panel, "Body");
        Transform footer = FindRequired(panel, "Footer");
        Transform viewport = FindRequired(body, "GridViewport");
        Transform grid = FindRequired(viewport, "Grid");
        Transform detail = FindRequired(body, "DetailPanel");
        Transform quickBar = FindRequired(inventory, "QuickSlotBar");

        HorizontalLayoutGroup bodyLayout = body.GetComponent<HorizontalLayoutGroup>();
        if (bodyLayout != null)
            bodyLayout.enabled = false;

        SetActiveDeep(panel, "SurfaceSheen", false);
        SetActiveDeep(panel, "LeftSignal", false);
        SetActiveDeep(panel, "PanelOutline", false);
        SetActiveDeep(panel, "HeaderDivider", false);
        SetActiveDeep(panel, "EyebrowText", false);
        SetActiveDeep(panel, "ModuleOutline", false);
        SetActiveDeep(panel, "DossierAccent", false);
        SetActiveDeep(panel, "DossierLabel", false);
        SetActiveDeep(panel, "ControlHintText", false);
        SetActiveDeep(inventory, "QuickAccessLabel", false);
        SetActiveDeep(quickBar, "TopSignal", false);

        Image panelImage = RequireImage(panel);
        panelImage.sprite = null;
        panelImage.type = Image.Type.Simple;
        panelImage.color = Color.clear;

        Stretch(RequireRect(body));

        switch (concept)
        {
            case Concept.GearBoard:
                StyleGearBoard(panel, header, footer, viewport, grid, detail, quickBar, theme);
                break;
            case Concept.SideDrawer:
                StyleSideDrawer(panel, header, footer, viewport, grid, detail, quickBar, theme);
                break;
            case Concept.FramelessDeck:
                StyleFramelessDeck(panel, header, footer, viewport, grid, detail, quickBar, theme);
                break;
            default:
                StyleCleanExtraction(inventory, panel, header, footer, viewport, grid, detail, quickBar, theme);
                break;
        }

        foreach (ActionSlot slot in quickBar.GetComponentsInChildren<ActionSlot>(true))
            StyleActionSlot(slot, theme, concept);
    }

    private static void StyleGearBoard(
        Transform panel,
        Transform header,
        Transform footer,
        Transform viewport,
        Transform grid,
        Transform detail,
        Transform quickBar,
        InventoryTheme theme)
    {
        SetRect(RequireRect(panel), Center, Center, Center, new Vector2(0f, 42f), new Vector2(1370f, 720f));

        SetRect(RequireRect(header), Center, Center, Center, new Vector2(-356f, 304f), new Vector2(646f, 72f));
        StyleHeader(header, theme, "CARRY SYSTEM", "ACTIVE FIELD CACHE", false);

        SetRect(RequireRect(viewport), Center, Center, Center, new Vector2(-250f, 16f), new Vector2(790f, 430f));
        ConfigureImage(RequireImage(viewport), theme.panelSprite, theme.surface0, Image.Type.Sliced, false);
        AddOutline(viewport, theme, new Vector2(-2f, -2f), Hex("78E0D4", 0.22f));
        AddRule(viewport, "Concept_A_TopRail", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -1f), new Vector2(-118f, 2f), theme.accent);
        AddLabel(viewport, "Concept_A_GridLabel", "GEAR ARRAY  /  32", 12f, theme.accent,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(300f, 18f), TextAlignmentOptions.TopLeft, 2f);

        SetGrid(grid, new Vector2(18f, -46f), new Vector2(754f, 364f), new Vector2(84f, 84f), new Vector2(10f, 9f), 8);

        SetRect(RequireRect(detail), Center, Center, Center, new Vector2(444f, 28f), new Vector2(374f, 506f));
        ConfigureImage(RequireImage(detail), theme.panelSprite, theme.surface1, Image.Type.Sliced, false);
        StyleDetailVertical(detail, theme, "SELECTED OBJECT", 118f, 30f, 142f, 184f);
        AddRule(detail, "Concept_A_DetailRail", new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(0f, 0.5f), new Vector2(1f, 0f), new Vector2(3f, -54f), theme.accent);

        SetRect(RequireRect(footer), Center, Center, Center, new Vector2(-250f, -238f), new Vector2(790f, 42f));
        ConfigureImage(RequireImage(footer), theme.panelSprite, Hex("081114", 0.92f), Image.Type.Sliced, false);
        StyleFooter(footer, theme, "FIELD MASS");

        SetRect(RequireRect(quickBar), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 24f), new Vector2(326f, 80f));
        StyleQuickBar(quickBar, theme, 9, 7, 6f, Color.clear);
        AddLabel(quickBar.parent, "Concept_A_QuickLabel", "DIRECT ACCESS", 11f, theme.textSecondary,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 109f), new Vector2(220f, 18f), TextAlignmentOptions.Center, 2.6f);

        AddCornerMarks(panel, theme.accent, 1370f, 720f);
    }

    private static void StyleSideDrawer(
        Transform panel,
        Transform header,
        Transform footer,
        Transform viewport,
        Transform grid,
        Transform detail,
        Transform quickBar,
        InventoryTheme theme)
    {
        SetRect(RequireRect(panel), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-42f, 10f), new Vector2(690f, 780f));
        Image panelImage = RequireImage(panel);
        panelImage.sprite = theme.panelSprite;
        panelImage.type = Image.Type.Sliced;
        panelImage.color = Hex("080A0D", 0.94f);

        SetRect(RequireRect(header), new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f),
            new Vector2(0f, 0f), new Vector2(-70f, 94f));
        StyleHeader(header, theme, "CARRY / 032", "BLACKSITE LOADOUT", true);

        SetRect(RequireRect(detail), new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f),
            new Vector2(-35f, -112f), new Vector2(-106f, 190f));
        ConfigureImage(RequireImage(detail), theme.panelSprite, theme.surface1, Image.Type.Sliced, false);
        StyleDetailHorizontal(detail, theme);

        SetRect(RequireRect(viewport), new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f),
            new Vector2(-35f, -326f), new Vector2(-106f, 342f));
        ConfigureImage(RequireImage(viewport), null, Color.clear, Image.Type.Simple, false);
        AddLabel(viewport, "Concept_B_GridLabel", "STORAGE MATRIX", 11f, theme.textSecondary,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -3f), new Vector2(260f, 18f), TextAlignmentOptions.TopLeft, 2.2f);
        AddRule(viewport, "Concept_B_GridRule", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(0f, 1f), theme.borderSubtle);
        SetGrid(grid, new Vector2(0f, -42f), new Vector2(584f, 442f), new Vector2(66f, 66f), new Vector2(7f, 7f), 8);

        SetRect(RequireRect(footer), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-35f, 25f), new Vector2(-106f, 46f));
        ConfigureImage(RequireImage(footer), null, Color.clear, Image.Type.Simple, false);
        StyleFooter(footer, theme, "TOTAL LOAD");

        SetRect(RequireRect(quickBar), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(68f, 40f), new Vector2(326f, 80f));
        StyleQuickBar(quickBar, theme, 9, 7, 6f, Hex("080A0D", 0.82f));
        AddLabel(quickBar.parent, "Concept_B_QuickLabel", "1 — 4  READY", 11f, theme.accent,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(68f, 126f), new Vector2(326f, 18f), TextAlignmentOptions.Center, 2.2f);

        AddRule(panel, "Concept_B_Spine", new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(0f, 0.5f), new Vector2(-1f, 0f), new Vector2(3f, -30f), theme.accent);
    }

    private static void StyleFramelessDeck(
        Transform panel,
        Transform header,
        Transform footer,
        Transform viewport,
        Transform grid,
        Transform detail,
        Transform quickBar,
        InventoryTheme theme)
    {
        SetRect(RequireRect(panel), Center, Center, Center, new Vector2(0f, 34f), new Vector2(1480f, 760f));

        SetRect(RequireRect(header), Center, Center, Center, new Vector2(-470f, 320f), new Vector2(430f, 66f));
        StyleHeader(header, theme, "EQUIPMENT", "OPERATIVE DECK", false);

        SetRect(RequireRect(detail), Center, Center, Center, new Vector2(-435f, 20f), new Vector2(438f, 544f));
        ConfigureImage(RequireImage(detail), theme.panelSprite, Hex("090C13", 0.84f), Image.Type.Sliced, false);
        StyleDetailVertical(detail, theme, "FOCUS ITEM", 146f, 42f, 174f, 214f);
        AddOutline(detail, theme, Vector2.zero, Hex("9CBFFF", 0.26f));

        SetRect(RequireRect(viewport), Center, Center, Center, new Vector2(300f, 30f), new Vector2(820f, 454f));
        ConfigureImage(RequireImage(viewport), null, Color.clear, Image.Type.Simple, false);
        AddLabel(viewport, "Concept_C_GridLabel", "CARRY DECK", 12f, theme.accent,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -4f), new Vector2(280f, 18f), TextAlignmentOptions.TopLeft, 2.5f);
        AddLabel(viewport, "Concept_C_Index", "01   02   03   04   05   06   07   08", 10f, theme.textDisabled,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -29f), new Vector2(0f, 16f), TextAlignmentOptions.Center, 3.1f);
        SetGrid(grid, new Vector2(0f, -58f), new Vector2(820f, 380f), new Vector2(92f, 80f), new Vector2(12f, 12f), 8);

        SetRect(RequireRect(footer), Center, Center, Center, new Vector2(300f, -236f), new Vector2(820f, 44f));
        ConfigureImage(RequireImage(footer), null, Color.clear, Image.Type.Simple, false);
        StyleFooter(footer, theme, "CARRIED");
        AddRule(footer, "Concept_C_FooterRule", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 1f), theme.borderSubtle);

        SetRect(RequireRect(quickBar), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 30f), new Vector2(342f, 86f));
        StyleQuickBar(quickBar, theme, 11, 8, 7f, Color.clear);
        AddLabel(quickBar.parent, "Concept_C_QuickLabel", "TACTICAL CHANNEL", 10f, theme.textSecondary,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(260f, 18f), TextAlignmentOptions.Center, 3f);

        AddRule(panel, "Concept_C_TopRule", new Vector2(0.5f, 1f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(0f, -20f), new Vector2(-790f, 1f), Hex("9CBFFF", 0.36f));
    }

    private static void StyleCleanExtraction(
        Transform inventory,
        Transform panel,
        Transform header,
        Transform footer,
        Transform viewport,
        Transform grid,
        Transform detail,
        Transform quickBar,
        InventoryTheme theme)
    {
        // The approved composition is one compact cluster.  The left storage module and
        // the right detail module are balanced around screen centre as a single unit.
        const float leftWidth = 650f;
        const float detailWidth = 205f;
        const float moduleGap = 3f;
        const float clusterWidth = leftWidth + moduleGap + detailWidth;
        float leftCenterX = -(clusterWidth * 0.5f) + (leftWidth * 0.5f);
        float detailCenterX = (clusterWidth * 0.5f) - (detailWidth * 0.5f);

        SetRect(RequireRect(panel), Center, Center, Center, new Vector2(-5f, 60f), new Vector2(clusterWidth, 520f));
        ConfigureImage(RequireImage(panel), null, Color.clear, Image.Type.Simple, false);

        // The reference keeps the open space above the inspection card transparent.  Back
        // only the left module and the narrow horizontal seam so the modules read as one
        // assembly without creating a solid cap over the detail card.
        RectTransform clusterBackplate = EnsureRectChild(panel, "Concept_D_ClusterBackplate");
        SetRect(clusterBackplate, Center, Center, Center, new Vector2(leftCenterX, -11.5f), new Vector2(leftWidth, 459f));
        ConfigureImage(RequireImage(clusterBackplate), theme.panelSprite, Hex("111111", 1f), Image.Type.Sliced, false);

        RectTransform moduleSeam = EnsureRectChild(panel, "Concept_D_ModuleSeam");
        SetRect(moduleSeam, Center, Center, Center,
            new Vector2(leftCenterX + (leftWidth * 0.5f) + (moduleGap * 0.5f), -42f),
            new Vector2(moduleGap, 398f));
        ConfigureImage(RequireImage(moduleSeam), null, Hex("111111", 1f), Image.Type.Simple, false);
        moduleSeam.SetAsFirstSibling();
        clusterBackplate.SetAsFirstSibling();

        Transform dim = FindRequired(inventory, "DimOverlay");
        ConfigureImage(RequireImage(dim), null, theme.dim, Image.Type.Simple, true);

        SetRect(RequireRect(header), Center, Center, Center, new Vector2(leftCenterX, 190f), new Vector2(leftWidth, 56f));
        ConfigureImage(RequireImage(header), null, Color.clear, Image.Type.Simple, false);
        Transform strip = FindRequired(header, "Strip");
        Stretch(RequireRect(strip));
        ConfigureImage(RequireImage(strip), null, Hex("151618", 1f), Image.Type.Simple, false);
        AddOutline(header, theme, Vector2.zero, Hex("5A5956", 0.58f));

        TMP_Text title = RequireText(FindRequired(header, "TitleText"));
        SetRect(RequireRect(title.transform), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(26f, 0f), new Vector2(245f, 36f));
        title.text = "EQUIPMENT";
        title.fontSize = 20f;
        title.characterSpacing = 1.2f;
        title.color = theme.textPrimary;
        title.alignment = TextAlignmentOptions.MidlineLeft;

        TMP_Text count = RequireText(FindRequired(header, "SlotCountText"));
        count.gameObject.SetActive(true);
        SetRect(RequireRect(count.transform), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-14f, 0f), new Vector2(70f, 30f));
        count.text = "12 / 24";
        count.fontSize = 13f;
        count.characterSpacing = 0.5f;
        count.color = theme.textSecondary;
        count.alignment = TextAlignmentOptions.MidlineRight;

        FindRequired(header, "CloseButton").gameObject.SetActive(false);
        FindRequired(header, "EyebrowText").gameObject.SetActive(false);

        Transform currency = FindRequired(panel, "Currency");
        currency.SetParent(header, false);
        currency.gameObject.SetActive(true);
        SetRect(RequireRect(currency), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-132f, 0f), new Vector2(112f, 30f));
        ConfigureImage(RequireImage(currency), null, Color.clear, Image.Type.Simple, false);
        TMP_Text currencyText = RequireText(FindRequired(currency, "CurrencyText"));
        SetRect(RequireRect(currencyText.transform), Vector2.zero, Vector2.one, new Vector2(1f, 0.5f),
            new Vector2(-28f, 0f), new Vector2(-30f, 0f));
        currencyText.fontSize = 16f;
        currencyText.color = theme.textPrimary;
        currencyText.alignment = TextAlignmentOptions.MidlineRight;
        Transform coin = FindRequired(currency, "RawImage");
        SetRect(RequireRect(coin), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(0f, -2f), new Vector2(24f, 24f));
        RawImage currencyIcon = coin.GetComponent<RawImage>();
        if (currencyIcon == null)
            throw new InvalidOperationException("Currency/RawImage is missing its RawImage component.");
        Texture2D currencyTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(CleanExtractionCurrencyTexturePath);
        if (currencyTexture == null)
            throw new InvalidOperationException($"Clean Extraction currency texture is missing: {CleanExtractionCurrencyTexturePath}");
        currencyIcon.texture = currencyTexture;
        currencyIcon.color = Color.white;
        currencyIcon.uvRect = new Rect(0f, 0f, 1f, 1f);
        currencyIcon.raycastTarget = false;

        SetRect(RequireRect(viewport), Center, Center, Center, new Vector2(leftCenterX, -42f), new Vector2(leftWidth, 398f));
        ConfigureImage(RequireImage(viewport), null, Hex("141414", 1f), Image.Type.Simple, false);
        AddOutline(viewport, theme, Vector2.zero, Hex("444444", 0.90f));
        SetGrid(grid, new Vector2(14f, -4f), new Vector2(623f, 389f), new Vector2(98f, 92f), new Vector2(7f, 7f), 6);

        SetRect(RequireRect(detail), Center, Center, Center, new Vector2(detailCenterX, -42f), new Vector2(detailWidth, 398f));
        ConfigureImage(RequireImage(detail), null, Hex("151617", 0.97f), Image.Type.Simple, false);
        AddOutline(detail, theme, Vector2.zero, Hex("5A5956", 0.56f));
        AddNamedOutline(detail, "Concept_D_AccentOutline", theme, new Vector2(-3f, -3f), Hex("F28A32", 0.30f));

        Transform plate = FindRequired(detail, "IconPlate");
        plate.gameObject.SetActive(true);
        SetRect(RequireRect(plate), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -9f), new Vector2(-18f, 212f));
        ConfigureImage(RequireImage(plate), null, Hex("131416", 0.90f), Image.Type.Simple, false);
        Transform icon = FindRequired(detail, "DetailIcon");
        SetRect(RequireRect(icon), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -25f), new Vector2(160f, 169f));

        SetDetailText(detail, "DetailName", new Vector2(16f, -229f), new Vector2(173f, 34f), 22f, theme.textPrimary);
        TMP_Text detailName = RequireText(FindRequired(detail, "DetailName"));
        detailName.alignment = TextAlignmentOptions.MidlineLeft;
        FindRequired(detail, "DetailCategory").gameObject.SetActive(false);
        FindRequired(detail, "DetailRarity").gameObject.SetActive(false);
        FindRequired(detail, "DetailDescription").gameObject.SetActive(false);

        Transform detailFooter = FindRequired(detail, "DetailFooter");
        SetRect(RequireRect(detailFooter), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(16f, -264f), new Vector2(173f, 25f));
        Transform price = FindRequired(detailFooter, "PriceText");
        price.gameObject.SetActive(false);
        TMP_Text weight = RequireText(FindRequired(detailFooter, "WeightText"));
        weight.gameObject.SetActive(true);
        Stretch(RequireRect(weight.transform));
        weight.fontSize = 14f;
        weight.color = theme.textSecondary;
        weight.alignment = TextAlignmentOptions.MidlineLeft;

        Transform divider = FindRequired(detail, "DetailDivider");
        divider.gameObject.SetActive(false);

        Transform stats = FindRequired(detail, "DetailStats");
        stats.gameObject.SetActive(false);

        RectTransform conditionRoot = EnsureRectChild(detail, "Concept_D_ConditionPips");
        SetRect(conditionRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(16f, -296f), new Vector2(96f, 10f));
        Image[] conditionPips = new Image[5];
        for (int i = 0; i < conditionPips.Length; i++)
        {
            RectTransform pip = EnsureRectChild(conditionRoot, $"Pip{i + 1:00}");
            SetRect(pip, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(i * 17f, 0f), new Vector2(11f, 9f));
            conditionPips[i] = RequireImage(pip);
            ConfigureImage(conditionPips[i], null, WithAlpha(theme.success, 0.90f), Image.Type.Simple, false);
        }

        RectTransform dropButton = EnsureRectChild(detail, "Concept_D_DropButton");
        SetRect(dropButton, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 9f), new Vector2(-16f, 45f));
        ConfigureImage(RequireImage(dropButton), null, Hex("1B1C1E", 0.98f), Image.Type.Simple, false);
        AddOutline(dropButton, theme, Vector2.zero, Hex("5D5C59", 0.72f));
        TMP_Text dropHint = AddLabel(dropButton, "DropHint", "DROP [G]", 14f, theme.accent,
            Vector2.zero, Vector2.one, Center, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center, 0.8f);

        TMP_Text empty = RequireText(FindRequired(detail, "EmptyStateText"));
        SetRect(RequireRect(empty.transform), Center, Center, Center, new Vector2(0f, -18f), new Vector2(190f, 50f));
        empty.text = "SELECT AN ITEM";
        empty.fontSize = 13f;
        empty.characterSpacing = 1.8f;
        empty.color = theme.textDisabled;
        empty.alignment = TextAlignmentOptions.Center;

        InventoryDetailPanel detailPanel = RequireComponent<InventoryDetailPanel>(detail.gameObject);
        SetString(detailPanel, "emptyStateMessage", "SELECT AN ITEM");
        SetObjectReferenceArray(detailPanel, "conditionPips", conditionPips);
        SetObjectArray(detailPanel, "contentOnlyObjects", plate.gameObject, icon.gameObject, detailName.gameObject,
            detailFooter.gameObject, conditionRoot.gameObject, dropButton.gameObject);

        footer.gameObject.SetActive(false);

        SetRect(RequireRect(quickBar), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 18f), new Vector2(310f, 78f));
        StyleQuickBar(quickBar, theme, 9, 7, 7f, Color.clear);

        InventoryManagerExtensions extension = inventory.GetComponent<InventoryManagerExtensions>();
        if (extension == null)
            throw new InvalidOperationException("InventoryManagerExtensions is missing from the inventory root.");
        SetInt(extension, "actionSlotCount", 4);
        SetInt(extension, "defaultCellSlots", 12);
        SetInt(extension, "absoluteMaxCells", 24);
        SetBool(extension, "useTacticalProgressUi", false);
        SetObject(extension, "maxSlotsText", count);
        SetObject(extension, "tacticalProgressText", null);
        SetObject(extension, "totalWeightText", null);
        SetObject(extension, "theme", theme);
    }

    private static void StyleHeader(Transform header, InventoryTheme theme, string titleValue, string eyebrowValue, bool rightAligned)
    {
        Transform strip = FindRequired(header, "Strip");
        Stretch(RequireRect(strip));
        ConfigureImage(RequireImage(strip), theme.panelSprite, Hex("090E12", 0.80f), Image.Type.Sliced, false);

        TMP_Text title = RequireText(FindRequired(header, "TitleText"));
        SetRect(RequireRect(title.transform),
            rightAligned ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f),
            rightAligned ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f),
            rightAligned ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f),
            rightAligned ? new Vector2(-96f, 10f) : new Vector2(22f, 10f), new Vector2(440f, 40f));
        title.text = titleValue;
        title.fontSize = 30f;
        title.color = theme.textPrimary;
        title.characterSpacing = 2.1f;
        title.alignment = rightAligned ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;

        TMP_Text eyebrow = RequireText(FindRequired(header, "EyebrowText"));
        eyebrow.gameObject.SetActive(true);
        SetRect(RequireRect(eyebrow.transform),
            rightAligned ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f),
            rightAligned ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f),
            rightAligned ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f),
            rightAligned ? new Vector2(-96f, -19f) : new Vector2(23f, -19f), new Vector2(440f, 18f));
        eyebrow.text = eyebrowValue;
        eyebrow.fontSize = 10f;
        eyebrow.color = theme.accent;
        eyebrow.characterSpacing = 2.4f;
        eyebrow.alignment = rightAligned ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;

        TMP_Text count = RequireText(FindRequired(header, "SlotCountText"));
        count.gameObject.SetActive(!rightAligned);
        SetRect(RequireRect(count.transform), Vector2.one, Vector2.one, Vector2.one,
            new Vector2(-54f, -12f), new Vector2(116f, 28f));
        count.fontSize = 14f;
        count.color = theme.textSecondary;
        count.alignment = TextAlignmentOptions.MidlineRight;

        Transform close = FindRequired(header, "CloseButton");
        SetRect(RequireRect(close), Vector2.one, Vector2.one, Vector2.one,
            new Vector2(-10f, -10f), new Vector2(34f, 34f));
        ConfigureImage(RequireImage(close), theme.buttonSprite, theme.surface2, Image.Type.Sliced, true);
    }

    private static void StyleDetailVertical(
        Transform detail,
        InventoryTheme theme,
        string labelValue,
        float plateSize,
        float left,
        float textLeft,
        float dividerY)
    {
        float detailWidth = RequireRect(detail).rect.width;
        TMP_Text label = RequireText(FindRequired(detail, "DossierLabel"));
        label.gameObject.SetActive(true);
        label.text = labelValue;
        label.color = theme.accent;
        label.fontSize = 11f;
        label.characterSpacing = 2.2f;
        SetRect(RequireRect(label.transform), new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f),
            new Vector2(left, -18f), new Vector2(-(left * 2f), 18f));

        Transform plate = FindRequired(detail, "IconPlate");
        plate.gameObject.SetActive(true);
        SetRect(RequireRect(plate), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(left, -52f), new Vector2(plateSize, plateSize));
        ConfigureImage(RequireImage(plate), theme.slotSprite, theme.surface0, Image.Type.Sliced, false);

        Transform icon = FindRequired(detail, "DetailIcon");
        SetRect(RequireRect(icon), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(left + 14f, -66f), new Vector2(plateSize - 28f, plateSize - 28f));

        float textWidth = Mathf.Max(120f, detailWidth - textLeft - 20f);
        SetDetailText(detail, "DetailName", new Vector2(textLeft, -58f), new Vector2(textWidth, 38f), 27f, theme.textPrimary);
        SetDetailText(detail, "DetailCategory", new Vector2(textLeft, -102f), new Vector2(textWidth, 18f), 11f, theme.textSecondary);
        SetDetailText(detail, "DetailRarity", new Vector2(textLeft, -127f), new Vector2(textWidth, 18f), 12f, theme.accent);

        Transform divider = FindRequired(detail, "DetailDivider");
        divider.gameObject.SetActive(true);
        SetRect(RequireRect(divider), new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f),
            new Vector2(0f, -dividerY), new Vector2(-48f, 1f));
        ConfigureImage(RequireImage(divider), theme.dividerSprite, theme.borderSubtle, Image.Type.Simple, false);

        Transform stats = FindRequired(detail, "DetailStats");
        stats.gameObject.SetActive(true);
        SetRect(RequireRect(stats), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -dividerY - 18f), new Vector2(detailWidth - 48f, 128f));
        Transform description = FindRequired(detail, "DetailDescription");
        description.gameObject.SetActive(true);
        SetRect(RequireRect(description), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -dividerY - 158f), new Vector2(detailWidth - 48f, 82f));
        Transform detailFooter = FindRequired(detail, "DetailFooter");
        SetRect(RequireRect(detailFooter), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 18f), new Vector2(-48f, 30f));
    }

    private static void StyleDetailHorizontal(Transform detail, InventoryTheme theme)
    {
        float detailWidth = RequireRect(detail).rect.width;
        TMP_Text label = RequireText(FindRequired(detail, "DossierLabel"));
        label.gameObject.SetActive(true);
        label.text = "ACTIVE OBJECT";
        label.fontSize = 10f;
        label.color = theme.accent;
        label.characterSpacing = 2.2f;
        SetRect(RequireRect(label.transform), new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f),
            new Vector2(18f, -14f), new Vector2(-36f, 16f));

        Transform plate = FindRequired(detail, "IconPlate");
        plate.gameObject.SetActive(true);
        SetRect(RequireRect(plate), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(18f, -42f), new Vector2(124f, 124f));
        ConfigureImage(RequireImage(plate), theme.slotSprite, theme.surface0, Image.Type.Sliced, false);
        Transform icon = FindRequired(detail, "DetailIcon");
        SetRect(RequireRect(icon), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(30f, -54f), new Vector2(100f, 100f));

        float textWidth = Mathf.Max(160f, detailWidth - 184f);
        SetDetailText(detail, "DetailName", new Vector2(164f, -45f), new Vector2(textWidth, 34f), 25f, theme.textPrimary);
        SetDetailText(detail, "DetailCategory", new Vector2(164f, -84f), new Vector2(textWidth, 18f), 11f, theme.textSecondary);
        SetDetailText(detail, "DetailRarity", new Vector2(164f, -108f), new Vector2(textWidth, 18f), 12f, theme.accent);

        Transform divider = FindRequired(detail, "DetailDivider");
        divider.gameObject.SetActive(false);
        FindRequired(detail, "DetailStats").gameObject.SetActive(false);
        FindRequired(detail, "DetailDescription").gameObject.SetActive(false);
        Transform detailFooter = FindRequired(detail, "DetailFooter");
        SetRect(RequireRect(detailFooter), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(164f, 20f), new Vector2(-184f, 30f));
    }

    private static void StyleFooter(Transform footer, InventoryTheme theme, string weightLabel)
    {
        TMP_Text weight = RequireText(FindRequired(footer, "TotalWeightText"));
        SetRect(RequireRect(weight.transform), Vector2.zero, new Vector2(0.5f, 1f), new Vector2(0f, 0.5f),
            new Vector2(18f, 0f), new Vector2(-18f, 0f));
        weight.fontSize = 14f;
        weight.color = theme.textSecondary;
        weight.text = weight.text.Replace("TOTAL WEIGHT", weightLabel).Replace("FIELD MASS", weightLabel).Replace("TOTAL LOAD", weightLabel).Replace("CARRIED", weightLabel);

        Transform currency = FindRequired(footer, "Currency");
        SetRect(RequireRect(currency), new Vector2(0.5f, 0f), Vector2.one, new Vector2(1f, 0.5f),
            new Vector2(-18f, 0f), new Vector2(-18f, 0f));
        TMP_Text currencyText = currency.GetComponentInChildren<TMP_Text>(true);
        if (currencyText != null)
        {
            currencyText.fontSize = 19f;
            currencyText.color = theme.textPrimary;
        }
    }

    private static void StyleQuickBar(Transform quickBar, InventoryTheme theme, int horizontalPadding, int verticalPadding, float spacing, Color color)
    {
        ConfigureImage(RequireImage(quickBar), theme.panelSprite, color, Image.Type.Sliced, false);
        HorizontalLayoutGroup layout = RequireComponent<HorizontalLayoutGroup>(quickBar.gameObject);
        layout.enabled = true;
        layout.padding = new RectOffset(horizontalPadding, horizontalPadding, verticalPadding, verticalPadding);
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    private static void StyleActionSlot(ActionSlot slot, InventoryTheme theme, Concept concept)
    {
        RectTransform rect = RequireRect(slot.transform);
        rect.sizeDelta = concept == Concept.CleanExtraction
            ? new Vector2(66f, 64f)
            : concept == Concept.FramelessDeck ? new Vector2(74f, 70f) : new Vector2(72f, 66f);
        rect.localScale = Vector3.one;

        Image background = RequireImage(slot.transform);
        Color slotColor = concept == Concept.SideDrawer
            ? Hex("151719", 0.98f)
            : theme.surface1;
        ConfigureImage(background, concept == Concept.CleanExtraction ? theme.slotSprite : background.sprite,
            slotColor, concept == Concept.CleanExtraction ? Image.Type.Sliced : background.type, true);

        Transform border = FindRequired(slot.transform, "Border");
        ConfigureImage(RequireImage(border), concept == Concept.CleanExtraction ? theme.slotOutlineSprite : RequireImage(border).sprite,
            WithAlpha(theme.borderSubtle, 0.86f), Image.Type.Sliced, false);
        Transform rail = FindRequired(slot.transform, "SelectionRail");
        SetRect(RequireRect(rail), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 1f), new Vector2(34f, 3f));

        Transform plate = FindRequired(slot.transform, "HotkeyPlate");
        SetRect(RequireRect(plate), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(5f, -5f), new Vector2(21f, 19f));
        RequireImage(plate).color = theme.surface0;
        Transform label = FindRequired(slot.transform, "HotkeyLabel");
        SetRect(RequireRect(label), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(5f, -5f), new Vector2(21f, 19f));
        TMP_Text hotkey = RequireText(label);
        hotkey.fontSize = 14f;
        hotkey.color = theme.textSecondary;

        if (concept == Concept.CleanExtraction)
        {
            InventorySlotVisual visual = slot.GetComponent<InventorySlotVisual>();
            if (visual != null)
                SetFloat(visual, "selectedGlowStrength", 0f);
        }
    }

    private static void StyleCell(GameObject root, InventoryTheme theme, Concept concept)
    {
        CleanupConceptObjects(root.transform);
        Vector2 size = concept == Concept.GearBoard
            ? new Vector2(84f, 84f)
            : concept == Concept.SideDrawer ? new Vector2(66f, 66f)
            : concept == Concept.CleanExtraction ? new Vector2(98f, 92f) : new Vector2(92f, 80f);
        RequireRect(root.transform).sizeDelta = size;
        RequireRect(root.transform).localScale = Vector3.one;

        Image background = RequireImage(root.transform);
        ConfigureImage(background, concept == Concept.CleanExtraction ? theme.slotSprite : background.sprite,
            concept == Concept.FramelessDeck ? Hex("0C111A", 0.74f) : theme.surface1,
            concept == Concept.CleanExtraction ? Image.Type.Sliced : background.type, true);
        Transform border = FindRequired(root.transform, "Border");
        ConfigureImage(RequireImage(border), concept == Concept.CleanExtraction ? theme.slotOutlineSprite : RequireImage(border).sprite,
            concept == Concept.FramelessDeck ? WithAlpha(theme.borderSubtle, 0.46f) : WithAlpha(theme.borderSubtle, 0.74f),
            Image.Type.Sliced, false);
        Transform rail = FindRequired(root.transform, "SelectionRail");
        if (concept == Concept.CleanExtraction)
        {
            SetRect(RequireRect(rail), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 1f), Vector2.zero);
            SetFloat(RequireComponent<InventorySlotVisual>(root), "selectedGlowStrength", 0f);
        }
        else
        {
            SetRect(RequireRect(rail), new Vector2(0f, 0.18f), new Vector2(0f, 0.82f), new Vector2(0f, 0.5f),
                new Vector2(1f, 0f), new Vector2(3f, 0f));
        }

        if (concept == Concept.SideDrawer)
            AddRule(root.transform, "Concept_B_Notch", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.one, new Vector2(-5f, -5f), new Vector2(10f, 2f), WithAlpha(theme.accent, 0.44f));

        if (concept == Concept.CleanExtraction)
            BuildLockIcon(root, theme);
    }

    private static void StyleItem(GameObject root, InventoryTheme theme, Concept concept)
    {
        CleanupConceptObjects(root.transform);
        Vector2 size = concept == Concept.GearBoard
            ? new Vector2(84f, 84f)
            : concept == Concept.SideDrawer ? new Vector2(66f, 66f)
            : concept == Concept.CleanExtraction ? new Vector2(98f, 92f) : new Vector2(92f, 80f);
        RequireRect(root.transform).sizeDelta = size;

        Transform icon = FindRequired(root.transform, "Icon");
        float iconSize = concept == Concept.GearBoard
            ? 58f
            : concept == Concept.SideDrawer ? 46f : concept == Concept.CleanExtraction ? 64f : 58f;
        SetRect(RequireRect(icon), Center, Center, Center,
            concept == Concept.CleanExtraction ? new Vector2(0f, 1f) : Vector2.zero,
            new Vector2(iconSize, iconSize));
        Image iconImage = RequireImage(icon);
        iconImage.preserveAspect = true;

        Transform rarity = FindRequired(root.transform, "RarityBorder");
        if (concept == Concept.CleanExtraction)
        {
            RequireImage(rarity).enabled = false;
            AddRule(root.transform, "Concept_D_ItemStatus", new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero,
                new Vector2(11f, 8f), new Vector2(20f, 3f), WithAlpha(theme.success, 0.82f));
        }
        else if (concept == Concept.FramelessDeck)
        {
            SetRect(RequireRect(rarity), new Vector2(0.2f, 0f), new Vector2(0.8f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 1f), new Vector2(0f, 3f));
        }
        else
        {
            SetRect(RequireRect(rarity), new Vector2(1f, 0.22f), new Vector2(1f, 0.78f), new Vector2(1f, 0.5f),
                new Vector2(-1f, 0f), new Vector2(3f, 0f));
        }

        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            text.fontSize = Mathf.Min(text.fontSize, concept == Concept.SideDrawer ? 13f : concept == Concept.CleanExtraction ? 14f : 15f);
    }

    private static void BuildLockIcon(GameObject root, InventoryTheme theme)
    {
        RectTransform lockRoot = EnsureRectChild(root.transform, "LockIcon");
        SetRect(lockRoot, Center, Center, Center, Vector2.zero, new Vector2(24f, 28f));

        RectTransform shackle = EnsureRectChild(lockRoot, "Shackle");
        SetRect(shackle, Center, Center, Center, new Vector2(0f, 5f), new Vector2(15f, 15f));
        ConfigureImage(RequireImage(shackle), theme.buttonSprite, WithAlpha(theme.textDisabled, 0.82f), Image.Type.Sliced, false);

        RectTransform shackleCutout = EnsureRectChild(lockRoot, "ShackleCutout");
        SetRect(shackleCutout, Center, Center, Center, new Vector2(0f, 5f), new Vector2(8f, 10f));
        ConfigureImage(RequireImage(shackleCutout), null, theme.surfaceLocked, Image.Type.Simple, false);

        RectTransform body = EnsureRectChild(lockRoot, "Body");
        SetRect(body, Center, Center, Center, new Vector2(0f, -5f), new Vector2(18f, 14f));
        ConfigureImage(RequireImage(body), theme.buttonSprite, WithAlpha(theme.textDisabled, 0.88f), Image.Type.Sliced, false);

        lockRoot.SetAsLastSibling();
        lockRoot.gameObject.SetActive(false);

        InventorySlotVisual visual = RequireComponent<InventorySlotVisual>(root);
        SetObject(visual, "lockIconRoot", lockRoot.gameObject);
    }

    private static void SetGrid(Transform grid, Vector2 position, Vector2 size, Vector2 cell, Vector2 spacing, int columns)
    {
        SetRect(RequireRect(grid), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), position, size);
        GridLayoutGroup layout = RequireComponent<GridLayoutGroup>(grid.gameObject);
        layout.cellSize = cell;
        layout.spacing = spacing;
        layout.padding = new RectOffset();
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = columns;
        layout.startAxis = GridLayoutGroup.Axis.Horizontal;
        layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        layout.childAlignment = TextAnchor.UpperLeft;
    }

    private static void SetDetailText(Transform detail, string name, Vector2 position, Vector2 size, float fontSize, Color color)
    {
        TMP_Text text = RequireText(FindRequired(detail, name));
        text.gameObject.SetActive(true);
        SetRect(RequireRect(text.transform), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), position, size);
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.TopLeft;
    }

    private static void AddCornerMarks(Transform parent, Color color, float width, float height)
    {
        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        AddRule(parent, "Concept_A_CornerTL_H", Center, Center, Center, new Vector2(-halfW + 42f, halfH - 12f), new Vector2(64f, 2f), color);
        AddRule(parent, "Concept_A_CornerTL_V", Center, Center, Center, new Vector2(-halfW + 11f, halfH - 43f), new Vector2(2f, 64f), color);
        AddRule(parent, "Concept_A_CornerBR_H", Center, Center, Center, new Vector2(halfW - 42f, -halfH + 12f), new Vector2(64f, 2f), color);
        AddRule(parent, "Concept_A_CornerBR_V", Center, Center, Center, new Vector2(halfW - 11f, -halfH + 43f), new Vector2(2f, 64f), color);
    }

    private static void AddOutline(Transform parent, InventoryTheme theme, Vector2 inset, Color color)
    {
        AddNamedOutline(parent, "Concept_Outline", theme, inset, color);
    }

    private static void AddNamedOutline(
        Transform parent,
        string name,
        InventoryTheme theme,
        Vector2 inset,
        Color color)
    {
        RectTransform rect = EnsureRectChild(parent, name);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = -inset;
        rect.offsetMax = inset;
        ConfigureImage(RequireImage(rect), theme.panelOutlineSprite, color, Image.Type.Sliced, false);
        rect.SetAsLastSibling();
    }

    private static void AddRule(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 position,
        Vector2 size,
        Color color)
    {
        RectTransform rect = EnsureRectChild(parent, name);
        SetRect(rect, anchorMin, anchorMax, pivot, position, size);
        ConfigureImage(RequireImage(rect), null, color, Image.Type.Simple, false);
        rect.SetAsLastSibling();
    }

    private static TMP_Text AddLabel(
        Transform parent,
        string name,
        string value,
        float size,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 position,
        Vector2 rectSize,
        TextAlignmentOptions alignment,
        float spacing)
    {
        RectTransform rect = EnsureRectChild(parent, name);
        SetRect(rect, anchorMin, anchorMax, pivot, position, rectSize);
        TextMeshProUGUI text = rect.GetComponent<TextMeshProUGUI>();
        if (text == null)
            text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.characterSpacing = spacing;
        text.raycastTarget = false;
        text.enableAutoSizing = false;
        return text;
    }

    private static void CleanupConceptObjects(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child.name.StartsWith("Concept_", StringComparison.Ordinal))
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
                continue;
            }

            CleanupConceptObjects(child);
        }
    }

    private static void SetActiveDeep(Transform root, string name, bool active)
    {
        Transform found = FindDeep(root, name);
        if (found != null)
            found.gameObject.SetActive(active);
    }

    private static void NormalizeCanvasHierarchy()
    {
        EditPrefab(CanvasPath, root =>
        {
            Transform inventory = FindRequired(root.transform, "inventory");
            Transform panel = FindRequired(inventory, "Panel");
            Transform footer = FindRequired(panel, "Footer");
            Transform currency = FindRequired(panel, "Currency");
            footer.gameObject.SetActive(true);
            if (currency.parent != footer)
                currency.SetParent(footer, false);
        });
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

    private static RectTransform EnsureRectChild(Transform parent, string name)
    {
        Transform child = FindDirect(parent, name);
        if (child != null)
            return RequireRect(child);
        GameObject created = new GameObject(name, typeof(RectTransform));
        created.transform.SetParent(parent, false);
        return created.GetComponent<RectTransform>();
    }

    private static Transform FindDirect(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
            if (parent.GetChild(i).name == name)
                return parent.GetChild(i);
        return null;
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
        RectTransform rect = transform as RectTransform;
        if (rect == null)
            throw new InvalidOperationException($"'{transform.name}' has no RectTransform.");
        return rect;
    }

    private static Image RequireImage(Transform transform)
    {
        Image image = transform.GetComponent<Image>();
        if (image == null)
            image = transform.gameObject.AddComponent<Image>();
        return image;
    }

    private static TMP_Text RequireText(Transform transform)
    {
        TMP_Text text = transform.GetComponent<TMP_Text>();
        if (text == null)
            throw new InvalidOperationException($"'{transform.name}' has no TMP text component.");
        return text;
    }

    private static T RequireComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component == null)
            component = target.AddComponent<T>();
        return component;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void ConfigureImage(Image image, Sprite sprite, Color color, Image.Type type, bool raycast)
    {
        image.sprite = sprite;
        image.type = sprite == null ? Image.Type.Simple : type;
        image.color = color;
        image.raycastTarget = raycast;
    }

    private static void SetObject(UnityEngine.Object target, string name, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        if (property == null)
            throw new InvalidOperationException($"Serialized object field '{name}' was not found on {target.name}.");
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetInt(UnityEngine.Object target, string name, int value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        if (property == null)
            throw new InvalidOperationException($"Serialized int field '{name}' was not found on {target.name}.");
        property.intValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetBool(UnityEngine.Object target, string name, bool value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        if (property == null)
            throw new InvalidOperationException($"Serialized bool field '{name}' was not found on {target.name}.");
        property.boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetFloat(UnityEngine.Object target, string name, float value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        if (property == null)
            throw new InvalidOperationException($"Serialized float field '{name}' was not found on {target.name}.");
        property.floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetString(UnityEngine.Object target, string name, string value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        if (property == null)
            throw new InvalidOperationException($"Serialized string field '{name}' was not found on {target.name}.");
        property.stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetObjectArray(UnityEngine.Object target, string name, params GameObject[] values)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        if (property == null || !property.isArray)
            throw new InvalidOperationException($"Serialized object array '{name}' was not found on {target.name}.");
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetObjectReferenceArray(UnityEngine.Object target, string name, params UnityEngine.Object[] values)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        if (property == null || !property.isArray)
            throw new InvalidOperationException($"Serialized object array '{name}' was not found on {target.name}.");
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Color Hex(string hex, float alpha)
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

    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
}
