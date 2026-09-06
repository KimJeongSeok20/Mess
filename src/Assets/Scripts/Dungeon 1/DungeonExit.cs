using UnityEngine;
using Demo.Scripts.Runtime.Character;

public class DungeonExit : AInteractable
{
    [Header("Prompt")]
    [SerializeField] private string prompt = "[F] Exit";

    private PromptPresenter _prompt;
    [SerializeField] private DungeonZoneManager zoneManager;

    // 출구 문에는 Door 컴포넌트도 같이 붙어있다. 문 여닫기가 아니라 복귀 텔레포트가 F를 가져가야 한다.
    public override int InteractPriority => 100;

    private void Awake()
    {
        _prompt = FindObjectOfType<PromptPresenter>();

        if (zoneManager == null) zoneManager = FindObjectOfType<DungeonZoneManager>();
    }

    public override void OnHover()
    {
        if (_prompt != null) _prompt.Show(prompt);
    }

    public override void OnStopHover()
    {
        if (_prompt != null) _prompt.Hide();
    }

    public override bool CanInteract()
    {
        // Hover�� ���� �ϴϱ� true�� �ΰ�, Interact���� ���� �� ����
        return true;
    }

    public override void Interact(InteractionManager interactor)
    {
        var target = StartMapReturnPoint.Instance;
        if (target == null)
        {
            Debug.LogWarning("[DungeonExit] StartMapReturnPoint.Instance is null");
            if (_prompt != null) _prompt.Show("Return point missing!");
            return;
        }

        var cam = interactor != null ? interactor.CurrentCamera : null;
        if (cam == null)
        {
            Debug.LogWarning("[DungeonExit] CurrentCamera ����");
            return;
        }

        Transform playerRoot = cam.transform.root;

        var cc = playerRoot.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        playerRoot.position = target.position;
        playerRoot.rotation = target.rotation;

        if (cc != null) cc.enabled = true;
        zoneManager?.ExitDungeon(playerRoot);

        var fpsController = playerRoot.GetComponentInChildren<FPSController>();
        if (fpsController != null)
        {
            fpsController.SetDungeonPostProcessing(false);
            fpsController.SetDungeonRenderLayerServerRpc(false);
        }
    }
}
