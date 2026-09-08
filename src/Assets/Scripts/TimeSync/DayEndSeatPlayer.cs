using System.Collections;
using System.Collections.Generic;
using Demo.Scripts.Runtime.Character;
using PurrNet;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(10000)]
public sealed class DayEndSeatPlayer : NetworkBehaviour
{
    private enum SeatState
    {
        Standing,
        AwaitingReservation,
        SittingDown,
        Seated,
        LyingDown,
        WakingToSit,
        StandingUp
    }

    private const int NoSeat = -1;

    [Header("Player References")]
    [SerializeField] private Animator animator;
    [SerializeField] private NetworkAnimator networkAnimator;
    [SerializeField] private FPSController fpsController;
    [SerializeField] private FPSMovement movement;
    [SerializeField] private PlayerInput playerInput;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private PlayerSound playerSound;

    [Header("Sleep Audio (Local Player)")]
    [SerializeField] private AudioSource sleepAudioSource;
    [SerializeField] private AudioClip sitSound;
    [SerializeField, Range(0f, 1f)] private float sitSoundVolume = 0.25f;
    [SerializeField] private AudioClip lieDownSound;
    [SerializeField, Range(0f, 1f)] private float lieDownSoundVolume = 0.3f;
    [SerializeField] private AudioClip wakeSound;
    [SerializeField, Range(0f, 1f)] private float wakeSoundVolume = 0.3f;

    [Header("Day End Animation")]
    [SerializeField] private AnimationClip standToSitClip;
    [SerializeField] private AnimationClip sitToStandClip;
    [SerializeField] private AnimationClip lyingDownClip;
    [SerializeField] private AnimationClip lyingToSitClip;
    [SerializeField] private string dayEndLayerName = "Day End Seat";
    [SerializeField] private string standToSitStateName = "Stand To Sit";
    [SerializeField] private string sitToStandStateName = "Sit To Stand";
    [SerializeField] private string lyingDownStateName = "Lying Down";
    [SerializeField] private string lyingToSitStateName = "Lying To Sit";
    [Tooltip("SK_CSF_M_BELTS bottom height above the player root at the final Stand To Sit pose.")]
    [SerializeField, Min(0f)] private float seatedBeltBottomOffsetFromRoot = 0.4191f;
    [SerializeField, Min(0f)] private float transitionDuration = 0.08f;
    [SerializeField, Min(0f)] private float lyingTransitionDuration = 0.18f;
    [SerializeField, Min(0f)] private float wakeSeatedHoldDuration = 0.25f;
    [SerializeField, Min(0f)] private float standReleaseDuration = 0.12f;

    // Lying To Sit and Sit To Stand come from different source clips, so their
    // seated pelvis poses are not pixel-identical. Keep the mattress contact for
    // the first part of standing, then release it while the body starts to rise.
    private const float StandContactReleaseNormalizedTime = 0.22f;

    private SeatState _state;
    private TimeManager _timeManager;
    private DayEndBedInteraction _bed;
    private Transform _seatPoint;
    private Coroutine _transitionRoutine;
    private int _currentSeatId = NoSeat;
    private int _dayEndLayer = -1;
    private bool _cancelPendingReservation;
    private bool _movementWasEnabled;
    private bool _playerInputWasEnabled;
    private bool _characterControllerWasEnabled;
    private bool _upperBodyPlayablesSuppressed;
    private bool _animatorCullingModeCaptured;
    private AnimatorCullingMode _animatorCullingModeBeforeDayEnd;
    private bool _followDayEndHeadCamera;
    private float _standingRootY;
    private Vector3 _seatedRootPosition;
    private Quaternion _seatedRootRotation;
    private Vector3 _dayEndCameraLocalPosition;
    private Quaternion _dayEndCameraLocalRotation;
    private SkinnedMeshRenderer _beltRenderer;
    private Mesh _beltBakeMesh;
    private readonly List<Vector3> _beltVertices = new();
    private Vector3 _seatedBeltAnchor;
    private Vector3 _lyingBeltAnchor;
    private Vector3 _activeBeltAnchor;
    private bool _hasSeatedBeltAnchor;
    private bool _hasLyingBeltAnchor;
    private float _standContactPinWeight;
    private bool _beltRendererLookupFailed;

