using System.Collections.Generic;
using UnityEngine;

/// <summary>Local hover selection consumed by the camera's outline render pass.</summary>
public static class InteractionHoverOutline
{
    private static readonly List<Renderer> TargetRenderers = new();
    private static InteractionManager _owner;
    private static AInteractable _target;
    private static Camera _camera;

    public static Camera TargetCamera => _owner != null && _owner.isActiveAndEnabled
        && _target != null && _target.isActiveAndEnabled ? _camera : null;

    public static IReadOnlyList<Renderer> Renderers => TargetRenderers;

    public static void SetTarget(InteractionManager owner, AInteractable target, Camera camera)
    {
        Reset();
        if (owner == null || target == null || camera == null)
            return;

        _owner = owner;
        _target = target;
        _camera = camera;
        target.HoverOutlineRoot.GetComponentsInChildren(true, TargetRenderers);
        for (int i = TargetRenderers.Count - 1; i >= 0; i--)
        {
            if (TargetRenderers[i] is not MeshRenderer && TargetRenderers[i] is not SkinnedMeshRenderer)
                TargetRenderers.RemoveAt(i);
        }
    }

    public static void Clear(InteractionManager owner)
    {
        if (_owner == owner)
            Reset();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        TargetRenderers.Clear();
        _owner = null;
        _target = null;
        _camera = null;
    }
}
