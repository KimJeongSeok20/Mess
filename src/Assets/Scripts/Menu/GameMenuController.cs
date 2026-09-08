using System;
using System.Collections;
using Demo.Scripts.Runtime.Character;
using Esper.SkillWeb.UI.UGUI;
using PurrNet;
using PurrNet.Transports;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>Authored title and in-game menus. Opening this menu does not pause the network world.</summary>
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class GameMenuController : MonoBehaviour
{
    private const string GameScene = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
    private const string TitleScene = "Assets/SceneTemplateAssets/Scenes/MainMenu.unity";
    private static readonly int[] FrameRates = { 0, 30, 60, 90, 120, 144, 165, 180, 240 };

    [SerializeField] private bool titleScreen;
    [SerializeField] private GameObject menuRoot;
    [SerializeField] private GameObject homePanel;
    [SerializeField] private GameObject optionsPanel;
    [SerializeField] private GameObject confirmationPanel;
    [SerializeField] private GameObject creditsPanel;
    [SerializeField] private UnityEngine.UI.Button newGameButton;
    [SerializeField] private UnityEngine.UI.Button continueButton;
    [SerializeField] private UnityEngine.UI.Button optionsButton;
    [SerializeField] private UnityEngine.UI.Button creditsButton;
    [SerializeField] private UnityEngine.UI.Button quitButton;
    [SerializeField] private UnityEngine.UI.Button resumeButton;
    [SerializeField] private UnityEngine.UI.Button returnButton;
    [SerializeField] private UnityEngine.UI.Button confirmButton;
    [SerializeField] private UnityEngine.UI.Button cancelButton;
    [SerializeField] private UnityEngine.UI.Button optionsBackButton;
    [SerializeField] private UnityEngine.UI.Button creditsBackButton;
    [SerializeField] private UnityEngine.UI.Button defaultsButton;
    [SerializeField] private UnityEngine.UI.Button qualityButton;
    [SerializeField] private UnityEngine.UI.Button frameRateButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text confirmationText;
    [SerializeField] private TMP_Text volumeValue;
    [SerializeField] private TMP_Text sensitivityValue;
    [SerializeField] private TMP_Text qualityValue;
    [SerializeField] private TMP_Text frameRateValue;
    [SerializeField] private UnityEngine.UI.Slider volumeSlider;
    [SerializeField] private UnityEngine.UI.Slider sensitivitySlider;
    [SerializeField] private UnityEngine.UI.Toggle fullscreenToggle;
    [SerializeField] private UnityEngine.UI.Toggle vSyncToggle;
    [SerializeField] private UnityEngine.UI.ScrollRect creditsScroll;
    [SerializeField] private SteamRoomPanel steamRoomPanel;

    private enum PendingAction { None, NewGame, ReturnToTitle, Quit }
    private static GameMenuController _openMenu;
    private static bool _clientBypassUsed;
    private UnityEngine.UI.Button[] _buttons;
    private PendingAction _pendingAction;
    private NetworkPlayer _localPlayer;
    private PlayerInput _playerInput;
    private FPSInputHandler _inputHandler;
    private PlayerVitals _vitals;
    private DungeonRoomPowerPhone _phone;
    private bool _inputWasActive;
    private string _previousActionMap;
    private CursorLockMode _previousCursorLock;
    private bool _previousCursorVisible;
    private bool _capturedControls;
    private bool _capturedPlayerInput;
    private bool _waitingForCheckpoint;
    private bool _loading;
    private bool _hadConnection;
    private bool _sessionReady;
    private bool _sessionFailed;
    private string _sessionError;
    private float _startedAt;
    private float _nextPoll;

    public static bool IsOpen => _openMenu != null && _openMenu.menuRoot.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _openMenu = null;
        _clientBypassUsed = false;
    }

    private void Awake()
    {
        _buttons = new[] { newGameButton, continueButton, optionsButton, creditsButton, quitButton, resumeButton,
            returnButton, confirmButton, cancelButton, optionsBackButton, creditsBackButton, defaultsButton, qualityButton, frameRateButton };
        newGameButton.onClick.AddListener(RequestNewGame);
        continueButton.onClick.AddListener(ContinueGame);
        optionsButton.onClick.AddListener(ShowOptions);
        creditsButton.onClick.AddListener(ShowCredits);
        quitButton.onClick.AddListener(RequestQuit);
        resumeButton.onClick.AddListener(CloseGameplayMenu);
        returnButton.onClick.AddListener(RequestReturn);
        confirmButton.onClick.AddListener(ConfirmAction);
        cancelButton.onClick.AddListener(ShowHome);
        optionsBackButton.onClick.AddListener(ShowHome);
        creditsBackButton.onClick.AddListener(ShowHome);
        defaultsButton.onClick.AddListener(RestoreDefaults);
        qualityButton.onClick.AddListener(CycleQuality);
        frameRateButton.onClick.AddListener(CycleFrameRate);
        volumeSlider.onValueChanged.AddListener(ChangeVolume);
        sensitivitySlider.onValueChanged.AddListener(ChangeSensitivity);
        fullscreenToggle.onValueChanged.AddListener(ChangeFullscreen);
        vSyncToggle.onValueChanged.AddListener(ChangeVSync);

        volumeSlider.minValue = 0f;
        volumeSlider.maxValue = 1f;
        sensitivitySlider.minValue = GameOptions.MinimumSensitivity;
        sensitivitySlider.maxValue = GameOptions.MaximumSensitivity;
        newGameButton.gameObject.SetActive(titleScreen);
        continueButton.gameObject.SetActive(titleScreen);
        creditsButton.gameObject.SetActive(titleScreen);
        resumeButton.gameObject.SetActive(!titleScreen);
        returnButton.gameObject.SetActive(!titleScreen);
        menuRoot.SetActive(titleScreen);
        if (titleScreen) _openMenu = this;
    }

    private void Start()
    {
        _startedAt = Time.realtimeSinceStartup;
        ShowHome();
        RefreshOptions();
        if (!titleScreen) return;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (!_clientBypassUsed && Array.Exists(Environment.GetCommandLineArgs(),
                argument => string.Equals(argument, "-client", StringComparison.OrdinalIgnoreCase)))
        {
            _clientBypassUsed = true;
            StartCoroutine(LoadScene(GameScene));
        }
    }

    private void Update()
    {
        if (_loading) return;

        if (!titleScreen && Time.realtimeSinceStartup >= _nextPoll)
        {
            _nextPoll = Time.realtimeSinceStartup + 0.5f;
            PollSession();
        }

        if (_waitingForCheckpoint)
        {
            CacheLocalPlayer();
            CapturePlayerControls();
            return;
        }

        if (SkillWebTerminalInteraction.BlocksGameplayInput) return;
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
        if (IsOpen && _openMenu == this)
        {
            if (optionsPanel.activeSelf || confirmationPanel.activeSelf || creditsPanel.activeSelf) ShowHome();
            else if (!titleScreen && !_sessionFailed) CloseGameplayMenu();
        }
        else if (!titleScreen && CanOpenGameplayMenu())
            OpenGameplayMenu();
    }

    private bool CanOpenGameplayMenu()
    {
        if (_playerInput == null) return false;
        if (Cursor.lockState != CursorLockMode.Locked && (_vitals == null || !_vitals.IsDead)) return false;
        if (InstanceHandler.TryGetInstance(out InventoryManager inventory) && inventory.IsInventoryOpen()) return false;
        if (WebViewUGUI.Active != null && WebViewUGUI.Active.IsOpen) return false;
        var anvil = FindFirstObjectByType<AnvilUI>();
        if (anvil != null && anvil.IsOpen) return false;
        return _phone == null || !_phone.IsOpen;
    }

    private void OpenGameplayMenu()
    {
        if (_openMenu == this) return;
        _previousCursorLock = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
        _capturedControls = true;
        CapturePlayerControls();
        _openMenu = this;
        menuRoot.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ShowHome();
    }

    private void CloseGameplayMenu()
    {
        if (titleScreen || _loading || _openMenu != this) return;
        menuRoot.SetActive(false);
        _openMenu = null;
        _pendingAction = PendingAction.None;
        if (!_capturedControls) return;
        _capturedControls = false;
        _capturedPlayerInput = false;
        if (_playerInput != null && _inputWasActive && _playerInput.enabled)
        {
            _playerInput.ActivateInput();
            if (!string.IsNullOrEmpty(_previousActionMap) && _playerInput.currentActionMap?.name != _previousActionMap)
                _playerInput.SwitchCurrentActionMap(_previousActionMap);
        }
        Cursor.lockState = _previousCursorLock;
        Cursor.visible = _previousCursorVisible;
    }

    /// <summary>Restore the menu's snapshot before death/revive applies a new control state.</summary>
    public static void CloseForPlayerStateChange()
    {
        if (_openMenu != null && !_openMenu.titleScreen && !_openMenu._loading && !_openMenu._waitingForCheckpoint)
            _openMenu.CloseGameplayMenu();
    }

    private void CapturePlayerControls()
    {
        if (_playerInput == null || _capturedPlayerInput) return;
        _capturedPlayerInput = true;
        _inputWasActive = _playerInput.inputIsActive;
        _previousActionMap = _playerInput.currentActionMap?.name;
        _playerInput.DeactivateInput();
        if (_inputHandler != null)
        {
            _inputHandler.ProcessFireInput(false);
            _inputHandler.ProcessLookInput(Vector2.zero);
        }
    }

    private void LateUpdate()
    {
        if (!_waitingForCheckpoint) return;
        // A newly spawned local player may enable controls after the menu's early Update.
        CapturePlayerControls();
        if (_playerInput != null && _playerInput.inputIsActive) _playerInput.DeactivateInput();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void PollSession()
    {
        if (_sessionFailed) return;
        CacheLocalPlayer();

        var steamRoom = SteamRoomService.Instance;
        if (steamRoom != null && steamRoom.IsGameSession && steamRoom.State == SteamRoomState.Error)
        {
            FailSession(steamRoom.Status);
            return;
        }

        var manager = NetworkManager.main;
        bool connected = manager != null && manager.clientState == ConnectionState.Connected;
        if (_hadConnection && !connected)
        {
            FailSession("Connection lost. Return to title.");
            return;
        }
        _hadConnection |= connected;
        var save = RunSaveService.Instance;
        if (save != null && save.IsSoloCheckpointSession && !save.IsReady && !string.IsNullOrEmpty(save.LastError))
        {
            FailSession(save.LastError);
            return;
        }
        _sessionReady = connected && _playerInput != null
            && (save == null || !save.IsSoloCheckpointSession || save.IsReady);
        if (_sessionReady) GameSceneLoading.PlayerReady();
        if (!_sessionReady && Time.realtimeSinceStartup - _startedAt >= 60f)
        {
            FailSession("Startup failed. Return to title and retry.");
            return;
        }
        if (save != null && save.IsSoloCheckpointSession && !save.IsReady)
        {
            if (!_waitingForCheckpoint)
            {
                _waitingForCheckpoint = true;
                OpenGameplayMenu();
                SetBusy(true);
            }
            statusText.text = $"Loading... {Mathf.FloorToInt(Time.realtimeSinceStartup - _startedAt)}s";
        }
        else if (_waitingForCheckpoint && _sessionReady)
        {
            _waitingForCheckpoint = false;
            SetBusy(false);
            CloseGameplayMenu();
        }
    }

    private void CacheLocalPlayer()
    {
        if (_localPlayer != null) return;
        _localPlayer = NetworkPlayer.Local;
        if (_localPlayer == null) return;
        _playerInput = _localPlayer.GetComponent<PlayerInput>();
        _inputHandler = _localPlayer.GetComponent<FPSInputHandler>();
        _vitals = _localPlayer.GetComponent<PlayerVitals>();
        _phone = _localPlayer.GetComponent<DungeonRoomPowerPhone>();
        if (_waitingForCheckpoint)
        {
            _previousCursorLock = CursorLockMode.Locked;
            _previousCursorVisible = false;
            CapturePlayerControls();
        }
    }

    private void FailSession(string message)
    {
        _sessionFailed = true;
        _waitingForCheckpoint = false;
        _sessionError = message;
        OpenGameplayMenu();
        SetBusy(false);
        ShowHome();
        statusText.text = _sessionError;
    }

    private void ShowHome()
    {
        bool wasShowingCredits = creditsPanel.activeSelf;
        _pendingAction = PendingAction.None;
        homePanel.SetActive(true);
        optionsPanel.SetActive(false);
        confirmationPanel.SetActive(false);
        creditsPanel.SetActive(false);
        if (steamRoomPanel != null) steamRoomPanel.ShowHome();
        if (titleScreen)
        {
            bool hasSave = RunSaveService.HasSave;
            continueButton.interactable = !_loading && hasSave;
            statusText.text = hasSave ? RunSaveService.SaveSummary : string.Empty;
        }
        else
        {
            resumeButton.interactable = !_loading && !_sessionFailed;
            statusText.text = _sessionFailed ? _sessionError
                : RunSaveService.HasSave ? RunSaveService.SaveSummary : string.Empty;
            var save = RunSaveService.Instance;
            if (!_sessionFailed && save != null && !string.IsNullOrEmpty(save.LastError))
                statusText.text += (statusText.text.Length > 0 ? "\n" : string.Empty) + save.LastError;
        }
        if (wasShowingCredits && creditsButton.gameObject.activeInHierarchy && creditsButton.interactable)
            creditsButton.Select();
    }

    private void ShowCredits()
    {
        if (_loading || !titleScreen) return;
        if (steamRoomPanel != null) steamRoomPanel.HidePanels();
        _pendingAction = PendingAction.None;
        homePanel.SetActive(false);
        optionsPanel.SetActive(false);
        confirmationPanel.SetActive(false);
        creditsPanel.SetActive(true);
        creditsScroll.StopMovement();
        Canvas.ForceUpdateCanvases();
        creditsScroll.verticalNormalizedPosition = 1f;
        creditsBackButton.Select();
    }

    private void RequestNewGame()
    {
        if (_loading || (steamRoomPanel != null && steamRoomPanel.IsRoomActive)) return;
        if (RunSaveService.HasSave)
            ShowConfirmation(PendingAction.NewGame, "Replace your saved game?");
        else StartNewGame();
    }

    private void StartNewGame()
    {
        if (_loading || (steamRoomPanel != null && steamRoomPanel.IsRoomActive)) return;
        RunSaveService.BeginNewGame();
        StartCoroutine(LoadScene(GameScene));
    }

    private void ContinueGame()
    {
        if (_loading || (steamRoomPanel != null && steamRoomPanel.IsRoomActive)) return;
        if (!RunSaveService.TryPrepareContinue(out string error))
        {
            ShowHome();
            statusText.text = error;
            return;
        }
        StartCoroutine(LoadScene(GameScene));
    }

    private void RequestReturn()
    {
        if (!_loading)
            ShowConfirmation(PendingAction.ReturnToTitle,
                "Return to title?\nUnsaved progress will be lost.");
    }

    private void RequestQuit()
    {
        if (!_loading)
            ShowConfirmation(PendingAction.Quit, titleScreen ? "Quit the game?"
                : "Quit the game?\nUnsaved progress will be lost.");
    }

    private void ShowConfirmation(PendingAction action, string message)
    {
        if (steamRoomPanel != null) steamRoomPanel.HidePanels();
        _pendingAction = action;
        homePanel.SetActive(false);
        optionsPanel.SetActive(false);
        creditsPanel.SetActive(false);
        confirmationPanel.SetActive(true);
        confirmationText.text = message;
    }

    private void ConfirmAction()
    {
        if (_loading) return;
        PendingAction action = _pendingAction;
        _pendingAction = PendingAction.None;
        switch (action)
        {
            case PendingAction.NewGame: StartNewGame(); break;
            case PendingAction.ReturnToTitle: StartCoroutine(LeaveSession(false)); break;
            case PendingAction.Quit: StartCoroutine(LeaveSession(true)); break;
        }
    }

    private IEnumerator LeaveSession(bool quit)
    {
        _loading = true;
        ShowHome();
        SetBusy(true);
        statusText.text = "Closing...";
        var manager = NetworkManager.main;
        if (manager != null)
        {
            var starter = manager.GetComponent<PurrLobby.MyConnectionStarter>();
            if (starter != null)
            {
                starter.StopAllCoroutines();
                starter.enabled = false;
            }
            if (manager.clientState != ConnectionState.Disconnected) manager.StopClient();
            if (manager.serverState != ConnectionState.Disconnected) manager.StopServer();
            float deadline = Time.realtimeSinceStartup + 8f;
            while (manager != null && (manager.clientState != ConnectionState.Disconnected
                    || manager.serverState != ConnectionState.Disconnected) && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (manager != null && (manager.clientState != ConnectionState.Disconnected
                    || manager.serverState != ConnectionState.Disconnected))
            {
                _loading = false;
                FailSession("Disconnect timed out. Return to title or quit to retry.");
                yield break;
            }
        }
        if (!TryLeaveLobby(out string lobbyError))
        {
            _loading = false;
            FailSession(lobbyError);
            yield break;
        }
        if (manager != null && manager.IsDontDestroyOnLoad()) Destroy(manager.gameObject);
        CompanyHud.DestroyIfExists();
        yield return null;
        if (quit)
        {
            Application.Quit();
#if UNITY_EDITOR
            _loading = false;
            _sessionFailed = !titleScreen;
            _sessionError = "Session closed. Return to title.";
            SetBusy(false);
            ShowHome();
            statusText.text = _sessionError;
#endif
            yield break;
        }
        yield return LoadScene(TitleScene);
    }

    private bool TryLeaveLobby(out string error)
    {
        error = null;
        var roomService = SteamRoomService.Instance;
        if (roomService != null)
        {
            try { roomService.LeaveRoom(); }
            catch (Exception exception)
            {
                error = "Unable to leave the room. Retry. " + exception.Message;
                return false;
            }
            if (roomService.IsBusy || !string.IsNullOrEmpty(roomService.RoomCode))
            {
                error = "The room is still closing. Retry.";
                return false;
            }
            return true;
        }
        var holder = FindFirstObjectByType<PurrLobby.LobbyDataHolder>();
        if (holder == null) return true;
        if (holder.CurrentLobby.IsValid && ulong.TryParse(holder.CurrentLobby.LobbyId, out ulong lobbyId))
        {
            try { Steamworks.SteamMatchmaking.LeaveLobby(new Steamworks.CSteamID(lobbyId)); }
            catch (Exception exception)
            {
                error = "Unable to leave the lobby. Please retry. " + exception.Message;
                return false;
            }
        }
        holder.SetCurrentLobby(default);
        Destroy(holder.gameObject);
        return true;
    }

    private IEnumerator LoadScene(string scenePath)
    {
        _loading = true;
        ShowHome();
        SetBusy(true);
        statusText.text = "Loading map...";
        // Show feedback before the first synchronous part of LoadSceneAsync.
        yield return null;
        AsyncOperation operation = null;
        string loadError = null;
        try { operation = GameSceneLoading.Begin(scenePath); }
        catch (Exception exception) { loadError = exception.Message; }
        if (operation == null)
        {
            _loading = false;
            if (!titleScreen) _sessionFailed = true;
            SetBusy(false);
            statusText.text = "Unable to load the scene. " + loadError;
            yield break;
        }
        while (!operation.isDone)
        {
            statusText.text = operation.progress >= 0.9f ? "Preparing map..."
                : $"Loading map... {Mathf.RoundToInt(Mathf.Clamp01(operation.progress / 0.9f) * 100f)}% · {GameSceneLoading.ElapsedSeconds:F0}s";
            yield return null;
        }
    }

    private void SetBusy(bool busy)
    {
        if (steamRoomPanel != null) steamRoomPanel.SetMenuBusy(busy);
        foreach (var button in _buttons) button.interactable = !busy;
        volumeSlider.interactable = !busy;
        sensitivitySlider.interactable = !busy;
        fullscreenToggle.interactable = !busy;
        vSyncToggle.interactable = !busy;
        if (!busy)
        {
            continueButton.interactable = titleScreen && RunSaveService.HasSave;
            resumeButton.interactable = !titleScreen && !_sessionFailed;
            RefreshOptions();
        }
    }

    private void ShowOptions()
    {
        if (_loading) return;
        if (steamRoomPanel != null) steamRoomPanel.HidePanels();
        homePanel.SetActive(false);
        confirmationPanel.SetActive(false);
        creditsPanel.SetActive(false);
        optionsPanel.SetActive(true);
        RefreshOptions();
    }

    private void RefreshOptions()
    {
        volumeSlider.SetValueWithoutNotify(GameOptions.MasterVolume);
        sensitivitySlider.SetValueWithoutNotify(GameOptions.MouseSensitivityMultiplier);
        fullscreenToggle.SetIsOnWithoutNotify(GameOptions.Fullscreen);
        vSyncToggle.SetIsOnWithoutNotify(GameOptions.VSync);
        volumeValue.text = $"{GameOptions.MasterVolume * 100f:F0}%";
        sensitivityValue.text = $"{GameOptions.MouseSensitivityMultiplier:F2}x";
        string[] qualities = QualitySettings.names;
        qualityValue.text = "QUALITY  /  " + (qualities.Length > 0 ? qualities[GameOptions.QualityLevel] : "Default");
        qualityButton.interactable = !_loading && qualities.Length > 1;
        frameRateValue.text = "FRAME LIMIT  /  " + (GameOptions.VSync ? "V-Sync"
            : GameOptions.FrameRateLimit == 0 ? "Unlimited" : $"{GameOptions.FrameRateLimit} FPS");
        frameRateButton.interactable = !_loading && !GameOptions.VSync;
    }

    private void ChangeVolume(float value) { GameOptions.MasterVolume = value; ApplyOptions(); }
    private void ChangeSensitivity(float value) { GameOptions.MouseSensitivityMultiplier = value; ApplyOptions(); }
    private void ChangeFullscreen(bool value) { GameOptions.Fullscreen = value; ApplyOptions(); }
    private void ChangeVSync(bool value) { GameOptions.VSync = value; ApplyOptions(); }

    private void CycleQuality()
    {
        if (QualitySettings.names.Length > 0)
            GameOptions.QualityLevel = (GameOptions.QualityLevel + 1) % QualitySettings.names.Length;
        ApplyOptions();
    }

    private void CycleFrameRate()
    {
        GameOptions.FrameRateLimit = FrameRates[(Array.IndexOf(FrameRates, GameOptions.FrameRateLimit) + 1) % FrameRates.Length];
        ApplyOptions();
    }

    private void ApplyOptions()
    {
        GameOptions.Apply();
        GameOptions.Save();
        RefreshOptions();
    }

    private void RestoreDefaults()
    {
        GameOptions.ResetDefaults();
        RefreshOptions();
    }

    private void OnDestroy()
    {
        if (_openMenu == this) _openMenu = null;
    }
}
