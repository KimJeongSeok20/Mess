using UnityEngine;
using PurrNet;
using UnityEngine.UI;

/// <summary>
/// 시계 상호작용 - 길게 눌러서 시간 흐름 시작 (2초 홀드)
/// </summary>
public class ClockInteraction : AInteractable
{
    [Header("Clock Settings")]
    [SerializeField] private TimeManager timeManager;
    [SerializeField] private float holdDuration = 2f; // 2초 홀드 필요

    [Header("UI")]
    [SerializeField] private GameObject holdUIPanel; // 프로그레스 바 패널
    [SerializeField] private Image holdProgressBar; // 프로그레스 바 Image (Fill Amount)

    // 홀드 상태
    private bool _isHolding = false;
    private float _holdTimer = 0f;

    // 캐시된 컴포넌트들
    private PromptPresenter _promptPresenter;

    private void Awake()
    {
        // TimeManager 자동 찾기
        if (timeManager == null)
        {
            timeManager = FindObjectOfType<TimeManager>();

            if (timeManager == null)
            {
                Debug.LogError("[ClockInteraction] TimeManager not found!");
            }
        }

        // UI 초기화
        if (holdUIPanel != null)
            holdUIPanel.SetActive(false);

        if (holdProgressBar != null)
            holdProgressBar.fillAmount = 0f;

        // PromptPresenter 찾기
        if (_promptPresenter == null)
            _promptPresenter = FindObjectOfType<PromptPresenter>();
    }

    private void Update()
    {
        // 홀드 중일 때만 타이머 업데이트
        if (_isHolding)
        {
            _holdTimer += Time.deltaTime;

            // 프로그레스 바 업데이트
            if (holdProgressBar != null)
            {
                holdProgressBar.fillAmount = Mathf.Clamp01(_holdTimer / holdDuration);
            }

            // 프롬프트에 카운트다운 표시
            UpdateHoldPrompt();

            // 홀드 완료
            if (_holdTimer >= holdDuration)
            {
                CompleteHold();
            }
        }
    }

    public override void Interact()
    {
        if (timeManager == null)
        {
            Debug.LogError("[ClockInteraction] TimeManager is null!");
            return;
        }

        //Host(서버)가 아닌 플레이어는 시계를 시작할 수 없음
        if (!timeManager.isServer)
        {
            Debug.Log("[ClockInteraction] Only the host can start the clock.");
            return;
        }

        // 이미 시계가 작동 중이면 무시
        if (timeManager.IsClockRunning())
        {
            Debug.Log("[ClockInteraction] Clock is already running!");
            return;
        }

        // 홀드 시작
        StartHold();
    }

    private void StartHold()
    {
        _isHolding = true;
        _holdTimer = 0f;

        // UI 표시
        if (holdUIPanel != null)
            holdUIPanel.SetActive(true);

        Debug.Log("[ClockInteraction] Hold started...");
    }

    private void CompleteHold()
    {
        _isHolding = false;

        // UI 숨김
        if (holdUIPanel != null)
            holdUIPanel.SetActive(false);

        // 시간 흐름 시작
        Debug.Log("[ClockInteraction] Hold completed - starting clock");
        timeManager.StartTimeClockRpc();

        // 프롬프트를 "Clock is already running."으로 업데이트
        if (_promptPresenter != null)
        {
            _promptPresenter.Show("Clock is already running.");
        }
    }


    private void CancelHold()
    {
        _isHolding = false;
        _holdTimer = 0f;

        // UI 숨김
        if (holdUIPanel != null)
            holdUIPanel.SetActive(false);

        if (holdProgressBar != null)
            holdProgressBar.fillAmount = 0f;

        Debug.Log("[ClockInteraction] Hold cancelled");
    }

    public override void OnHover()
    {
        base.OnHover();

        if (_promptPresenter != null)
        {
            if (timeManager == null)
            {
                _promptPresenter.Show("Clock is unavailable.");
            }
            // host가 아닌 경우
            else if (!timeManager.isServer)
            {
                _promptPresenter.Show("Only the host can start the clock.");
            }
            // 시계 이미 작동 중
            else if (timeManager.IsClockRunning())
            {
                _promptPresenter.Show("Clock is already running.");
            }
            //  host + 아직 멈춰 있는 상태
            else
            {
                _promptPresenter.Show("[Hold F] Start the clock");
            }
        }
    }


    public override void OnStopHover()
    {
        base.OnStopHover();

        // 홀드 취소
        if (_isHolding)
        {
            CancelHold();
        }

        // 프롬프트 숨김
        if (_promptPresenter != null)
            _promptPresenter.Hide();
    }

    private void UpdateHoldPrompt()
    {
        if (_promptPresenter == null || timeManager == null)
            return;

        // 이미 시계가 돌아가는 중이면
        if (timeManager.IsClockRunning())
        {
            _promptPresenter.Show("Clock is already running.");
            return;
        }

        // 남은 시간 계산
        float remaining = Mathf.Max(0f, holdDuration - _holdTimer);
        int secondsLeft = Mathf.CeilToInt(remaining);

        // 0은 보여주지 않고 최소 1로 고정
        if (secondsLeft < 1)
            secondsLeft = 1;

        // 예: [Hold F] Start the clock .. 2 / .. 1
        _promptPresenter.Show($"[Hold F] Start the clock .. {secondsLeft}");
    }

    public override bool CanInteract()
    {
        // Hover 효과는 항상 표시
        return true;
    }
}