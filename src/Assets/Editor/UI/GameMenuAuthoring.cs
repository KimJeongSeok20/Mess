using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>Authors the small front end once; runtime references remain Inspector owned.</summary>
public static class GameMenuAuthoring
{
    public const string TitleScene = "Assets/SceneTemplateAssets/Scenes/MainMenu.unity";
    public const string GameScene = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
    private const string PrefabPath = "Assets/UI/Menu/GameMenu.prefab";
    private static readonly Color Ink = new(0.035f, 0.046f, 0.057f, 1f);
    private static readonly Color Paper = new(0.94f, 0.93f, 0.87f, 1f);
    private static readonly Color Amber = new(0.88f, 0.69f, 0.34f, 1f);
    private static readonly Color Muted = new(0.58f, 0.64f, 0.65f, 1f);
    private static TMP_FontAsset _font;

    public static string AddCredits()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Stopped, compiled Editor required.");
        var document = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/UI/Menu/Credits.txt");
        if (document == null || string.IsNullOrWhiteSpace(document.text))
            throw new InvalidOperationException("Credits document missing.");

        var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var controller = contents.GetComponent<GameMenuController>();
            var canvas = contents.transform.Find("MenuCanvas");
            var existing = canvas.Find("Credits");
            if (existing != null)
            {
                existing.GetComponent<UnityEngine.UI.Image>().color = Ink;
                existing.Find("Card/Scroll View/Viewport/Content/Text").GetComponent<TMP_Text>().text = document.text;
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                return "Updated the existing credits text.";
            }

            var actions = canvas.Find("Home/Actions");
            var template = actions.Find("Options").GetComponent<UnityEngine.UI.Button>();
            _font = template.GetComponentInChildren<TMP_Text>().font;
            var open = UnityEngine.Object.Instantiate(template, actions);
            open.name = "Credits";
            open.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            open.GetComponentInChildren<TMP_Text>().text = "CREDITS";
            open.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

