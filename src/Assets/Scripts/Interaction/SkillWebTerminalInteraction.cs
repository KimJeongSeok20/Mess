using System.Reflection;
using System.Collections.Generic;
using Demo.Scripts.Runtime.Character;
using UnityEngine;
using UnityEngine.InputSystem;
using SkillWebRuntime = Esper.SkillWeb.SkillWeb;
using SkillWebSettingsAsset = Esper.SkillWeb.Settings.SkillWebSettings;
using WebGraphAsset = Esper.SkillWeb.Graph.WebGraph;
using HovercardUGUIComponent = Esper.SkillWeb.UI.UGUI.HovercardUGUI;
using SkillNodeUGUIComponent = Esper.SkillWeb.UI.UGUI.SkillNodeUGUI;
using WebViewUGUIComponent = Esper.SkillWeb.UI.UGUI.WebViewUGUI;

[DisallowMultipleComponent]
public class SkillWebTerminalInteraction : AInteractable
{
    [Header("Skill Web Setup")]
    [SerializeField] private string webName = "Survival Skills";
    [SerializeField] private bool autoInstantiateUiPrefabs = true;
    [SerializeField] private GameObject webViewPrefab;
    [SerializeField] private GameObject hovercardPrefab;

    [Header("Prompt Text")]
    [SerializeField] private string openPrompt = "[F] Open skill web";
    [SerializeField] private string closePrompt = "[Esc] Close skill web";
    [SerializeField] private string unavailablePrompt = "Skill web setup is missing.";

    [Header("Test Runtime Defaults")]
    [SerializeField] private bool ensureMinimumRuntimeProgression = true;
    [SerializeField, Min(0)] private int minimumSkillPoints = 1;

    [Header("UI Visibility While Open")]
    [SerializeField] private bool autoHideActionPanel = true;
    [SerializeField] private GameObject[] panelsToHideWhileOpen;

    [Header("Cursor While Open")]
    [SerializeField] private Texture2D openCursorTexture;
    [SerializeField] private Vector2 openCursorHotspot = new Vector2(8f, 8f);
    [SerializeField] private CursorMode openCursorMode = CursorMode.Auto;
    [SerializeField] private string openCursorResourcePath = string.Empty;

    private const string WebViewResourcePath = "Prefabs/uGUI/WebViewUGUI";
    private const string HovercardResourcePath = "Prefabs/uGUI/HovercardUGUI";

    private static bool _uiPrepared;
    private static bool _runtimeSettingsInjected;
    private static bool _settingsInitFailed;
    private static WebViewUGUIComponent _webView;
    private static HovercardUGUIComponent _hovercard;
    private static SkillWebTerminalInteraction _activeTerminal;
    private static int _lastClosedFrame = -1;

    public static bool IsModalOpen => _activeTerminal != null && _webView != null
        && _webView.content != null && _webView.IsOpen;
    public static bool BlocksGameplayInput => _activeTerminal != null || _lastClosedFrame == Time.frameCount;

    private global::PromptPresenter _promptPresenter;
    private SkillTerminalPanel _terminalPanel;
    private PlayerInput _activePlayerInput;
    private string _previousActionMap = string.Empty;
    private bool _inputWasActive;
    private CursorLockMode _previousCursorLockMode = CursorLockMode.Locked;
    private bool _previousCursorVisible;
    private bool _cursorOverridden;
    private Texture2D _cpuAccessibleCursorTexture;
    private readonly List<GameObject> _resolvedPanelsToHide = new List<GameObject>();
    private readonly Dictionary<GameObject, bool> _panelActiveStateBeforeOpen = new Dictionary<GameObject, bool>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetModalState()
    {
        _activeTerminal = null;
        _lastClosedFrame = -1;
        _uiPrepared = false;
        _webView = null;
        _hovercard = null;
    }

    private void Awake()
    {
        if (_promptPresenter == null)
            _promptPresenter = global::PromptPresenter.Instance ?? FindFirstObjectByType<global::PromptPresenter>();

        ResolvePanelsToHide();
    }

    private void OnDisable()
    {
        CloseWebView();
        RestoreOpenCursor();
        RestoreGameplayPanels();
    }