    public int CurrentSeatId => _currentSeatId;
    public bool IsSeatedOrTransitioning => _state != SeatState.Standing;
    public float LyingDownDuration => lyingDownClip != null ? lyingDownClip.length : 0f;

    protected override void OnSpawned()
    {
        base.OnSpawned();

        if (!isOwner)
            return;

        TimeManager.OnLocalSeatReservationResult += HandleSeatReservationResult;
        TimeManager.OnAllPlayersSeated += HandleAllPlayersSeated;
        TimeManager.OnDayReset += HandleDayReset;
    }

    protected override void OnDespawned()
    {
        if (isOwner)
        {
            TimeManager.OnLocalSeatReservationResult -= HandleSeatReservationResult;
            TimeManager.OnAllPlayersSeated -= HandleAllPlayersSeated;
            TimeManager.OnDayReset -= HandleDayReset;
        }

        RestoreAnimatorCullingMode();
        StopFollowingDayEndHeadCamera();
        if (playerSound != null)
            playerSound.SetFootstepsSuppressed(false);

        ReleaseBeltBakeMesh();

        base.OnDespawned();
    }

    private void Update()
    {
        if (SkillWebTerminalInteraction.BlocksGameplayInput)
            return;

        if (!isOwner || _state != SeatState.Seated)
            return;

        if (_timeManager != null && _timeManager.IsForwardingTime())
            return;

        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            RequestStandUp();
    }

    private void LateUpdate()
    {
        if (animator != null)
        {
            if (_dayEndLayer < 0)
                _dayEndLayer = animator.GetLayerIndex(dayEndLayerName);

            if (_dayEndLayer >= 0 && animator.GetLayerWeight(_dayEndLayer) > 0f)
            {
                AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(_dayEndLayer);
                AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(_dayEndLayer);
                bool isDayEndPose = IsDayEndState(current) ||
                    (animator.IsInTransition(_dayEndLayer) && IsDayEndState(next));

                if (isDayEndPose)
                {
                    // KINEMATION's custom PlayableGraph advances the controller state time,
                    // but can leave the rendered skeleton on an older pose. A zero-delta
                    // evaluation here preserves the Animator's own cross-fade and current time
                    // while applying that pose after the custom graph has finished this frame.
                    animator.Update(0f);
                }
            }
        }

        // Pin the visible belt contact, not the player root. The lying clip moves
        // the pelvis inside the skeleton; holding only transform.position still
        // lets the body swing forward and float away from the mattress.
        if (_state == SeatState.LyingDown || _state == SeatState.WakingToSit)
        {
            PinBeltsToActiveAnchor(1f);
        }
        else if (_state == SeatState.StandingUp && _standContactPinWeight > 0f)
        {
            PinBeltsToActiveAnchor(_standContactPinWeight);
        }

        // The camera is a direct child of the animated head. The day-end full-body
        // layer can reset the Camera transform to its prefab defaults, which no
        // longer points along the helmet's normal view direction. Preserve the
        // pre-sit local face offset instead: the animated head still controls the
        // world position and rotation, while the camera keeps looking where the
        // helmet looks.
        if (_followDayEndHeadCamera && isOwner && playerCamera != null)
        {
            playerCamera.transform.localPosition = _dayEndCameraLocalPosition;
            playerCamera.transform.localRotation = _dayEndCameraLocalRotation;
        }

    }

    private bool IsDayEndState(AnimatorStateInfo state)
    {
        return state.IsName(standToSitStateName) ||
            state.IsName(sitToStandStateName) ||
            state.IsName(lyingDownStateName) ||
            state.IsName(lyingToSitStateName);
    }

