using TMPro;
using UnityEngine;

public sealed class DayEndBedInteraction : AInteractable
{
    [Header("Seat")]
    [SerializeField] private int seatId;
    [SerializeField] private MeshCollider bedCollider;
    [SerializeField] private Transform seatPoint;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private TimeManager timeManager;

    [Header("Prompt")]
    [SerializeField] private PromptPresenter promptPresenter;
    [SerializeField] private TextMeshProUGUI voteText;

    private bool _isHovered;

    public int SeatId => seatId;
    public Transform SeatPoint => seatPoint;
    public Transform ExitPoint => exitPoint;

    public bool TryGetBedSurfaceY(out float surfaceY)
    {
        if (bedCollider == null)
        {
            surfaceY = 0f;
            return false;
        }

        surfaceY = bedCollider.bounds.max.y;
        return true;
    }

    public bool TryGetLyingPose(out Quaternion rotation, out Vector3 center)
    {
        rotation = default;
        center = default;

        if (bedCollider == null || bedCollider.sharedMesh == null || seatPoint == null)
            return false;

        Bounds meshBounds = bedCollider.sharedMesh.bounds;
        center = bedCollider.transform.TransformPoint(meshBounds.center);
        Vector3 localLongAxis = meshBounds.size.z >= meshBounds.size.x
            ? Vector3.forward
            : Vector3.right;
        Vector3 worldLongAxis = bedCollider.transform.TransformDirection(localLongAxis).normalized;

        // Both directions follow the same bed axis. Choose the one that makes the
        // seated player turn right by roughly 90 degrees instead of spinning around.
        if (Vector3.Dot(worldLongAxis, seatPoint.right) < 0f)
            worldLongAxis = -worldLongAxis;

        rotation = Quaternion.LookRotation(worldLongAxis, Vector3.up);
        return true;
    }

    private void OnEnable()
    {
        TimeManager.OnSleepVoteChanged += HandleSeatCountChanged;
        TimeManager.OnDayReset += HandleDayReset;
    }

    private void OnDisable()
    {
        TimeManager.OnSleepVoteChanged -= HandleSeatCountChanged;
        TimeManager.OnDayReset -= HandleDayReset;
    }

    public override void Interact(InteractionManager interactor)
    {
        DayEndSeatPlayer player = interactor.CurrentCamera.GetComponentInParent<DayEndSeatPlayer>();
        if (player == null)
        {
            Debug.LogError("[DayEndBedInteraction] Interacting player has no DayEndSeatPlayer component.", this);
            return;
        }

        if (!timeManager.CanBeginDayEnd())
        {
            RefreshPrompt();
            return;
        }

        if (player.IsSeatedOrTransitioning)
            return;

        player.RequestSit(this, timeManager);
        RefreshPrompt();
    }

    public override void OnHover()
    {
        _isHovered = true;
        RefreshPrompt();
    }

    public override void OnStopHover()
    {
        _isHovered = false;
        promptPresenter.Hide();
    }

    private void HandleSeatCountChanged(int current, int total)
    {
        if (voteText != null)
            voteText.text = $"Seated\n{current} / {total}";

        if (_isHovered)
            RefreshPrompt();
    }

    private void HandleDayReset()
    {
        if (_isHovered)
            RefreshPrompt();
    }

    private void RefreshPrompt()
    {
        if (timeManager.IsForwardingTime())
        {
            promptPresenter.Show("Day is changing...");
            return;
        }

        if (!timeManager.CanBeginDayEnd())
        {
            promptPresenter.Show($"Can only end the day after {timeManager.DayEndAvailableHour:F0}:00");
            return;
        }

        (int current, int total) = timeManager.GetSleepVoteStatus();
        promptPresenter.Show($"[F] Sit on bed to end day ({current}/{total} seated)");
    }

}
