using UnityEngine;
using PurrNet;
using DunGen;        // ★ DunGen Door 사용
using DungeonDoor = DunGen.Door;

public class DoorInteraction : AInteractable
{
    public bool IsOpen => isOpen.value;
    public Collider SolidCollider => solidCollider;

    [Header("Initial State")]
    [SerializeField] private bool useDunGenInitialState = true; // DunGen Door.IsOpen을 초기값으로 쓸지
    [SerializeField] private bool startsOpen = true;            // DunGen이 없을 때 기본값
    [SerializeField] private SyncVar<bool> isOpen = new(true);  // 네트워크 동기화용

    [Header("Prompt Text")]
    [SerializeField] private string openText = "[F] Open the door";
    [SerializeField] private string closeText = "[F] Close the door";

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string openBoolParam = "IsOpen";   // 애니메이터 bool 파라미터 이름

    [Header("Collision")]
    [SerializeField] private Collider solidCollider;            // 문을 막는 콜라이더 (★ 상태는 건드리지 않음, 참고용)

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip openClip;
    [SerializeField] private AudioClip closeClip;

    // DunGen Door 컴포넌트 (Culling & Pathfinding 연동 핵심)
    private DungeonDoor _dunGenDoor;
    private PromptPresenter _prompt;

    #region Network LifeCycle

    protected override void OnSpawned()
    {
        base.OnSpawned();

        // 레퍼런스 자동 할당
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!solidCollider) solidCollider = GetComponentInChildren<Collider>();
        if (_prompt == null) _prompt = PromptPresenter.Instance ?? FindObjectOfType<PromptPresenter>();

        // DunGen Door 컴포넌트 (Doorway에 의해 스폰될 때 자동 추가됨)
        _dunGenDoor = GetComponent<DungeonDoor>();

        // SyncVar onChanged 구독
        isOpen.onChanged += OnOpenChanged;

        // ★ 서버에서 초기값 결정
        if (isServer)
        {
            bool initial;

            if (_dunGenDoor != null && useDunGenInitialState)
            {
                // DunGen이 만들어둔 문 상태를 그대로 가져와서 사용
                initial = _dunGenDoor.IsOpen;
            }
            else
            {
                // 또는 따로 설정한 startsOpen 사용
                initial = startsOpen;
            }

            isOpen.value = initial;
        }

        // 현재 값 기준으로 비주얼 적용
        ApplyState(isOpen.value);
    }

    private void OnDestroy()
    {
        isOpen.onChanged -= OnOpenChanged;
    }

    private void OnOpenChanged(bool newValue)
    {
        ApplyState(newValue);
    }

    #endregion

    #region Interaction Core

    public override void Interact()
    {
        // 서버가 아니면 서버에 요청
        if (!isServer)
        {
            ToggleServerRpc();
            return;
        }

        SetOpen(!isOpen.value);
    }

    [ServerRpc(requireOwnership: false)]
    private void ToggleServerRpc()
    {
        SetOpen(!isOpen.value);
    }

    [ServerRpc(requireOwnership: false)]
    public void SV_RequestOpen()
    {
        if (isOpen.value)
            return;

        SetOpen(true);
    }

    private void SetOpen(bool open)
    {
        if (!isServer)
            return;

        if (isOpen.value == open)
            return;

        isOpen.value = open;
    }

    private void ApplyState(bool open)
    {
        // 1) 애니메이션
        if (animator && !string.IsNullOrEmpty(openBoolParam))
        {
            animator.SetBool(openBoolParam, open);
        }

        // ★ 2) 콜라이더/트리거/Enabled 상태는 더 이상 건드리지 않는다.
        // - solidCollider는 Inspector에서 참조용으로만 사용
        // - 항상 같은 상태(Enabled, isTrigger 그대로)를 유지

        // 3) DunGen Door.IsOpen 연동 (★ 핵심)
        if (_dunGenDoor != null)
        {
            _dunGenDoor.IsOpen = open;
        }

        // 4) 사운드
        if (audioSource)
        {
            var clip = open ? openClip : closeClip;
            if (clip)
            {
                audioSource.PlayOneShot(clip);
            }
        }
    }

    #endregion

    #region Hover / Prompt

    public override void OnHover()
    {
        base.OnHover();

        if (_prompt == null)
            _prompt = PromptPresenter.Instance ?? FindObjectOfType<PromptPresenter>();

        if (_prompt != null)
        {
            string text = isOpen.value ? closeText : openText;
            _prompt.Show(text);
        }
    }

    public override void OnStopHover()
    {
        base.OnStopHover();

        if (_prompt == null)
            _prompt = PromptPresenter.Instance ?? FindObjectOfType<PromptPresenter>();

        if (_prompt != null)
        {
            _prompt.Hide();
        }
    }

    public override bool CanInteract()
    {
        // 나중에 "잠긴 문 / 전력 꺼짐 / 몬스터가 잡고 있는 문" 같은 조건 여기서 체크하면 됨
        return base.CanInteract() && gameObject.activeInHierarchy;
    }

    #endregion
}