    private void OnDestroy()
    {
        CloseWebView();
        if (_cpuAccessibleCursorTexture != null)
        {
            Destroy(_cpuAccessibleCursorTexture);
            _cpuAccessibleCursorTexture = null;
        }
    }

    private void Update()
    {
        if (_activeTerminal != this)
            return;

        if (_webView == null || _webView.content == null || !_webView.IsOpen
            || !_webView.gameObject.activeInHierarchy
            || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            CloseWebView();
        }
    }

    public override void Interact(InteractionManager interactor)
    {
        if (BlocksGameplayInput)
            return;

        if (!EnsureUi(interactor))
        {
            ShowPrompt(unavailablePrompt);
            return;
        }

        if (_webView.IsOpen)
        {
            CloseWebView();
            return;
        }

        OpenWebView(interactor);
    }

    public override void OnHover()
    {
        base.OnHover();
        ShowPrompt(IsWebViewOpen() ? closePrompt : openPrompt);
    }

    public override void OnStopHover()
    {
        base.OnStopHover();

        if (_promptPresenter == null)
            _promptPresenter = global::PromptPresenter.Instance ?? FindFirstObjectByType<global::PromptPresenter>();

        if (_promptPresenter != null)
            _promptPresenter.Hide();
    }

    public override bool CanInteract()
    {
        return base.CanInteract() && gameObject.activeInHierarchy;
    }

    private bool EnsureUi(InteractionManager interactor)
    {
        if (!EnsureSkillWebSettings())
            return false;

        if (_webView == null)
        {
            _uiPrepared = false;
            _webView = FindFirstObjectByType<WebViewUGUIComponent>(FindObjectsInactive.Include);
        }

        if (_webView == null && autoInstantiateUiPrefabs)
            TryInstantiateWebView();

        if (_hovercard == null)
            _hovercard = FindFirstObjectByType<HovercardUGUIComponent>(FindObjectsInactive.Include);

        if (_hovercard == null && autoInstantiateUiPrefabs)
            TryInstantiateHovercard();

        if (_webView == null)
            return false;

        var camera = interactor != null ? interactor.CurrentCamera : null;
        AssignCanvasCamera(_webView, camera);
        AssignCanvasCamera(_hovercard, camera);

        if (!_uiPrepared)
        {
            TryLoadConfiguredWeb();
            _webView.Close();
            _uiPrepared = true;
        }
        else if (!_webView.IsWebSet)
        {
            TryLoadConfiguredWeb();
        }

        if (_terminalPanel == null)
            _terminalPanel = _webView.GetComponentInChildren<SkillTerminalPanel>(true);
        _terminalPanel?.Bind(this);

        return true;
    }

    /// <summary>Prepare the authored progression graph for loading a checkpoint without opening the terminal.</summary>
    public bool PrepareForCheckpoint() => EnsureUi(null) && _webView != null && _webView.web != null;

