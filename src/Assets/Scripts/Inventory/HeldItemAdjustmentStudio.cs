using UnityEngine;

public enum HeldItemStudioView
{
    FirstPerson = 0,
    ThirdPerson = 1
}

public enum HeldItemStudioMotion
{
    Idle = 0,
    Walk = 1,
    Run = 2,
    Attack = 3
}

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class HeldItemAdjustmentStudio : MonoBehaviour
{
    public const int CurrentStudioLayoutVersion = 3;

    [SerializeField] private HeldItemVisualCatalog catalog;
    [SerializeField] private Transform heldItemAnchor;
    [SerializeField] private Transform previewRoot;
    [SerializeField] private string selectedItemName;
    [SerializeField] private Transform playerRoot;
    [SerializeField] private Camera firstPersonCamera;
    [SerializeField] private Camera thirdPersonCamera;
    [SerializeField] private AnimationClip locomotionPoseClip;
    [SerializeField] private AnimationClip gameplayPoseClip;
    [SerializeField] private AnimationClip walkClip;
    [SerializeField] private AnimationClip runClip;
    [SerializeField] private AnimationClip attackClip;
    [SerializeField] private HeldItemStudioView viewMode = HeldItemStudioView.FirstPerson;
    [SerializeField] private HeldItemStudioMotion motionMode = HeldItemStudioMotion.Idle;
    [SerializeField, Range(0f, 1f)] private float motionNormalizedTime;
    [SerializeField] private int studioLayoutVersion;

    public HeldItemVisualCatalog Catalog => catalog;
    public Transform HeldItemAnchor => heldItemAnchor;
    public Transform PreviewRoot => previewRoot;
    public string SelectedItemName => selectedItemName;
    public Transform PlayerRoot => playerRoot;
    public Camera FirstPersonCamera => firstPersonCamera;
    public Camera ThirdPersonCamera => thirdPersonCamera;
    public AnimationClip LocomotionPoseClip => locomotionPoseClip;
    public AnimationClip GameplayPoseClip => gameplayPoseClip;
    public AnimationClip WalkClip => walkClip;
    public AnimationClip RunClip => runClip;
    public AnimationClip AttackClip => attackClip;
    public HeldItemStudioView ViewMode => viewMode;
    public HeldItemStudioMotion MotionMode => motionMode;
    public float MotionNormalizedTime => motionNormalizedTime;
    public int StudioLayoutVersion => studioLayoutVersion;

    public void Configure(HeldItemVisualCatalog assignedCatalog, Transform assignedAnchor, string itemName)
    {
        catalog = assignedCatalog;
        heldItemAnchor = assignedAnchor;
        selectedItemName = itemName;
    }

    public void SetPreviewRoot(Transform assignedPreviewRoot)
    {
        previewRoot = assignedPreviewRoot;
    }

    public void SetPlayerPreview(Transform assignedPlayerRoot, Camera assignedFirstPerson, Camera assignedThirdPerson)
    {
        playerRoot = assignedPlayerRoot;
        firstPersonCamera = assignedFirstPerson;
        thirdPersonCamera = assignedThirdPerson;
    }

    public void SetPoseClips(AnimationClip locomotionClip, AnimationClip fistsClip,
        AnimationClip assignedWalkClip, AnimationClip assignedRunClip, AnimationClip assignedAttackClip)
    {
        locomotionPoseClip = locomotionClip;
        gameplayPoseClip = fistsClip;
        walkClip = assignedWalkClip;
        runClip = assignedRunClip;
        attackClip = assignedAttackClip;
    }

    public void SetViewMode(HeldItemStudioView mode)
    {
        viewMode = mode;
        ApplyCameraView();
    }

    public void SetMotionMode(HeldItemStudioMotion mode)
    {
        motionMode = mode;
    }

    public void SetMotionNormalizedTime(float normalizedTime)
    {
        motionNormalizedTime = Mathf.Repeat(normalizedTime, 1f);
    }

    public void SetStudioLayoutVersion(int version)
    {
        studioLayoutVersion = version;
    }

    private void OnEnable()
    {
        if (Application.isPlaying && playerRoot != null)
            DisablePreviewGameplay(playerRoot.gameObject);
    }

    public void ApplyCameraView()
    {
        bool firstPerson = viewMode == HeldItemStudioView.FirstPerson;
        if (firstPersonCamera != null)
        {
            firstPersonCamera.enabled = firstPerson;
            AudioListener firstPersonListener = firstPersonCamera.GetComponent<AudioListener>();
            if (firstPersonListener != null)
                firstPersonListener.enabled = firstPerson;
            firstPersonCamera.tag = firstPerson ? "MainCamera" : "Untagged";
        }

        if (thirdPersonCamera != null)
        {
            thirdPersonCamera.enabled = !firstPerson;
            AudioListener thirdPersonListener = thirdPersonCamera.GetComponent<AudioListener>();
            if (thirdPersonListener != null)
                thirdPersonListener.enabled = !firstPerson;
            if (!firstPerson)
                thirdPersonCamera.tag = "MainCamera";
            else if (thirdPersonCamera.CompareTag("MainCamera"))
                thirdPersonCamera.tag = "Untagged";
        }
    }

    public static void DisablePreviewGameplay(GameObject playerPreview)
    {
        if (playerPreview == null)
            return;

        foreach (Behaviour behaviour in playerPreview.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour == null)
                continue;
            if (behaviour is Camera || behaviour is Light || behaviour is AudioListener)
                continue;
            if (behaviour.GetType().Name == "UniversalAdditionalCameraData")
                continue;
            behaviour.enabled = false;
        }

        foreach (Rigidbody body in playerPreview.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        foreach (CharacterController controller in playerPreview.GetComponentsInChildren<CharacterController>(true))
            controller.enabled = false;
    }

    private void OnDrawGizmos()
    {
        if (heldItemAnchor == null)
            return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(heldItemAnchor.position, 0.015f);
        Gizmos.DrawLine(heldItemAnchor.position - heldItemAnchor.right * 0.05f,
            heldItemAnchor.position + heldItemAnchor.right * 0.05f);
        Gizmos.DrawLine(heldItemAnchor.position - heldItemAnchor.up * 0.05f,
            heldItemAnchor.position + heldItemAnchor.up * 0.05f);
        Gizmos.DrawLine(heldItemAnchor.position - heldItemAnchor.forward * 0.05f,
            heldItemAnchor.position + heldItemAnchor.forward * 0.05f);

        if (previewRoot == null || !HeldItemVisualPlacement.TryGetWorldRendererBounds(previewRoot, out Bounds bounds))
            return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(bounds.center, bounds.size);
        Gizmos.DrawLine(heldItemAnchor.position, bounds.center);
    }
}