            var panel = Panel("Credits", canvas);
            var shield = panel.gameObject.AddComponent<UnityEngine.UI.Image>();
            shield.color = Ink;
            var card = Rect("Card", panel);
            Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1520, 928));

            var header = Label("Heading", card, "CREDITS", 58, Paper);
            Place(header.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(1100, 78));
            var subtitle = Label("Subtitle", card, "MessUP  /  THE PEOPLE BEHIND THE WORLD", 24, Amber);
            Place(subtitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -80), new Vector2(1400, 38));
            var line = Rect("Divider", card);
            Place(line, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -134), new Vector2(1520, 2));
            var lineImage = line.gameObject.AddComponent<UnityEngine.UI.Image>();
            lineImage.color = Amber; lineImage.raycastTarget = false;

            var scrollRoot = Panel("Scroll View", card);
            scrollRoot.offsetMin = new Vector2(0, 100);
            scrollRoot.offsetMax = new Vector2(0, -158);
            var scroll = scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48;
            var viewport = Panel("Viewport", scrollRoot);
            viewport.offsetMax = new Vector2(-40, 0);
            var viewportImage = viewport.gameObject.AddComponent<UnityEngine.UI.Image>();
            viewportImage.color = Color.white;
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(0, 20, 0, 28);
            var fitter = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            var text = Label("Text", content, document.text, 26, Paper);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = true;
            text.lineSpacing = 4;
            scroll.viewport = viewport; scroll.content = content;

            var track = Rect("Scrollbar", scrollRoot);
            track.anchorMin = new Vector2(1, 0); track.anchorMax = Vector2.one;
            track.pivot = new Vector2(1, 0.5f); track.sizeDelta = new Vector2(16, 0);
            track.anchoredPosition = Vector2.zero;
            track.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0.13f, 0.16f, 0.18f, 1);
            var sliding = Panel("Sliding Area", track);
            var handle = Panel("Handle", sliding);
            var handleImage = handle.gameObject.AddComponent<UnityEngine.UI.Image>();
            handleImage.color = Amber;
            var scrollbar = track.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            scrollbar.value = 1;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.Permanent;

            var backTemplate = canvas.Find("Options/Settings/Back").GetComponent<UnityEngine.UI.Button>();
            var back = UnityEngine.Object.Instantiate(backTemplate, card);
            back.name = "Back";
            back.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            Place((RectTransform)back.transform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(280, 62));
            var hint = Label("Scroll Hint", card, "SCROLL TO VIEW ALL CREDITS  /  ESC TO RETURN", 22, Muted);
            Place(hint.rectTransform, new Vector2(1, 0), new Vector2(1, 0), Vector2.zero, new Vector2(1100, 62));
            hint.alignment = TextAlignmentOptions.MidlineRight;

            Set(controller, "creditsPanel", panel.gameObject);
            Set(controller, "creditsButton", open);
            Set(controller, "creditsBackButton", back);
            Set(controller, "creditsScroll", scroll);
            panel.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            return "Added title credits button, scrollable credits panel and Inspector references.";
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
    }

    public static string FinishMessUp()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || SceneManager.GetActiveScene().path != TitleScene)
            throw new InvalidOperationException("Saved MainMenu, stopped and compiled, required.");
        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/UI/Fonts/Vitals/BarlowCondensed-SemiBold SDF.asset");
        var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var panel = contents.transform.Find("MenuCanvas");
            foreach (string name in new[] { "Header", "Tagline", "Footer", "Accent" })
            {
                var unwanted = panel.Find(name);
                if (unwanted != null) UnityEngine.Object.DestroyImmediate(unwanted.gameObject);
            }
            var title = panel.Find("Title").GetComponent<TMP_Text>(); title.text = "MessUP"; title.fontSize = 112;
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(108, -96), new Vector2(600, 160));
            var actions = (RectTransform)panel.Find("Home/Actions");
            Place(actions, new Vector2(0, 1), new Vector2(0, 1), new Vector2(112, -294), new Vector2(440, 670));
            actions.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().spacing = 10;
            foreach (Transform action in actions)
            {
                var size = action.GetComponent<UnityEngine.UI.LayoutElement>(); size.minHeight = size.preferredHeight = 56;
            }
            var status = panel.Find("Status").GetComponent<TMP_Text>(); status.text = ""; status.fontSize = 22;
            Place(status.rectTransform, Vector2.zero, Vector2.zero, new Vector2(112, 42), new Vector2(700, 62));
            var optionsCard = panel.Find("Options/Settings");
            var cardImage = optionsCard.GetComponent<UnityEngine.UI.Image>() ?? optionsCard.gameObject.AddComponent<UnityEngine.UI.Image>();
            cardImage.color = new Color(Ink.r, Ink.g, Ink.b, 0.96f);
            var confirmTitle = panel.Find("Confirmation/Dialog/BEFORE YOU LEAVE");
            if (confirmTitle != null) UnityEngine.Object.DestroyImmediate(confirmTitle.gameObject);
            panel.GetComponent<UnityEngine.UI.CanvasScaler>().screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }

        var menu = Find<GameMenuController>();
        if (menu.GetComponent<SteamRoomPanel>() != null || GameObject.Find("Menu Cinematic") != null)
            throw new InvalidOperationException("MessUP finish already authored; inspect before repeating.");
        var canvasRoot = menu.transform.Find("MenuCanvas");
        canvasRoot.GetComponent<UnityEngine.UI.Image>().color = new Color(0, 0, 0, 0);
        PrefabUtility.RecordPrefabInstancePropertyModifications(canvasRoot.GetComponent<UnityEngine.UI.Image>());
        var home = canvasRoot.Find("Home");
        var actionsRoot = home.Find("Actions");
        var steamRoot = new GameObject("Steam Session");
        var runtime = steamRoot.AddComponent<SteamManager>();
        var holder = steamRoot.AddComponent<PurrLobby.LobbyDataHolder>();
        var service = steamRoot.AddComponent<SteamRoomService>(); service.Configure(runtime, holder);
        var roomUi = menu.gameObject.AddComponent<SteamRoomPanel>();
        Set(menu, "steamRoomPanel", roomUi); Set(roomUi, "service", service); Set(roomUi, "homePanel", home.gameObject);
        var create = Button("Create Steam Room", actionsRoot, "CREATE ROOM");
        var code = CreateCodeInput(actionsRoot);
        var join = Button("Join Steam Room", actionsRoot, "JOIN ROOM");
        Set(roomUi, "createRoomButton", create); Set(roomUi, "roomCodeInput", code); Set(roomUi, "joinRoomButton", join);
        create.transform.SetSiblingIndex(2); code.transform.SetSiblingIndex(3); join.transform.SetSiblingIndex(4);
        foreach (Transform action in actionsRoot)
        {
            var element = action.GetComponent<UnityEngine.UI.LayoutElement>(); element.minHeight = element.preferredHeight = 56;
        }
        var room = Panel("Steam Room", canvasRoot);
        var roomColumn = Column("Room Actions", room, new Vector2(112, -300), new Vector2(560, 500), 18);
        Set(roomUi, "roomPanel", room.gameObject);
        Set(roomUi, "roomCodeText", LabelInColumn(roomColumn, "", 54, Paper, 90));
        Set(roomUi, "membersText", LabelInColumn(roomColumn, "", 28, Muted, 60));
        Set(roomUi, "startRoomButton", Button("Start Room", roomColumn, "START GAME", true));
        Set(roomUi, "leaveRoomButton", Button("Leave Room", roomColumn, "LEAVE ROOM"));
        room.gameObject.SetActive(false);
        var roomStatus = Label("Room Status", canvasRoot, "", 23, Amber);
        Place(roomStatus.rectTransform, Vector2.zero, Vector2.zero, new Vector2(112, 108), new Vector2(780, 75));
        Set(roomUi, "roomStatusText", roomStatus);

        var cinematic = new GameObject("Menu Cinematic", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        var canvas = cinematic.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 29999;
        var scaler = cinematic.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
        var stage = Rect("Stage", cinematic.transform);
        Place(stage, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080));
        var background = Panel("Prison", stage);
        var backgroundImage = background.gameObject.AddComponent<UnityEngine.UI.RawImage>(); backgroundImage.raycastTarget = false;
        backgroundImage.texture = ImportArt("Assets/UI/Menu/Art/MessUP_PrisonMonsters_Background.png");
        var player = Rect("Shooter", stage);
        Place(player, Vector2.zero, Vector2.zero, new Vector2(330, -120), new Vector2(1440, 810));
        var playerImage = player.gameObject.AddComponent<UnityEngine.UI.RawImage>(); playerImage.raycastTarget = false;
        playerImage.texture = ImportArt("Assets/UI/Menu/Art/MessUP_ShotgunPlayer_ChromaMagenta.png");
        var material = new Material(Shader.Find("MessUP/UI/ChromaCutout"));
        AssetDatabase.CreateAsset(material, "Assets/UI/Menu/MessUP_Shooter.mat"); playerImage.material = material;
        var effects = Panel("Shotgun Discharge", stage).gameObject.AddComponent<ShotgunPelletGraphic>(); effects.raycastTarget = false;
        var fxData = new SerializedObject(effects);
        fxData.FindProperty("muzzle").vector2Value = new Vector2((330 + 1440 * 0.691f) / 1920f, (-120 + 810 * 0.644f) / 1080f);
        fxData.FindProperty("target").vector2Value = new Vector2(0.80f, 0.60f); fxData.ApplyModifiedPropertiesWithoutUndo();
        var animation = cinematic.AddComponent<MenuShotgunCinematic>();
        Set(animation, "backdrop", background); Set(animation, "shooter", player); Set(animation, "discharge", effects);
        PlayerSettings.productName = "MessUP";
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
        return "MessUP cinematic and six-digit Steam room UI authored.";
    }

    private static Texture2D ImportArt(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default; importer.mipmapEnabled = false;
        importer.maxTextureSize = 2048; importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.wrapMode = TextureWrapMode.Clamp; importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static TMP_InputField CreateCodeInput(Transform parent)
    {
        var rect = Rect("Room Number", parent);
        rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 56;
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(0.08f, 0.10f, 0.12f, 0.98f);
        var input = rect.gameObject.AddComponent<TMP_InputField>(); input.targetGraphic = image;
        var viewport = Panel("Text Area", rect); viewport.offsetMin = new Vector2(20, 4); viewport.offsetMax = new Vector2(-20, -4);
        var text = Label("Text", viewport, "", 28, Paper);
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        var hint = Label("Placeholder", viewport, "ROOM NUMBER", 25, Muted);
        hint.rectTransform.anchorMin = Vector2.zero; hint.rectTransform.anchorMax = Vector2.one; hint.rectTransform.offsetMin = hint.rectTransform.offsetMax = Vector2.zero;
        input.textViewport = viewport; input.textComponent = text; input.placeholder = hint;
        input.contentType = TMP_InputField.ContentType.IntegerNumber; input.characterLimit = 6;
        return input;
    }

    public static string Create()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Stopped, compiled Editor required.");
        var game = SceneManager.GetActiveScene();
        if (game.path != GameScene || game.isDirty)
            throw new InvalidOperationException("Open the saved StartMap before authoring.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null ||
            AssetDatabase.LoadAssetAtPath<SceneAsset>(TitleScene) != null ||
            UnityEngine.Object.FindFirstObjectByType<GameMenuController>() != null)
            throw new InvalidOperationException("Menu already exists; inspect it before changing it.");

        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/Resources/UI/Fonts/Vitals/BarlowCondensed-SemiBold SDF.asset");
        if (_font == null) throw new InvalidOperationException("Existing UI font missing.");
        if (!AssetDatabase.IsValidFolder("Assets/UI/Menu")) AssetDatabase.CreateFolder("Assets/UI", "Menu");
        var authored = BuildMenu();
        var prefab = PrefabUtility.SaveAsPrefabAsset(authored, PrefabPath);
        UnityEngine.Object.DestroyImmediate(authored);
        AddCredits();
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var inGame = (GameObject)PrefabUtility.InstantiatePrefab(prefab, game);
        SetBool(inGame.GetComponent<GameMenuController>(), "titleScreen", false);
        inGame.transform.Find("MenuCanvas").gameObject.SetActive(false);
        PrefabUtility.RecordPrefabInstancePropertyModifications(inGame.transform.Find("MenuCanvas").gameObject);

        var save = new GameObject("Run Save").AddComponent<RunSaveService>();
        Set(save, "timeManager", Find<TimeManager>());
        Set(save, "currencyManager", Find<CurrencyManager>());
        Set(save, "taxMachine", Find<TaxCollectionMachine>());
        Set(save, "contractBoard", Find<DailyContractBoard>());
        Set(save, "inventoryManager", Find<InventoryManager>());
        Set(save, "inventoryExpansion", Find<InventoryManagerExtensions>());
        var terminal = Find<SkillWebTerminalInteraction>();
        Set(save, "skillTerminal", terminal);
        SetBool(terminal, "ensureMinimumRuntimeProgression", false);
        EditorSceneManager.MarkSceneDirty(game);
        EditorSceneManager.SaveScene(game);

        var title = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(title);
        var camera = new GameObject("Menu Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Ink;
        camera.cullingMask = 0;
        var menu = (GameObject)PrefabUtility.InstantiatePrefab(prefab, title);
        SetBool(menu.GetComponent<GameMenuController>(), "titleScreen", true);
        var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        EditorSceneManager.SaveScene(title, TitleScene);

        var retained = EditorBuildSettings.scenes.Where(s => s.path != TitleScene && s.path != GameScene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(TitleScene, true),
            new EditorBuildSettingsScene(GameScene, true) }.Concat(retained).ToArray();
        AssetDatabase.SaveAssets();
        EditorSceneManager.CloseScene(game, true);
        Selection.activeGameObject = menu;
        return "Created MainMenu, authored gameplay menu and save references, MainMenu is build scene 0.";
    }

    private static T Find<T>() where T : UnityEngine.Object
    {
        var result = UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        if (result == null) throw new InvalidOperationException("Missing StartMap reference: " + typeof(T).Name);
        return result;
    }

    private static GameObject BuildMenu()
    {
        var root = new GameObject("Game Menu");
        var controller = root.AddComponent<GameMenuController>();
        var canvasObject = Rect("MenuCanvas", root.transform);
        var canvas = canvasObject.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        var scaler = canvasObject.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        var background = canvasObject.gameObject.AddComponent<UnityEngine.UI.Image>();
        background.color = new Color(Ink.r, Ink.g, Ink.b, 0.98f);
        Set(controller, "menuRoot", canvasObject.gameObject);

        // Restrained field-report styling follows the existing company HUD palette.
        var accent = Rect("Accent", canvasObject);
        Place(accent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(76, -72), new Vector2(8, 936));
        accent.gameObject.AddComponent<UnityEngine.UI.Image>().color = Amber;
        var header = Label("Header", canvasObject, "FIELD OPERATIONS  /  PERSONNEL TERMINAL", 22, Amber);
        Place(header.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(112, -72), new Vector2(1150, 40));
        var title = Label("Title", canvasObject, "START ON", 126, Paper);
        Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(108, -132), new Vector2(850, 180));
        var tagline = Label("Tagline", canvasObject, "REPORT IN.  MAKE IT BACK.  PAY YOUR DUES.", 26, Muted);
        Place(tagline.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(116, -322), new Vector2(820, 50));
        var status = Label("Status", canvasObject, "Continue from a saved camp morning.", 24, Muted);
        Place(status.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(116, 90), new Vector2(770, 150));
        status.alignment = TextAlignmentOptions.BottomLeft;
        Set(controller, "statusText", status);
        var footer = Label("Footer", canvasObject, "LOCAL CHECKPOINT  /  CAMP, 09:00", 20, Muted);
        Place(footer.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(116, 44), new Vector2(1000, 32));

        var home = Panel("Home", canvasObject);
        var buttons = Column("Actions", home, new Vector2(112, -406), new Vector2(630, 472), 12);
        Set(controller, "homePanel", home.gameObject);
        Set(controller, "newGameButton", Button("New Game", buttons, "LOCAL PLAY", true));
        Set(controller, "continueButton", Button("Continue", buttons, "CONTINUE LOCAL"));
        Set(controller, "resumeButton", Button("Resume", buttons, "RESUME"));
        Set(controller, "optionsButton", Button("Options", buttons, "OPTIONS"));
        Set(controller, "returnButton", Button("Return", buttons, "MAIN MENU"));
        Set(controller, "quitButton", Button("Quit", buttons, "QUIT GAME"));

        var options = Panel("Options", canvasObject);
        var optionsCard = Column("Settings", options, new Vector2(976, -168), new Vector2(790, 824), 16);
        LabelInColumn(optionsCard, "OPTIONS", 48, Paper, 72);
        AddSlider(controller, optionsCard, "MASTER VOLUME", "volumeSlider", "volumeValue", 0f, 1f);
        AddSlider(controller, optionsCard, "LOOK SENSITIVITY", "sensitivitySlider", "sensitivityValue", 0.1f, 3f);
        Set(controller, "fullscreenToggle", Toggle(optionsCard, "FULLSCREEN"));
        Set(controller, "vSyncToggle", Toggle(optionsCard, "VERTICAL SYNC"));
        var quality = Button("Quality", optionsCard, "QUALITY");
        Set(controller, "qualityButton", quality);
        Set(controller, "qualityValue", quality.GetComponentInChildren<TMP_Text>());
        var frameRate = Button("Frame Rate", optionsCard, "FRAME LIMIT");
        Set(controller, "frameRateButton", frameRate);
        Set(controller, "frameRateValue", frameRate.GetComponentInChildren<TMP_Text>());
        Set(controller, "defaultsButton", Button("Defaults", optionsCard, "RESTORE DEFAULTS"));
        Set(controller, "optionsBackButton", Button("Back", optionsCard, "BACK", true));
        Set(controller, "optionsPanel", options.gameObject);
        options.gameObject.SetActive(false);

        var confirmation = Panel("Confirmation", canvasObject);
        var shield = confirmation.gameObject.AddComponent<UnityEngine.UI.Image>();
        shield.color = new Color(Ink.r, Ink.g, Ink.b, 0.96f);
        var confirmColumn = Column("Dialog", confirmation, new Vector2(542, -330), new Vector2(836, 430), 20);
        LabelInColumn(confirmColumn, "BEFORE YOU LEAVE", 46, Amber, 74);
        var text = LabelInColumn(confirmColumn, "", 30, Paper, 160);
        Set(controller, "confirmationText", text);
        Set(controller, "confirmButton", Button("Confirm", confirmColumn, "CONFIRM", true));
        Set(controller, "cancelButton", Button("Cancel", confirmColumn, "CANCEL"));
        Set(controller, "confirmationPanel", confirmation.gameObject);
        confirmation.gameObject.SetActive(false);
        return root;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static RectTransform Panel(string name, Transform parent)
    {
        var rect = Rect(name, parent);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot;
        rect.anchoredPosition = position; rect.sizeDelta = size;
    }

    private static RectTransform Column(string name, Transform parent, Vector2 position, Vector2 size, float spacing)
    {
        var rect = Rect(name, parent);
        Place(rect, new Vector2(0, 1), new Vector2(0, 1), position, size);
        var layout = rect.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        return rect;
    }

    private static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color)
    {
        var rect = Rect(name, parent);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = _font; label.text = text; label.fontSize = size; label.color = color;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }

    private static TMP_Text LabelInColumn(Transform parent, string text, float size, Color color, float height)
    {
        var label = Label(text.Length > 0 ? text : "Message", parent, text, size, color);
        label.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = height;
        return label;
    }

    private static UnityEngine.UI.Button Button(string name, Transform parent, string text, bool primary = false)
    {
        var rect = Rect(name, parent);
        var element = rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        element.preferredHeight = element.minHeight = 66;
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = primary ? Amber : new Color(0.11f, 0.14f, 0.16f, 1);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1);
        colors.disabledColor = new Color(0.38f, 0.38f, 0.38f, 0.65f);
        button.colors = colors;
        var label = Label("Label", rect, text, 30, primary ? Ink : Paper);
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(24, 0); label.rectTransform.offsetMax = new Vector2(-24, 0);
        return button;
    }

    private static void AddSlider(GameMenuController controller, Transform parent, string title,
        string sliderField, string valueField, float min, float max)
    {
        var row = Rect(title, parent);
        row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 95;
        var label = Label("Caption", row, title, 24, Muted);
        Place(label.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(550, 34));
        var value = Label("Value", row, "", 24, Paper);
        Place(value.rectTransform, new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(170, 34));
        value.alignment = TextAlignmentOptions.MidlineRight;
        var sliderRect = Rect("Slider", row);
        sliderRect.anchorMin = new Vector2(0, 0); sliderRect.anchorMax = new Vector2(1, 0);
        sliderRect.pivot = new Vector2(0.5f, 0); sliderRect.sizeDelta = new Vector2(-20, 44);
        sliderRect.anchoredPosition = new Vector2(0, 3);
        var slider = sliderRect.gameObject.AddComponent<UnityEngine.UI.Slider>();
        var background = Panel("Track", sliderRect);
        background.offsetMin = new Vector2(0, 18); background.offsetMax = new Vector2(0, -18);
        background.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0.17f, 0.21f, 0.23f, 1);
        var fill = Panel("Fill", background);
        fill.gameObject.AddComponent<UnityEngine.UI.Image>().color = Amber;
        var handle = Rect("Handle", sliderRect);
        handle.sizeDelta = new Vector2(18, 36);
        var handleImage = handle.gameObject.AddComponent<UnityEngine.UI.Image>(); handleImage.color = Paper;
        slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = handleImage;
        slider.minValue = min; slider.maxValue = max; slider.value = 1;
        Set(controller, sliderField, slider); Set(controller, valueField, value);
    }

    private static UnityEngine.UI.Toggle Toggle(Transform parent, string title)
    {
        var row = Rect(title, parent);
        row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 54;
        var toggle = row.gameObject.AddComponent<UnityEngine.UI.Toggle>();
        var label = Label("Caption", row, title, 26, Paper);
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero; label.rectTransform.offsetMax = new Vector2(-74, 0);
        var box = Rect("Box", row);
        Place(box, new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(46, 38));
        var image = box.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(0.17f, 0.21f, 0.23f, 1);
        var check = Panel("Check", box);
        check.offsetMin = new Vector2(8, 8); check.offsetMax = new Vector2(-8, -8);
        var checkedImage = check.gameObject.AddComponent<UnityEngine.UI.Image>(); checkedImage.color = Amber;
        toggle.targetGraphic = image; toggle.graphic = checkedImage; toggle.isOn = true;
        return toggle;
    }

    private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        if (value == null) throw new InvalidOperationException("Missing reference: " + field);
        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty(field) ?? throw new MissingFieldException(target.GetType().Name, field);
        property.objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetBool(UnityEngine.Object target, string field, bool value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).boolValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
