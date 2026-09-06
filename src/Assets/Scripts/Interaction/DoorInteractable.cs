using System;
using PurrNet;
using UnityEngine;
using UnityEngine.AI;

public class DoorInteractable : AInteractable
{
    [Header("Animator")]
    [SerializeField] private Animator anim;
    [SerializeField] private string openBool = "Open";
    [SerializeField] private bool startsOpen = false;
    [SerializeField, Tooltip("Network fallback: if the bool parameter is missing, trigger local OpenStart playback.")]
    private bool forceLocalAnimation = false;

    [Header("Prompt")]
    [SerializeField] private string openText = "Open [E]";
    [SerializeField] private string closeText = "Close [E]";

    [Header("Navigation")]
    [SerializeField] private NavMeshObstacle obstacle;
    [SerializeField] private Collider solidCollider;
    [SerializeField] private bool makeTriggerWhenOpen;

    private SyncVar<bool> isOpen = new(false);

    public bool IsOpen => isOpen.value;
    public bool HasNavigationObstacle => obstacle != null;
    public bool IsNavigationObstacleEnabled => obstacle != null && obstacle.enabled;
    public bool IsNavigationObstacleCarving => obstacle != null && obstacle.carving;
    public string GetPrompt() => isOpen.value ? closeText : openText;

    protected override void OnSpawned()
    {
        base.OnSpawned();
        isOpen.onChanged += OnOpenChanged;

        if (isServer)
            isOpen.value = startsOpen;

        ApplyState(isOpen.value);
    }

    protected override void OnDestroy()
    {
        isOpen.onChanged -= OnOpenChanged;
        base.OnDestroy();
    }

    private void OnOpenChanged(bool open)
    {
        ApplyState(open);
    }

    public override void Interact()
    {
        if (!isServer)
        {
            SV_Toggle();
            return;
        }

        SetServer(!isOpen.value);
    }

    [ServerRpc(requireOwnership: false)]
    private void SV_Toggle()
    {
        SetServer(!isOpen.value);
    }

    [ServerRpc(requireOwnership: false)]
    public void SV_RequestOpen()
    {
        if (isOpen.value)
            return;

        SetServer(true);
    }

    private void SetServer(bool open)
    {
        if (!isServer)
            return;

        isOpen.value = open;
        OB_Apply(open);
    }

    [ObserversRpc]
    private void OB_Apply(bool open)
    {
        ApplyState(open);
    }

    private void ApplyState(bool open)
    {
        if (anim)
        {
            if (HasParameter(anim, openBool))
                anim.SetBool(openBool, open);
            else if (forceLocalAnimation && open && HasParameter(anim, "OpenStart"))
                anim.SetTrigger("OpenStart");
        }

        ApplyNavigationObstacleState(open);

        if (solidCollider != null && makeTriggerWhenOpen)
            solidCollider.isTrigger = open;
    }

    public void ApplyNavigationObstacleState()
    {
        ApplyNavigationObstacleState(isOpen.value);
    }

    public void SetNavigationObstacleBlockedForValidation(bool blocked)
    {
        if (obstacle == null)
            return;

        obstacle.carving = true;
        obstacle.enabled = blocked;
    }

    private void ApplyNavigationObstacleState(bool open)
    {
        if (obstacle == null)
            return;

        obstacle.carving = true;
        obstacle.enabled = !open;
    }

    private static bool HasParameter(Animator targetAnimator, string parameterName)
    {
        if (!targetAnimator || targetAnimator.runtimeAnimatorController == null)
            return false;

        AnimatorControllerParameter[] parameters = targetAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == parameterName)
                return true;
        }

        return false;
    }
}