    private void CaptureSeatedBeltAnchor()
    {
        if (animator != null)
            animator.Update(0f);

        _hasSeatedBeltAnchor = TryGetBeltAnchor(out _seatedBeltAnchor);
        _activeBeltAnchor = _seatedBeltAnchor;
        if (!_hasSeatedBeltAnchor)
            Debug.LogError("[DayEndSeatPlayer] Could not capture the seated SK_CSF_M_BELTS anchor.", this);
    }

    private void PinBeltsToActiveAnchor(float weight)
    {
        if (!_hasSeatedBeltAnchor ||
            !_hasLyingBeltAnchor ||
            weight <= 0f ||
            !TryGetBeltAnchor(out Vector3 currentAnchor))
            return;

        transform.position += (_activeBeltAnchor - currentAnchor) * Mathf.Clamp01(weight);
    }

    private bool TryGetBeltAnchor(out Vector3 anchor)
    {
        anchor = default;
        if (!TryResolveBeltRenderer())
            return false;

        _beltBakeMesh.Clear(false);
        _beltRenderer.BakeMesh(_beltBakeMesh);
        _beltVertices.Clear();
        _beltBakeMesh.GetVertices(_beltVertices);
        if (_beltVertices.Count == 0)
            return false;

        Transform beltTransform = _beltRenderer.transform;
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxZ = float.NegativeInfinity;

        for (int i = 0; i < _beltVertices.Count; i++)
        {
            Vector3 vertex = beltTransform.TransformPoint(_beltVertices[i]);
            minX = Mathf.Min(minX, vertex.x);
            minY = Mathf.Min(minY, vertex.y);
            minZ = Mathf.Min(minZ, vertex.z);
            maxX = Mathf.Max(maxX, vertex.x);
            maxZ = Mathf.Max(maxZ, vertex.z);
        }

        if (float.IsInfinity(minY) || float.IsNaN(minY))
            return false;

        anchor = new Vector3((minX + maxX) * 0.5f, minY, (minZ + maxZ) * 0.5f);
        return true;
    }