    private void OpenWebView(InteractionManager interactor)
    {
        EnsureMinimumRuntimeProgression();
        CachePlayerInput(interactor);
        _previousCursorLockMode = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
        _activeTerminal = this;

        if (_activePlayerInput != null)
        {
            _inputWasActive = _activePlayerInput.inputIsActive;
            _previousActionMap = _activePlayerInput.currentActionMap != null
                ? _activePlayerInput.currentActionMap.name
                : string.Empty;

            // The player's UI map also contains inventory/phone shortcuts. The EventSystem
            // owns separate UI actions, so only the player's actions are suspended here.
            _activePlayerInput.DeactivateInput();
            _activePlayerInput.GetComponent<FPSController>()?.ClearGameplayInputForModal();
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ApplyOpenCursor();

        HideGameplayPanels();
        _webView.Open();

        if (_promptPresenter != null)
            _promptPresenter.Hide();
    }

    public static void CloseForPlayerStateChange()
    {
        if (_activeTerminal != null)
            _activeTerminal.CloseWebView();
    }

    public void CloseWebView()
    {
        if (_activeTerminal != this)
            return;

        _activeTerminal = null;
        _lastClosedFrame = Time.frameCount;

        try
        {
            // The hovercard lives on its own canvas, so both UI surfaces must close.
            CloseSkillHovercard();
            if (_webView != null && _webView.content != null)
                _webView.Close();
        }
        finally
        {
            if (_activePlayerInput != null && _activePlayerInput.enabled && _inputWasActive)
            {
                _activePlayerInput.ActivateInput();
                if (!string.IsNullOrWhiteSpace(_previousActionMap))
                    TrySwitchActionMap(_previousActionMap);
            }

            _activePlayerInput = null;
            _previousActionMap = string.Empty;
            _inputWasActive = false;

            Cursor.lockState = _previousCursorLockMode;
            Cursor.visible = _previousCursorVisible;
            RestoreOpenCursor();
            RestoreGameplayPanels();
        }
    }

    private void CloseSkillHovercard()
    {
        var hovercard = _hovercard != null ? _hovercard : HovercardUGUIComponent.Instance;

        // Disabling the web view never sends OnPointerExit to the node under the cursor, so its
        // hover flag stays set and the hovercard reopens itself right after closing.
        if (_webView != null)
        {
            var nodes = _webView.GetComponentsInChildren<SkillNodeUGUIComponent>(true);
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] != null)
                    nodes[i].hasPointerHover = false;
            }
        }

        if (hovercard == null)
            return;

        if (hovercard.Target != null)
            hovercard.Target.hasPointerHover = false;

        // Close() also stops the pending appearance-delay coroutine when nothing is shown yet.
        hovercard.Close();
    }

    private void ApplyOpenCursor()
    {
        var cursorTexture = ResolveOpenCursorTexture();
        if (cursorTexture == null)
            return;

        if (!cursorTexture.isReadable)
            cursorTexture = GetCpuAccessibleCursorTexture(cursorTexture);

        if (cursorTexture == null)
            return;

        Cursor.SetCursor(cursorTexture, openCursorHotspot, openCursorMode);
        _cursorOverridden = true;
    }

    private void RestoreOpenCursor()
    {
        if (!_cursorOverridden)
            return;

        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        _cursorOverridden = false;
    }

    private Texture2D ResolveOpenCursorTexture()
    {
        if (openCursorTexture != null)
            return openCursorTexture;

        if (!string.IsNullOrWhiteSpace(openCursorResourcePath))
        {
            var resourceTexture = Resources.Load<Texture2D>(openCursorResourcePath);
            if (resourceTexture != null)
                return resourceTexture;
        }

#if UNITY_EDITOR
        const string preferredPath = "Assets/MyAsset/Adobe Express - file (3).png";
        var preferred = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(preferredPath);
        if (preferred != null)
            return preferred;

        const string legacyPath = "Assets/MyAsset/Gemini_Generated_Image_9hkgd29hkgd29hkg.png";
        return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(legacyPath);
#else
        return null;
#endif
    }

    private Texture2D GetCpuAccessibleCursorTexture(Texture2D source)
    {
        if (_cpuAccessibleCursorTexture != null)
            return _cpuAccessibleCursorTexture;

        var temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        var previousActive = RenderTexture.active;

        Graphics.Blit(source, temporary);
        RenderTexture.active = temporary;

        _cpuAccessibleCursorTexture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        _cpuAccessibleCursorTexture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        _cpuAccessibleCursorTexture.Apply();
        _cpuAccessibleCursorTexture.name = source.name + "_CpuCopy";

        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(temporary);

        return _cpuAccessibleCursorTexture;
    }

    private void ResolvePanelsToHide()
    {
        _resolvedPanelsToHide.Clear();

        if (panelsToHideWhileOpen != null)
        {
            for (int i = 0; i < panelsToHideWhileOpen.Length; i++)
            {
                var panel = panelsToHideWhileOpen[i];
                if (panel != null && !_resolvedPanelsToHide.Contains(panel))
                    _resolvedPanelsToHide.Add(panel);
            }
        }

        if (!autoHideActionPanel)
            return;

        GameObject actionPanel = null;
        var rootCanvas = GameObject.Find("Canvas");
        if (rootCanvas != null)
        {
            var child = FindQuickSlotPanel(rootCanvas.transform);
            if (child != null)
                actionPanel = child.gameObject;
        }

        if (actionPanel == null)
        {
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                var candidate = canvases[i] != null ? FindQuickSlotPanel(canvases[i].transform) : null;
                if (candidate != null)
                {
                    actionPanel = candidate.gameObject;
                    break;
                }
            }
        }

        if (actionPanel != null && !_resolvedPanelsToHide.Contains(actionPanel))
            _resolvedPanelsToHide.Add(actionPanel);
    }

    private static Transform FindQuickSlotPanel(Transform canvasRoot)
    {
        if (canvasRoot == null)
            return null;

        return canvasRoot.Find("inventory/QuickSlotBar")
            ?? canvasRoot.Find("QuickSlotBar")
            ?? canvasRoot.Find("inventory/ActionPanel")
            ?? canvasRoot.Find("ActionPanel");
    }

    private void HideGameplayPanels()
    {
        _panelActiveStateBeforeOpen.Clear();

        for (int i = 0; i < _resolvedPanelsToHide.Count; i++)
        {
            var panel = _resolvedPanelsToHide[i];
            if (panel == null)
                continue;

            bool wasActive = panel.activeSelf;
            _panelActiveStateBeforeOpen[panel] = wasActive;

            if (wasActive)
                panel.SetActive(false);
        }
    }

    private void RestoreGameplayPanels()
    {
        if (_panelActiveStateBeforeOpen.Count == 0)
            return;

        foreach (var item in _panelActiveStateBeforeOpen)
        {
            if (item.Key != null)
                item.Key.SetActive(item.Value);
        }

        _panelActiveStateBeforeOpen.Clear();
    }

    private void CachePlayerInput(InteractionManager interactor)
    {
        _activePlayerInput = null;

        if (interactor != null && interactor.CurrentCamera != null)
        {
            var camera = interactor.CurrentCamera;
            _activePlayerInput = camera.GetComponentInParent<PlayerInput>();

            if (_activePlayerInput == null)
                _activePlayerInput = camera.transform.root.GetComponentInChildren<PlayerInput>(true);
        }

        if (_activePlayerInput == null)
            _activePlayerInput = FindFirstObjectByType<PlayerInput>();
    }

    private void TrySwitchActionMap(string actionMapName)
    {
        if (_activePlayerInput == null || _activePlayerInput.actions == null)
            return;

        if (string.IsNullOrWhiteSpace(actionMapName))
            return;

        var targetMap = _activePlayerInput.actions.FindActionMap(actionMapName, false);
        if (targetMap == null)
            return;

        var currentMap = _activePlayerInput.currentActionMap;
        if (currentMap != null && currentMap.name == targetMap.name)
            return;

        _activePlayerInput.SwitchCurrentActionMap(targetMap.name);
    }

    private bool EnsureSkillWebSettings()
    {
        if (SkillWebRuntime.Settings != null)
        {
            EnsureUsableSkillWebSettings(SkillWebRuntime.Settings, runtimeInjected: false);
            return true;
        }

        if (_settingsInitFailed)
            return false;

        var settingsField = typeof(SkillWebRuntime).GetField("settings", BindingFlags.Static | BindingFlags.NonPublic);
        if (settingsField == null)
        {
            _settingsInitFailed = true;
            Debug.LogError("[SkillWebTerminalInteraction] SkillWeb settings field was not found.");
            return false;
        }

        var runtimeSettings = ScriptableObject.CreateInstance<SkillWebSettingsAsset>();
        runtimeSettings.hideFlags = HideFlags.HideAndDontSave;
        runtimeSettings.name = "RuntimeSkillWebSettings";
        EnsureUsableSkillWebSettings(runtimeSettings, runtimeInjected: true);
        settingsField.SetValue(null, runtimeSettings);

        if (SkillWebRuntime.Settings == null)
        {
            _settingsInitFailed = true;
            Debug.LogError("[SkillWebTerminalInteraction] SkillWeb settings could not be initialized.");
            return false;
        }

        _runtimeSettingsInjected = true;
        return true;
    }

    private static void EnsureUsableSkillWebSettings(SkillWebSettingsAsset settings, bool runtimeInjected)
    {
        if (settings == null)
            return;

        if (string.IsNullOrWhiteSpace(settings.databaseName))
            settings.databaseName = "SkillWeb";

        if (settings.zoomStrength <= 0f)
            settings.zoomStrength = 0.2f;

        if (settings.zoomSmoothing <= 0f)
            settings.zoomSmoothing = 8f;

        if (settings.minScale <= 0f)
            settings.minScale = 0.25f;

        if (settings.maxScale <= settings.minScale)
            settings.maxScale = settings.minScale + 1.25f;

        if (settings.panSmoothing <= 0f)
            settings.panSmoothing = 8f;

        if (settings.resetFocusSpeed <= 0f)
            settings.resetFocusSpeed = 5f;

        if (settings.afterSnapDelay < 0f)
            settings.afterSnapDelay = 0.25f;

        var sizes = settings.skillNodeSizes;
        if (sizes.tiny <= 0f || sizes.small <= 0f || sizes.medium <= 0f || sizes.large <= 0f || sizes.giant <= 0f)
        {
            settings.skillNodeSizes = new SkillWebSettingsAsset.SkillNodeSizes
            {
                tiny = 56f,
                small = 72f,
                medium = 96f,
                large = 120f,
                giant = 148f
            };
        }

        if (runtimeInjected)
        {
            settings.startingScale = SkillWebSettingsAsset.StartingScale.Normal;
            settings.enablePlayerLevelRequirement = true; // civic rank (tax payments) gates the special nodes
            settings.enableDowngrading = true;
        }
    }

    private void TryInstantiateWebView()
    {
        var prefab = webViewPrefab != null ? webViewPrefab : Resources.Load<GameObject>(WebViewResourcePath);
        if (prefab == null)
            return;

        var instance = Instantiate(prefab);
        _webView = instance.GetComponent<WebViewUGUIComponent>();

        if (_webView != null)
            _webView.SetActive();
    }

    private void TryInstantiateHovercard()
    {
        var prefab = hovercardPrefab != null ? hovercardPrefab : Resources.Load<GameObject>(HovercardResourcePath);
        if (prefab == null)
            return;

        var instance = Instantiate(prefab);
        _hovercard = instance.GetComponent<HovercardUGUIComponent>();
    }

    private void TryLoadConfiguredWeb()
    {
        if (_webView == null)
            return;

        var webGraphs = SkillWebRuntime.GetAllWebGraphs();
        if (webGraphs == null || webGraphs.Length == 0)
            return;

        string targetWebName = string.IsNullOrWhiteSpace(webName) ? "Survivability Test" : webName;

        if (!string.IsNullOrWhiteSpace(targetWebName))
        {
            WebGraphAsset configuredWeb = null;

            for (int i = 0; i < webGraphs.Length; i++)
            {
                var graph = webGraphs[i];
                if (graph == null)
                    continue;

                if (string.Equals(graph.webName, targetWebName, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(graph.name, targetWebName, System.StringComparison.OrdinalIgnoreCase))
                {
                    configuredWeb = graph;
                    break;
                }
            }

            if (configuredWeb != null)
            {
                _webView.Load(configuredWeb);
                return;
            }
        }

        if (_webView.IsWebSet)
            return;

        _webView.Load(webGraphs[0]);
    }

    private static void AssignCanvasCamera(Component target, Camera camera)
    {
        if (target == null || camera == null)
            return;

        var canvases = target.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            var canvas = canvases[i];
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
                canvas.worldCamera = camera;
        }
    }

    private bool IsWebViewOpen()
    {
        return _webView != null && _webView.IsOpen;
    }

    private void ShowPrompt(string text)
    {
        if (_promptPresenter == null)
            _promptPresenter = global::PromptPresenter.Instance ?? FindFirstObjectByType<global::PromptPresenter>();

        if (_promptPresenter != null && !string.IsNullOrWhiteSpace(text))
            _promptPresenter.Show(text);
    }

    private void EnsureMinimumRuntimeProgression()
    {
        if (!ensureMinimumRuntimeProgression)
            return;

        TeamProgress.EnsureInitialSkillPoints(minimumSkillPoints);
    }
}
