using UnityEngine;
using PurrNet;
using UnityEngine.InputSystem;

public class InteractionManager : MonoBehaviour
{
    [SerializeField] private LayerMask interactableLayer;
    [SerializeField] private float interactDistance = 4f;
    [SerializeField] private float aimAssistRadius = 0.45f;

    private Camera _cam;
    public Camera CurrentCamera => _cam;
    private AInteractable _currentHoveredInteractable;

    private void OnEnable()
    {
        // 이벤트 구독 시작
        NetworkPlayer.OnLocalPlayerSpawned += HandleLocalPlayerSpawned;
        NetworkPlayer.OnLocalPlayerDespawned += HandleLocalPlayerDespawned;
    }

    private void OnDisable()
    {
        ClearHover();
        // 이벤트 구독 해제 (메모리 누수 방지)
        NetworkPlayer.OnLocalPlayerSpawned -= HandleLocalPlayerSpawned;
        NetworkPlayer.OnLocalPlayerDespawned -= HandleLocalPlayerDespawned;
    }

    private void HandleLocalPlayerSpawned(Camera cam)
    {
        ClearHover();
        _localVitals = null;
        _cam = cam;
    }

    private void HandleLocalPlayerDespawned()
    {
        ClearHover();
        _localVitals = null;
        _cam = null;
    }

    private void Update()
    {
        if (GameMenuController.IsOpen || SkillWebTerminalInteraction.BlocksGameplayInput)
        {
            ClearHover();
            return;
        }
        HandleHovers();
    }

    public void OnGrabItem(InputValue value)  // F키 입력
    {
        if (!value.isPressed || GameMenuController.IsOpen || SkillWebTerminalInteraction.BlocksGameplayInput) return;

        if (_cam == null)
        {
            Debug.LogWarning("카메라가 설정되지 않았습니다.");
            return;
        }

        if (!TryGetFocusedInteractable(out var interactable))
            return;

        if (IsLocalPlayerDead() && !interactable.AllowInteractionWhileDead)
        {
            PromptPresenter.ShowPrompt("You are dead. Find the revive station.");
            return;
        }

        if (interactable.CanInteract())
            interactable.Interact(this);
    }

    private PlayerVitals _localVitals;

    private bool IsLocalPlayerDead()
    {
        if (_localVitals == null && _cam != null)
            _localVitals = _cam.transform.root.GetComponent<PlayerVitals>();

        return _localVitals != null && _localVitals.IsDead;
    }

    private void HandleHovers()
    {
        if (_cam == null)
        {
            ClearHover();
            return;
        }

        if (!TryGetFocusedInteractable(out var interactable))
        {
            ClearHover();
            return;
        }

        if (_currentHoveredInteractable == interactable)
            return;

        ClearHover();
        _currentHoveredInteractable = interactable;

        if (_currentHoveredInteractable.CanHover())
        {
            InteractionHoverOutline.SetTarget(this, _currentHoveredInteractable, _cam);
            _currentHoveredInteractable.OnHover();
        }
    }

    private bool TryGetFocusedInteractable(out AInteractable interactable)
    {
        interactable = null;

        if (_cam == null)
            return false;

        Vector3 origin = _cam.transform.position;
        Vector3 direction = _cam.transform.forward;

        if (TryResolveInteractable(origin, direction, 0f, itemsOnly: false, out interactable))
            return true;

        return aimAssistRadius > 0f && TryResolveInteractable(origin, direction, aimAssistRadius, itemsOnly: true, out interactable);
    }

    private bool TryResolveInteractable(Vector3 origin, Vector3 direction, float sphereRadius, bool itemsOnly, out AInteractable interactable)
    {
        interactable = null;

        RaycastHit[] hits = sphereRadius > 0f
            ? Physics.SphereCastAll(origin, sphereRadius, direction, interactDistance, interactableLayer, QueryTriggerInteraction.Ignore)
            : Physics.RaycastAll(origin, direction, interactDistance, interactableLayer, QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return false;

        System.Array.Sort(hits, static (a, b) => a.distance.CompareTo(b.distance));

        for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
        {
            var hit = hits[hitIndex];
            if (itemsOnly && LayerMask.LayerToName(hit.collider.gameObject.layer) != "Item")
                continue;

            var interactables = hit.collider.GetComponentsInParent<AInteractable>();
            if (interactables == null || interactables.Length == 0)
                continue;

            // 같은 오브젝트에 여러 AInteractable이 붙어있을 수 있으므로(예: 던전 출구 문 = Door + DungeonExit)
            // 컴포넌트 순서가 아니라 InteractPriority가 가장 높은 것을 고른다. 동점이면 계층상 가까운 쪽.
            AInteractable best = null;
            int bestPriority = int.MinValue;

            for (int i = 0; i < interactables.Length; i++)
            {
                AInteractable candidate = interactables[i];
                if (candidate == null || !candidate.isActiveAndEnabled || !candidate.CanHover())
                    continue;

                if (candidate.InteractPriority > bestPriority)
                {
                    best = candidate;
                    bestPriority = candidate.InteractPriority;
                }
            }

            if (best != null)
            {
                interactable = best;
                return true;
            }
        }

        return false;
    }

    private void ClearHover()
    {
        InteractionHoverOutline.Clear(this);
        var previous = _currentHoveredInteractable;
        _currentHoveredInteractable = null;
        if (previous != null)
            previous.OnStopHover();
    }
}

public abstract class AInteractable : NetworkBehaviour
{
    [SerializeField, Tooltip("테두리를 그릴 모델 루트. 비어 있으면 이 오브젝트의 하위 모델을 사용합니다.")]
    private Transform hoverOutlineRoot;

    public Transform HoverOutlineRoot
    {
        get => hoverOutlineRoot != null ? hoverOutlineRoot : transform;
        set => hoverOutlineRoot = value;
    }

    /// <summary>
    /// 한 오브젝트에 AInteractable이 여러 개 붙어있을 때 어느 쪽이 F 입력을 가져갈지 결정한다.
    /// 값이 클수록 우선. 기본 상호작용은 0.
    /// </summary>
    public virtual int InteractPriority => 0;

    /// <summary>Dead (ghost) players can only use interactables that opt in, e.g. the revive station.</summary>
    public virtual bool AllowInteractionWhileDead => false;

    public virtual void Interact(InteractionManager interactor)//플레이어 확인
    {
        Interact();
    }

    public virtual void Interact()
    {
    }

    public virtual void OnHover() { }
    public virtual void OnStopHover() { }

    public virtual bool CanHover()
    {
        return true;
    }

    public virtual bool CanInteract()
    {
        return true;
    }
}