    private bool TryResolveBeltRenderer()
    {
        if (_beltRenderer != null && _beltRenderer.sharedMesh != null)
            return true;

        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i].name != "SK_CSF_M_BELTS" || renderers[i].sharedMesh == null)
                continue;

            _beltRenderer = renderers[i];
            _beltBakeMesh = new Mesh { name = "DayEnd_Belts_Baked" };
            _beltRendererLookupFailed = false;
            return true;
        }

        if (!_beltRendererLookupFailed)
        {
            _beltRendererLookupFailed = true;
            Debug.LogError("[DayEndSeatPlayer] SK_CSF_M_BELTS renderer is missing.", this);
        }

        return false;
    }

    private void ReleaseBeltBakeMesh()
    {
        if (_beltBakeMesh != null)
            Destroy(_beltBakeMesh);

        _beltBakeMesh = null;
        _beltRenderer = null;
        _beltVertices.Clear();
    }

    private bool TryGetDayEndStateNormalizedTime(string stateName, out float normalizedTime)
    {
        normalizedTime = 0f;
        if (animator == null || _dayEndLayer < 0)
            return false;

        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(_dayEndLayer);
        if (current.IsName(stateName))
        {
            normalizedTime = current.normalizedTime;
            return true;
        }

        if (!animator.IsInTransition(_dayEndLayer))
            return false;

        AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(_dayEndLayer);
        if (!next.IsName(stateName))
            return false;

        normalizedTime = next.normalizedTime;
        return true;
    }

    public void RequestSit(DayEndBedInteraction bed, TimeManager timeManager)
    {
        if (!isOwner || _state != SeatState.Standing || !timeManager.CanBeginDayEnd())
            return;

        _timeManager = timeManager;
        _bed = bed;
        _seatPoint = bed.SeatPoint;
        _currentSeatId = bed.SeatId;
        _cancelPendingReservation = false;
        _state = SeatState.AwaitingReservation;

        timeManager.RequestSeatReservationRpc(_currentSeatId);
    }

    public void RequestStandUp()
    {
        if (!isOwner || _state == SeatState.Standing || _state == SeatState.StandingUp)
            return;

        if (_state == SeatState.AwaitingReservation)
        {
            _cancelPendingReservation = true;
            return;
        }

        _timeManager.LeaveSeatRpc(_currentSeatId);
        BeginStandUp();
    }

    private void HandleSeatReservationResult(int seatId, bool accepted)
    {
        if (!isOwner || _state != SeatState.AwaitingReservation || seatId != _currentSeatId)
            return;

        if (!accepted)
        {
            ClearSeatState();
            return;
        }

        if (_cancelPendingReservation)
        {
            _timeManager.LeaveSeatRpc(_currentSeatId);
            ClearSeatState();
            return;
        }

        BeginSitDown();
    }

    private void BeginSitDown()
    {
        _dayEndLayer = animator.GetLayerIndex(dayEndLayerName);
        if (_dayEndLayer < 0)
        {
            Debug.LogError($"[DayEndSeatPlayer] Animator layer '{dayEndLayerName}' is missing.", this);
            _timeManager.LeaveSeatRpc(_currentSeatId);
            ClearSeatState();
            return;
        }

        if (_bed == null || !_bed.TryGetBedSurfaceY(out float bedSurfaceY))
        {
            Debug.LogError("[DayEndSeatPlayer] Bed surface is not configured.", this);
            _timeManager.LeaveSeatRpc(_currentSeatId);
            ClearSeatState();
            return;
        }

        _state = SeatState.SittingDown;
        BeginFollowingDayEndHeadCamera();
        ForceAnimatorAlwaysAnimate();
        SetLocalControlLocked(true);
        SetUpperBodyPlayablesSuppressed(true);

        // Preserve the player's actual grounded root height. Using ExitPoint.y here
        // made the character snap down before the animation and then rise again.
        _standingRootY = transform.position.y;
        Vector3 standingPosition = _seatPoint.position;
        standingPosition.y = _standingRootY;

        // The final Stand To Sit pose was sampled from SK_CSF_M_BELTS. Move in one
        // direction from the grounded root to the exact belt/mattress contact height.
        Vector3 seatedPosition = standingPosition;
        seatedPosition.y = bedSurfaceY - seatedBeltBottomOffsetFromRoot;
        _seatedRootPosition = seatedPosition;
        _seatedRootRotation = _seatPoint.rotation;
        transform.SetPositionAndRotation(standingPosition, _seatPoint.rotation);

        networkAnimator.SetLayerWeight(_dayEndLayer, 1f);
        networkAnimator.CrossFade(standToSitStateName, transitionDuration, _dayEndLayer, 0f);

        StartTransition(FinishSitDown(standToSitClip.length, standingPosition, seatedPosition));
    }

    private IEnumerator FinishSitDown(float duration, Vector3 standingPosition, Vector3 seatedPosition)
    {
        float elapsed = 0f;
        bool playedSitSound = false;
        float timeout = duration + Mathf.Max(0.5f, transitionDuration * 4f);
        while (elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            bool foundState = TryGetDayEndStateNormalizedTime(
                standToSitStateName,
                out float normalizedTime);
            float animationProgress = foundState
                ? Mathf.Clamp01(normalizedTime)
                : Mathf.Clamp01(elapsed / duration);
            transform.position = Vector3.Lerp(
                standingPosition,
                seatedPosition,
                Mathf.SmoothStep(0f, 1f, animationProgress));

            if (!playedSitSound && _state == SeatState.SittingDown && animationProgress >= 0.75f)
            {
                playedSitSound = true;
                PlaySleepSound(sitSound, sitSoundVolume);
            }

            if (foundState && normalizedTime >= 0.995f)
                break;

            yield return null;
        }

        transform.SetPositionAndRotation(_seatedRootPosition, _seatedRootRotation);
        CaptureSeatedBeltAnchor();
        _transitionRoutine = null;
        _state = SeatState.Seated;
        _timeManager.ConfirmSeatedRpc(_currentSeatId);
    }

    private void HandleAllPlayersSeated()
    {
        if (!isOwner || _state != SeatState.Seated)
            return;

        if (lyingDownClip == null || lyingToSitClip == null)
        {
            Debug.LogError("[DayEndSeatPlayer] Lying Down or Lying To Sit clip is missing.", this);
            return;
        }

        if (_bed == null ||
            !_bed.TryGetLyingPose(out Quaternion lyingTargetRotation, out Vector3 bedCenter) ||
            !_hasSeatedBeltAnchor)
        {
            Debug.LogError("[DayEndSeatPlayer] Bed lying pose is not configured.", this);
            return;
        }

        // Sitting stays at the authored edge. Only the lying contact moves to the
        // mattress center, using the same belt height that was already validated.
        _lyingBeltAnchor = bedCenter;
        _lyingBeltAnchor.y = _seatedBeltAnchor.y;
        _hasLyingBeltAnchor = true;
        Vector3 lyingStartAnchor = _activeBeltAnchor;

        _state = SeatState.LyingDown;
        PlaySleepSound(lieDownSound, lieDownSoundVolume);
        networkAnimator.SetLayerWeight(_dayEndLayer, 1f);
        networkAnimator.CrossFade(lyingDownStateName, lyingTransitionDuration, _dayEndLayer, 0f);

        Quaternion lyingStartRotation = transform.rotation;
        StartTransition(MoveIntoBedWhileLyingDown(
            lyingDownClip.length,
            lyingStartRotation,
            lyingTargetRotation,
            lyingStartAnchor,
            _lyingBeltAnchor));

        Debug.Log("[DayEndSeatPlayer] All players are seated - playing Lying Down.", this);
    }

    private IEnumerator MoveIntoBedWhileLyingDown(
        float duration,
        Quaternion startRotation,
        Quaternion targetRotation,
        Vector3 startAnchor,
        Vector3 targetAnchor)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float turnProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.05f, 0.58f, progress));
            float centerProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.08f, 0.82f, progress));

            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, turnProgress);
            _activeBeltAnchor = Vector3.Lerp(startAnchor, targetAnchor, centerProgress);
            yield return null;
        }

        transform.rotation = targetRotation;
        _activeBeltAnchor = targetAnchor;
        _transitionRoutine = null;
    }

    private void BeginWakeUpToSit()
    {
        if (!isOwner || _state != SeatState.LyingDown)
            return;

        if (lyingToSitClip == null || _seatPoint == null)
        {
            Debug.LogError("[DayEndSeatPlayer] Cannot wake through seated pose: reversed lying clip or SeatPoint is missing.", this);
            return;
        }

        StopTransition();
        _state = SeatState.WakingToSit;
        PlaySleepSound(wakeSound, wakeSoundVolume);
        networkAnimator.SetLayerWeight(_dayEndLayer, 1f);
        networkAnimator.CrossFade(lyingToSitStateName, lyingTransitionDuration, _dayEndLayer, 0f);

        Quaternion lyingRotation = transform.rotation;
        Vector3 wakeStartAnchor = _activeBeltAnchor;
        StartTransition(FinishWakeUpToSit(
            lyingToSitClip.length,
            lyingRotation,
            _seatedRootRotation,
            wakeStartAnchor,
            _seatedBeltAnchor));

        Debug.Log("[DayEndSeatPlayer] Day reset - waking from lying to seated pose.", this);
    }

    private IEnumerator FinishWakeUpToSit(
        float duration,
        Quaternion lyingRotation,
        Quaternion seatedRotation,
        Vector3 lyingAnchor,
        Vector3 seatedAnchor)
    {
        float elapsed = 0f;
        float timeout = duration + Mathf.Max(0.5f, lyingTransitionDuration * 4f);
        while (elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            bool foundState = TryGetDayEndStateNormalizedTime(
                lyingToSitStateName,
                out float normalizedTime);
            float animationProgress = foundState
                ? Mathf.Clamp01(normalizedTime)
                : Mathf.Clamp01(elapsed / duration);
            float turnProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.1f, 0.9f, animationProgress));
            float edgeProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.08f, 0.88f, animationProgress));

            transform.rotation = Quaternion.Slerp(lyingRotation, seatedRotation, turnProgress);
            _activeBeltAnchor = Vector3.Lerp(lyingAnchor, seatedAnchor, edgeProgress);

            if (foundState && normalizedTime >= 0.995f)
                break;

            yield return null;
        }

        _activeBeltAnchor = seatedAnchor;
        transform.SetPositionAndRotation(_seatedRootPosition, seatedRotation);

        float holdElapsed = 0f;
        while (holdElapsed < wakeSeatedHoldDuration)
        {
            holdElapsed += Time.deltaTime;
            transform.rotation = seatedRotation;
            yield return null;
        }

        _transitionRoutine = null;
        BeginStandUp();
    }

    private void BeginStandUp()
    {
        StopTransition();
        _state = SeatState.StandingUp;
        transform.SetPositionAndRotation(_seatedRootPosition, _seatedRootRotation);
        _activeBeltAnchor = _seatedBeltAnchor;
        _standContactPinWeight = _hasLyingBeltAnchor ? 1f : 0f;

        networkAnimator.SetLayerWeight(_dayEndLayer, 1f);
        // Sit To Stand is an actual time-reversed copy of Stand To Sit. KINEMATION's
        // custom graph advances controller time forward even when a state uses speed -1,
        // so a reversed clip is required for the rendered skeleton to reliably stand up.
        networkAnimator.CrossFade(sitToStandStateName, transitionDuration, _dayEndLayer, 0f);

        StartTransition(FinishStandUp(sitToStandClip.length));
    }

    private IEnumerator FinishStandUp(float duration)
    {
        float elapsed = 0f;
        float timeout = duration + Mathf.Max(0.5f, transitionDuration * 4f);
        while (elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            bool foundState = TryGetDayEndStateNormalizedTime(
                sitToStandStateName,
                out float normalizedTime);
            float animationProgress = foundState
                ? Mathf.Clamp01(normalizedTime)
                : Mathf.Clamp01(elapsed / duration);
            float contactReleaseProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    0.02f,
                    StandContactReleaseNormalizedTime,
                    animationProgress));
            _standContactPinWeight = 1f - contactReleaseProgress;
            // Keep the same seated root for the whole reverse animation. Moving the
            // root toward ground here made the body dip below the mattress before
            // the animation had finished standing up.
            transform.SetPositionAndRotation(_seatedRootPosition, _seatedRootRotation);

            if (foundState && normalizedTime >= 0.995f)
                break;

            yield return null;
        }

        _standContactPinWeight = 0f;

        Vector3 standingPosition = _seatedRootPosition;
        standingPosition.y = _standingRootY;

        // Release the full-body pose over several frames at the grounded root.
        // A one-frame layer 1 -> 0 switch evaluated the base standing pose while
        // the root was still at seated height, producing a brief upward pop.
        float releaseElapsed = 0f;
        float releaseDuration = Mathf.Max(0.01f, standReleaseDuration);
        float releaseStartWeight = animator.GetLayerWeight(_dayEndLayer);
        while (releaseElapsed < releaseDuration)
        {
            releaseElapsed += Time.deltaTime;
            float releaseProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(releaseElapsed / releaseDuration));
            transform.SetPositionAndRotation(standingPosition, _seatedRootRotation);
            networkAnimator.SetLayerWeight(
                _dayEndLayer,
                Mathf.Lerp(releaseStartWeight, 0f, releaseProgress));
            yield return null;
        }

        _transitionRoutine = null;
        transform.SetPositionAndRotation(standingPosition, _seatedRootRotation);
        networkAnimator.SetLayerWeight(_dayEndLayer, 0f);
        animator.Update(0f);
        SetUpperBodyPlayablesSuppressed(false);
        RestoreAnimatorCullingMode();
        SetLocalControlLocked(false);
        StopFollowingDayEndHeadCamera();
        ClearSeatState();
    }

    private void HandleDayReset()
    {
        if (!isOwner ||
            _state == SeatState.Standing ||
            _state == SeatState.WakingToSit ||
            _state == SeatState.StandingUp)
            return;

        if (_state == SeatState.LyingDown)
        {
            BeginWakeUpToSit();
            return;
        }

        BeginStandUp();
    }

    private void StartTransition(IEnumerator routine)
    {
        StopTransition();
        _transitionRoutine = StartCoroutine(routine);
    }

    private void PlaySleepSound(AudioClip clip, float volume)
    {
        if (!isOwner || sleepAudioSource == null || clip == null)
            return;

        sleepAudioSource.spatialBlend = 0f;
        sleepAudioSource.Stop();
        sleepAudioSource.PlayOneShot(clip, volume);
    }

    private void StopTransition()
    {
        if (_transitionRoutine == null)
            return;

        StopCoroutine(_transitionRoutine);
        _transitionRoutine = null;
    }

    private void SetLocalControlLocked(bool locked)
    {
        if (locked)
        {
            _movementWasEnabled = movement.enabled;
            _playerInputWasEnabled = playerInput.enabled;
            _characterControllerWasEnabled = characterController.enabled;
            movement.enabled = false;
            playerInput.enabled = false;
            characterController.enabled = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            return;
        }

        movement.enabled = _movementWasEnabled;
        playerInput.enabled = _playerInputWasEnabled;
        characterController.enabled = _characterControllerWasEnabled;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void SetUpperBodyPlayablesSuppressed(bool suppressed)
    {
        if (_upperBodyPlayablesSuppressed == suppressed)
            return;

        _upperBodyPlayablesSuppressed = suppressed;
        fpsController.SetDayEndFullBodyOverride(suppressed);
        if (playerSound != null)
            playerSound.SetFootstepsSuppressed(suppressed);
        SetDayEndFullBodyOverrideRpc(suppressed);
    }

    private void ForceAnimatorAlwaysAnimate()
    {
        if (!_animatorCullingModeCaptured)
        {
            _animatorCullingModeBeforeDayEnd = animator.cullingMode;
            _animatorCullingModeCaptured = true;
        }

        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    private void RestoreAnimatorCullingMode()
    {
        if (!_animatorCullingModeCaptured || animator == null)
            return;

        animator.cullingMode = _animatorCullingModeBeforeDayEnd;
        _animatorCullingModeCaptured = false;
    }

    private void BeginFollowingDayEndHeadCamera()
    {
        if (!isOwner || playerCamera == null)
            return;

        _dayEndCameraLocalPosition = playerCamera.transform.localPosition;
        _dayEndCameraLocalRotation = playerCamera.transform.localRotation;
        _followDayEndHeadCamera = true;
    }

    private void StopFollowingDayEndHeadCamera()
    {
        _followDayEndHeadCamera = false;
    }

    [ServerRpc]
    private void SetDayEndFullBodyOverrideRpc(bool active)
    {
        SetDayEndFullBodyOverrideObserversRpc(active);
    }

    [ObserversRpc]
    private void SetDayEndFullBodyOverrideObserversRpc(bool active)
    {
        fpsController.SetDayEndFullBodyOverride(active);
        if (playerSound != null)
            playerSound.SetFootstepsSuppressed(active);

        if (active)
            ForceAnimatorAlwaysAnimate();
        else
            RestoreAnimatorCullingMode();
    }

    private void ClearSeatState()
    {
        StopFollowingDayEndHeadCamera();
        _state = SeatState.Standing;
        _timeManager = null;
        _bed = null;
        _seatPoint = null;
        _currentSeatId = NoSeat;
        _cancelPendingReservation = false;
        _standingRootY = 0f;
        _seatedRootPosition = default;
        _seatedRootRotation = default;
        _seatedBeltAnchor = default;
        _lyingBeltAnchor = default;
        _activeBeltAnchor = default;
        _hasSeatedBeltAnchor = false;
        _hasLyingBeltAnchor = false;
        _standContactPinWeight = 0f;
    }
}
