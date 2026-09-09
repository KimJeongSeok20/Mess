using System.Collections;
using Demo.Scripts.Runtime.Character;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.Shared.KAnimationCore.Runtime.Input;
using PurrNet;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Looks at the handheld phone, then waits for a separate UI action to toggle
/// the current dungeon room between P0 and P100.
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonRoomPowerPhone : MonoBehaviour
{
    [SerializeField] private FPSAnimationAsset lookAnimation;
    [SerializeField] private FPSAnimationAsset holdPose;
    [SerializeField, Min(0.05f)] private float poseBlendIn = 0.35f;
    [SerializeField, Min(0.05f)] private float poseBlendOut = 0.28f;

    [Header("Right Hand Camera Lift")]
    [SerializeField] private Vector3 rightClavicleExtraEuler = new Vector3(8f, 0f, 14f);
    [SerializeField] private Vector3 rightUpperArmExtraEuler = new Vector3(-12f, 10f, 28f);
    [SerializeField] private Vector3 rightLowerArmExtraEuler = new Vector3(6f, 18f, 8f);

    private FPSController _controller;
    private IPlayablesController _playables;
    private PlayerInput _playerInput;
    private UserInputController _userInput;
    private NetworkDungeonController _dungeon;
    private Coroutine _openRoutine;

    private bool _isOpen;
    private bool _isClosing;
    private bool _controlsReady;
    private float _poseBlend;
    private Canvas _canvas;
    private TextMeshProUGUI _statusText;
    private TextMeshProUGUI _buttonText;
    private Button _powerButton;
    private Transform _rightClavicle;
    private Transform _rightUpperArm;
    private Transform _rightLowerArm;
    private bool _bonesCached;

    public bool IsOpen => _isOpen;

    private void Awake()
    {
        _controller = GetComponent<FPSController>();
        _playables = GetComponent<IPlayablesController>();
        _playerInput = GetComponent<PlayerInput>();
        _userInput = GetComponent<UserInputController>();
    }

    private void OnDisable()
    {
        if (_isOpen)
            CloseImmediate();
    }

    public void HandleToggleInput()
    {
        if ((SkillWebTerminalInteraction.BlocksGameplayInput || AnvilUI.BlocksGameplayInput))
            return;

        if (_controller == null || !_controller.isOwner)
            return;

        if (_isClosing)
            return;

        if (_isOpen)
        {
            Close();
            return;
        }

        Open();
    }

    private void Update()
    {
        if (!_isOpen || _controller == null || !_controller.isOwner)
            return;

        var inventory = InstanceHandler.GetInstance<InventoryManager>();
        if (inventory != null && inventory.IsInventoryOpen())
        {
            Close();
            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        if (_controlsReady)
            RefreshStatus();
    }

    private void LateUpdate()
    {
        float target = _isOpen && !_isClosing ? 1f : 0f;
        float duration = target > _poseBlend ? poseBlendIn : poseBlendOut;
        _poseBlend = Mathf.MoveTowards(
            _poseBlend,
            target,
            duration > 0.0001f ? Time.deltaTime / duration : 1f);

        if (_poseBlend <= 0.0001f)
            return;

        CacheArmBones();
        ApplyLocalLift(_rightClavicle, rightClavicleExtraEuler * _poseBlend);
        ApplyLocalLift(_rightUpperArm, rightUpperArmExtraEuler * _poseBlend);
        ApplyLocalLift(_rightLowerArm, rightLowerArmExtraEuler * _poseBlend);
    }

    private void Open()
    {
        if (_isOpen)
            return;

        var inventory = InstanceHandler.GetInstance<InventoryManager>();
        if (inventory != null && inventory.IsInventoryOpen())
            return;

        if (_controller == null || !_controller.TryBeginPhoneInspect())
            return;

        _isOpen = true;
        _isClosing = false;
        _controlsReady = false;
        SwitchToUiMap();
        _controller.SetPhoneInspectIkSuppressed(true);
        PlayHold();
        ShowUi();
        UnlockCursor();
        _controlsReady = true;
        RefreshStatus();
    }

    private void Close()
    {
        if (!_isOpen || _isClosing)
            return;

        if (_openRoutine != null)
            StopCoroutine(_openRoutine);
        _openRoutine = StartCoroutine(CloseRoutine());
    }

    private void CloseImmediate()
    {
        if (_openRoutine != null)
        {
            StopCoroutine(_openRoutine);
            _openRoutine = null;
        }

        FinishClose(0f);
    }

    private IEnumerator CloseRoutine()
    {
        _isClosing = true;
        _controlsReady = false;
        HideUi();
        RestoreGameplayInput();

        if (_playables != null)
            _playables.StopAnimation(poseBlendOut);

        if (poseBlendOut > 0f)
            yield return new WaitForSeconds(poseBlendOut);

        FinishClose(poseBlendOut);
        _openRoutine = null;
    }

    private void FinishClose(float blendOutTime)
    {
        _isOpen = false;
        _isClosing = false;
        _controlsReady = false;

        if (_playables != null && blendOutTime <= 0.0001f)
            _playables.StopAnimation(0f);

        if (_controller != null)
        {
            _controller.EndPhoneInspect();
            _controller.SetPhoneInspectIkSuppressed(false);
            _controller.RestoreActiveItemPose();
        }

        HideUi();
        RestoreGameplayInput();
    }

    private void PlayHold()
    {
        if (_playables == null || !FPSAnimationAsset.IsValid(holdPose))
            return;

        _userInput?.SetValue("PlayablesWeight", 1f);
        _playables.PlayHeldPose(holdPose, poseBlendIn, poseBlendOut);
    }

    private void OnPowerClicked()
    {
        if (!_isOpen || !_controlsReady || _controller == null || !_controller.isOwner)
            return;

        NetworkDungeonController dungeon = ResolveDungeon();
        if (dungeon == null)
        {
            SetStatus("No dungeon.");
            return;
        }

        // Battery empty: the button becomes "recharge" and eats one battery item (Jerrycan).
        if (!dungeon.HasPower)
        {
            var inventory = InstanceHandler.GetInstance<InventoryManager>();
            string receipt = inventory != null ? inventory.FindReceipt(batteryItemName) : null;
            if (!string.IsNullOrEmpty(receipt))
            {
                dungeon.RequestRechargeServerRpc(receipt);
                SetStatus($"Recharging +{dungeon.RechargeAmountPerItem:0}...");
            }
            else
            {
                SetStatus($"NO POWER\nBring a {batteryItemName} to recharge.");
                PromptPresenter.ShowPrompt($"No power. Bring a {batteryItemName}.");
            }

            Invoke(nameof(RefreshStatus), 0.6f);
            return;
        }

        dungeon.DebugToggleRoomPowerAtPosition(_controller.transform.position);
        Invoke(nameof(RefreshStatus), 0.3f);
        RefreshStatus();
    }

    [Header("Battery")]
    [Tooltip("전력을 충전하는 소모품 아이템 이름 (던전 루팅 Jerrycan을 배터리로 쓴다)")]
    [SerializeField] private string batteryItemName = "Jerrycan";

    private string BuildPowerLine(NetworkDungeonController dungeon)
    {
        if (dungeon == null || !dungeon.IsDungeonActive)
            return string.Empty;

        int lit = dungeon.LitRoomCount;
        string drain = lit > 0 ? $"  ▼{lit} room{(lit == 1 ? string.Empty : "s")} on" : string.Empty;
        return $"\nBATTERY {dungeon.Power:0}/{dungeon.PowerCapacity:0} ({dungeon.PowerNormalized:P0}){drain}";
    }

    private void RefreshStatus()
    {
        if (_statusText == null)
            return;

        NetworkDungeonController dungeon = ResolveDungeon();
        if (dungeon == null ||
            !dungeon.TryGetRoomPowerAtPosition(_controller.transform.position, out var level, out string roomName))
        {
            SetStatus("No powered room here.");
            if (_buttonText != null)
                _buttonText.text = "No room";
            if (_powerButton != null)
                _powerButton.interactable = false;
            return;
        }

        bool lightsOn = level == DungeonTileLightmapSwitcher.PowerLevel.P100;
        string powerLine = BuildPowerLine(dungeon);
        SetStatus((lightsOn
            ? $"{roomName}\nLights ON (P100)"
            : $"{roomName}\nLights OFF (P0)") + powerLine);

        bool hasPower = dungeon.HasPower;
        if (_buttonText != null)
            _buttonText.text = !hasPower ? $"Recharge ({batteryItemName})" : lightsOn ? "Turn lights off" : "Turn lights on";
        if (_powerButton != null)
            _powerButton.interactable = true;
    }

    private void SetStatus(string text)
    {
        if (_statusText != null)
            _statusText.text = text;
    }

    private NetworkDungeonController ResolveDungeon()
    {
        if (_dungeon == null)
            _dungeon = FindFirstObjectByType<NetworkDungeonController>();
        return _dungeon;
    }

    private void SwitchToUiMap()
    {
        if (_playerInput != null)
            _playerInput.SwitchCurrentActionMap("UI");
    }

    private void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RestoreGameplayInput()
    {
        var inventory = InstanceHandler.GetInstance<InventoryManager>();
        bool inventoryOpen = inventory != null && inventory.IsInventoryOpen();

        if (_playerInput != null && !inventoryOpen)
            _playerInput.SwitchCurrentActionMap("Gameplay");

        if (!inventoryOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void ShowUi()
    {
        EnsureUi();
        if (_canvas != null)
            _canvas.gameObject.SetActive(true);
    }

    private void HideUi()
    {
        if (_canvas != null)
            _canvas.gameObject.SetActive(false);
    }

    private void EnsureUi()
    {
        if (_canvas != null)
            return;

        var root = new GameObject("RoomPowerPhoneCanvas");
        root.transform.SetParent(transform, false);

        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 80;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        Sprite white = CreateWhiteSprite();

        var panel = CreateImage(root.transform, "PhonePanel", new Color(0.07f, 0.08f, 0.1f, 0.94f), white);
        var panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(1f, 0f);
        panelRect.anchorMax = new Vector2(1f, 0f);
        panelRect.pivot = new Vector2(1f, 0f);
        panelRect.anchoredPosition = new Vector2(-48f, 48f);
        panelRect.sizeDelta = new Vector2(320f, 420f);

        var bezel = CreateImage(panelRect, "Bezel", new Color(0.16f, 0.18f, 0.22f, 1f), white);
        bezel.rectTransform.anchorMin = Vector2.zero;
        bezel.rectTransform.anchorMax = Vector2.one;
        bezel.rectTransform.offsetMin = new Vector2(10f, 10f);
        bezel.rectTransform.offsetMax = new Vector2(-10f, -10f);

        var screen = CreateImage(bezel.rectTransform, "Screen", new Color(0.05f, 0.07f, 0.09f, 1f), white);
        screen.rectTransform.anchorMin = Vector2.zero;
        screen.rectTransform.anchorMax = Vector2.one;
        screen.rectTransform.offsetMin = new Vector2(12f, 12f);
        screen.rectTransform.offsetMax = new Vector2(-12f, -12f);

        _statusText = CreateText(screen.rectTransform, "Status", 22f, TextAlignmentOptions.Top);
        var statusRect = _statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0f, 0.55f);
        statusRect.anchorMax = new Vector2(1f, 1f);
        statusRect.offsetMin = new Vector2(16f, 8f);
        statusRect.offsetMax = new Vector2(-16f, -16f);
        _statusText.text = "Room power";

        var buttonObject = CreateImage(screen.rectTransform, "PowerButton", new Color(0.85f, 0.78f, 0.28f, 1f), white);
        var buttonRect = buttonObject.rectTransform;
        buttonRect.anchorMin = new Vector2(0.12f, 0.18f);
        buttonRect.anchorMax = new Vector2(0.88f, 0.46f);
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;
        _powerButton = buttonObject.gameObject.AddComponent<Button>();
        _powerButton.targetGraphic = buttonObject;
        var colors = _powerButton.colors;
        colors.highlightedColor = new Color(0.95f, 0.9f, 0.4f, 1f);
        colors.pressedColor = new Color(0.7f, 0.64f, 0.18f, 1f);
        _powerButton.colors = colors;
        _powerButton.onClick.AddListener(OnPowerClicked);

        _buttonText = CreateText(buttonRect, "Label", 24f, TextAlignmentOptions.Center);
        _buttonText.rectTransform.anchorMin = Vector2.zero;
        _buttonText.rectTransform.anchorMax = Vector2.one;
        _buttonText.rectTransform.offsetMin = Vector2.zero;
        _buttonText.rectTransform.offsetMax = Vector2.zero;
        _buttonText.text = "Turn lights on";
        _buttonText.color = new Color(0.12f, 0.12f, 0.12f, 1f);

        var hint = CreateText(screen.rectTransform, "Hint", 16f, TextAlignmentOptions.Bottom);
        var hintRect = hint.rectTransform;
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0.16f);
        hintRect.offsetMin = new Vector2(12f, 8f);
        hintRect.offsetMax = new Vector2(-12f, -4f);
        hint.text = "P / Esc to put away";
        hint.color = new Color(0.7f, 0.72f, 0.75f, 1f);

        _canvas.gameObject.SetActive(false);
    }

    private static Image CreateImage(Transform parent, string name, Color color, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = true;
        return image;
    }

    private static TextMeshProUGUI CreateText(
        Transform parent,
        string name,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.enableWordWrapping = true;
        return text;
    }

    private static Sprite CreateWhiteSprite()
    {
        Texture2D texture = Texture2D.whiteTexture;
        return Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
    }

    private void CacheArmBones()
    {
        if (_bonesCached)
            return;

        _rightClavicle = FindBoneByName("clavicle_r");
        _rightUpperArm = FindBoneByName("upperarm_r");
        _rightLowerArm = FindBoneByName("lowerarm_r");
        _bonesCached = true;
    }

    private Transform FindBoneByName(string boneName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != null && child.name == boneName)
                return child;
        }

        return null;
    }

    private static void ApplyLocalLift(Transform bone, Vector3 extraEuler)
    {
        if (bone == null || extraEuler.sqrMagnitude < 0.0001f)
            return;

        bone.localRotation *= Quaternion.Euler(extraEuler);
    }
}
