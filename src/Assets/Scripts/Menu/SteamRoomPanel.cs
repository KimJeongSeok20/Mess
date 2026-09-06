using TMPro;
using UnityEngine;

/// <summary>Inspector-authored Steam room controls for the title menu.</summary>
[DisallowMultipleComponent]
public sealed class SteamRoomPanel : MonoBehaviour
{
    [SerializeField] private SteamRoomService service;
    [SerializeField] private GameObject homePanel;
    [SerializeField] private GameObject roomPanel;
    [SerializeField] private UnityEngine.UI.Button createRoomButton;
    [SerializeField] private UnityEngine.UI.Button joinRoomButton;
    [SerializeField] private UnityEngine.UI.Button startRoomButton;
    [SerializeField] private UnityEngine.UI.Button leaveRoomButton;
    [SerializeField] private TMP_InputField roomCodeInput;
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private TMP_Text membersText;
    [SerializeField] private TMP_Text roomStatusText;

    private bool _showingHome = true;
    private bool _menuBusy;

    public bool IsRoomActive => service != null && (service.IsBusy
        || !string.IsNullOrEmpty(service.RoomCode) || service.State == SteamRoomState.InRoom
        || service.State == SteamRoomState.InGame);

    private void Awake()
    {
        roomPanel.SetActive(false);
        roomCodeInput.characterLimit = 6;
        createRoomButton.onClick.AddListener(CreateRoom);
        joinRoomButton.onClick.AddListener(JoinRoom);
        startRoomButton.onClick.AddListener(StartRoom);
        leaveRoomButton.onClick.AddListener(LeaveRoom);
        roomCodeInput.onSubmit.AddListener(JoinFromInput);
    }

    private void Start()
    {
        // Returning to the title creates an authored duplicate; the active room service persists.
        if (SteamRoomService.Instance != null) service = SteamRoomService.Instance;
        service.Changed += Refresh;
        Refresh();
    }

    public void ShowHome()
    {
        _showingHome = true;
        Refresh();
    }

    public void HidePanels()
    {
        _showingHome = false;
        homePanel.SetActive(false);
        roomPanel.SetActive(false);
        roomStatusText.text = string.Empty;
    }

    public void SetMenuBusy(bool busy)
    {
        _menuBusy = busy;
        Refresh();
    }

    private void Refresh()
    {
        if (service == null) return;
        bool roomActive = IsRoomActive;
        bool canEnter = !_menuBusy && !roomActive;
        homePanel.SetActive(_showingHome && !roomActive);
        roomPanel.SetActive(_showingHome && roomActive);
        createRoomButton.interactable = canEnter;
        joinRoomButton.interactable = canEnter;
        roomCodeInput.interactable = canEnter;
        startRoomButton.gameObject.SetActive(service.IsOwner);
        startRoomButton.interactable = !_menuBusy && !service.IsBusy
            && service.State == SteamRoomState.InRoom && service.IsOwner;
        leaveRoomButton.interactable = !_menuBusy && !service.IsBusy;
        roomCodeText.text = string.IsNullOrEmpty(service.RoomCode) ? string.Empty : "ROOM  /  " + service.RoomCode;
        membersText.text = string.IsNullOrEmpty(service.RoomCode) ? string.Empty
            : $"{service.MemberCount} PLAYER{(service.MemberCount == 1 ? string.Empty : "S")}";
        roomStatusText.text = _showingHome && !_menuBusy
            && (service.IsBusy || service.State == SteamRoomState.Error) ? service.Status : string.Empty;
    }

    private void CreateRoom()
    {
        if (!_showingHome || _menuBusy || IsRoomActive) return;
        service.CreateRoom();
        Refresh();
    }

    private void JoinRoom()
    {
        if (!_showingHome || _menuBusy || IsRoomActive) return;
        service.JoinRoom(roomCodeInput.text.Trim());
        Refresh();
    }

    private void JoinFromInput(string value) => JoinRoom();

    private void StartRoom()
    {
        if (!_showingHome || _menuBusy || service.IsBusy || !service.IsOwner || service.State != SteamRoomState.InRoom) return;
        service.StartGame();
        Refresh();
    }

    private void LeaveRoom()
    {
        if (!_showingHome || _menuBusy || service.IsBusy) return;
        service.LeaveRoom();
        Refresh();
    }

    private void OnDestroy()
    {
        if (service != null) service.Changed -= Refresh;
    }
}
