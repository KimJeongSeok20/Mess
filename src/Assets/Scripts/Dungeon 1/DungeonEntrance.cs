using UnityEngine;
using Demo.Scripts.Runtime.Character;

/// <summary>
/// StartMap에 있는 던전 입구 문.
/// F를 누르면 플레이어를 던전 시작 지점으로 텔레포트.
/// </summary>
public class DungeonEntrance : AInteractable
{

    [Header("Dungeon State")]
    [SerializeField] private NetworkDungeonController dungeonController;

    [Header("Prompt 문구")]
    [SerializeField] private string promptActive = "[F] Enter the dungeon";
    [SerializeField] private string promptInactive = "Dungeon is inactive.";


    private PromptPresenter _prompt;   // 네가 쓰던 프롬프트 매니저

    [SerializeField] private DungeonZoneManager zoneManager;

    // 입구 문에 Door 컴포넌트가 함께 붙어도 진입 텔레포트가 F를 가져가도록 한다.
    public override int InteractPriority => 100;

    private void Awake()
    {
        _prompt = FindObjectOfType<PromptPresenter>();
        if (dungeonController == null)
            dungeonController = FindObjectOfType<NetworkDungeonController>();

        if (zoneManager == null) zoneManager = FindObjectOfType<DungeonZoneManager>();
    }

    // 실제 상호작용 (F 눌렀을 때)
    public override void Interact(InteractionManager interactor)
    {
        // ✅ 실제 동작만 막기
        bool active = (dungeonController != null && dungeonController.IsDungeonActive);
        if (!active)
        {
            if (_prompt != null) _prompt.Show(promptInactive);
            return;
        }

        // A client that is still generating would be teleported into empty space (audit M4).
        if (!dungeonController.IsLocalDungeonReady || (TimeManager.Active != null && TimeManager.Active.IsPreparingDungeon))
        {
            if (_prompt != null) _prompt.Show(string.IsNullOrEmpty(dungeonController.MapLoadError)
                ? "The dungeon is still forming..." : "Dungeon preparation failed. Return to the title.");
            return;
        }

        Transform target = DungeonStartPoint.Instance;
        if (target == null)
        {
            if (_prompt != null) _prompt.Show("Dungeon start point missing.");
            return;
        }

        var cam = interactor != null ? interactor.CurrentCamera : null;
        if (cam == null)
        {
            Debug.LogWarning("[DungeonEntrance] Main Camera 없음");
            return;
        }

        Transform playerRoot = cam.transform.root;
        var cc = playerRoot.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        playerRoot.position = target.position;
        playerRoot.rotation = target.rotation;

        if (cc != null) cc.enabled = true;

        zoneManager?.EnterDungeon(playerRoot);

        var fpsController = playerRoot.GetComponentInChildren<FPSController>();
        if (fpsController != null)
        {
            fpsController.SetDungeonPostProcessing(true);
            fpsController.SetDungeonRenderLayerServerRpc(true);
        }
    }

    // 플레이어가 문을 바라볼 때
    public override void OnHover()
    {
        if (_prompt == null) return;

        bool active = (dungeonController != null && dungeonController.IsDungeonActive);

        _prompt.Show(active ? promptActive : promptInactive);
    }

    // 시선이 문에서 떨어졌을 때
    public override void OnStopHover()
    {
        if (_prompt != null)
            _prompt.Hide();
    }

    public override bool CanInteract()
    {
        // ✅ Hover/Prompt는 항상 뜨게
        return true;
    }

}
