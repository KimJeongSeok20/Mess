using UnityEngine;
using PurrNet;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using Demo.Scripts.Runtime.Character;

/// <summary>
/// InventoryManager 확장 기능 - 인벤토리 슬롯 확장 관리
/// 기존 InventoryManager.cs에 추가하거나 별도 컴포넌트로 사용
/// </summary>
public class InventoryManagerExtensions : MonoBehaviour
{
    [Header("========== Inventory Expansion Settings ==========")]
    [SerializeField] private int actionSlotCount = 4;         // 고정된 ActionSlot 수 (앞쪽 자식들)
    [SerializeField] private int defaultCellSlots = 0;       // 기본 Cell 슬롯 수
    [SerializeField] private int absoluteMaxCells = 32;       // 최대 Cell 슬롯 수
    [SerializeField] private GameObject slotPrefab;           // Cell 슬롯 UI 프리팹
    [SerializeField] private Transform inventoryContainer;    // ActionSlot + Cell 들의 부모 Transform

    [Header("Expansion Visual/Audio")]
    [SerializeField] private AudioClip expansionSound;        // 확장 시 효과음
    [SerializeField] private GameObject expansionEffectPrefab;// 확장 시 UI 이펙트
    [SerializeField] private float slotAnimationDuration = 0.3f; // 슬롯 추가 애니메이션 시간

    [Header("UI References")]
    [SerializeField] private TMP_Text maxSlotsText;           // "16/32" 같은 표시 (Cell 기준)
    [SerializeField] private Image inventoryProgressBar;      // 인벤토리 사용률 바 (Cell 기준)

    [Header("Inventory Theme")]
    [SerializeField] private InventoryTheme theme;
    [SerializeField] private bool useTacticalProgressUi = true;
    [SerializeField] private TMP_Text tacticalProgressText;
    [SerializeField] private TMP_Text totalWeightText;
    [SerializeField] private Image _headerStrip;
    [SerializeField] private Image _headerDivider;

    [SerializeField] private InventoryManager inventoryManager;

    // 로컬 플레이어의 현재 Cell 슬롯 리스트 (ActionSlot 제외)
    private List<InventorySlot> inventorySlots = new List<InventorySlot>();
    private readonly List<InventorySlot> allVisualSlots = new();
    private readonly HashSet<InventorySlot> registeredSlots = new();

    // 현재 Cell 최대 개수
    private int currentCellSlots;

    // 오디오 소스 캐시
    private AudioSource audioSource;

    // 싱글톤 패턴 (옵션)
    public static InventoryManagerExtensions Instance { get; private set; }

    #region Initialization

    private void Awake()
    {
        // 싱글톤 설정
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (inventoryManager == null)
        {
            inventoryManager = FindObjectOfType<InventoryManager>();
        }

        // 오디오 소스 가져오기 또는 생성
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        // 저장된 인벤토리 크기 로드 (없으면 defaultCellSlots 사용)
        LoadInventorySize();

        if (currentCellSlots <= 0)
        {
            currentCellSlots = defaultCellSlots;
        }
    }

    private void Start()
    {
        // 로컬 플레이어 인벤토리 초기화
        InitializeLocalInventory();

        if (inventoryManager != null)
            inventoryManager.InventoryChanged += RefreshTacticalProgressUi;
    }

    private void OnDestroy()
    {
        if (inventoryManager != null)
            inventoryManager.InventoryChanged -= RefreshTacticalProgressUi;

        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// 로컬 플레이어 인벤토리 초기화
    /// </summary>
    private void InitializeLocalInventory()
    {
        // 확장 가능 전체 공간을 먼저 렌더링하고, 현재 해금 수만 활성화
        CreateInventorySlots(absoluteMaxCells);
        ApplySlotProgressionStates();

        // UI 텍스트/프로그레스 업데이트
        int totalSlots = actionSlotCount + currentCellSlots;
        UpdateSlotCountUI(totalSlots);
        RefreshTacticalProgressUi();
    }

    #endregion

    #region Public Methods - Expansion

    /// <summary>
    /// 플레이어의 인벤토리 확장 (Cell만 확장)
    /// </summary>
    /// <param name="additionalCells">추가할 Cell 수</param>
    public void ExpandLocalPlayerInventory(int additionalCells, bool showPrompt = true)
    {
        int newCellCount = Mathf.Min(currentCellSlots + additionalCells, absoluteMaxCells);

        // 실제 증가량
        int actualIncrease = newCellCount - currentCellSlots;

        if (actualIncrease > 0)
        {
            int oldCellCount = currentCellSlots;
            currentCellSlots = newCellCount;

            int totalSlots = actionSlotCount + currentCellSlots;

            Debug.Log($"[InventoryManagerExtensions] Cells expanded: {oldCellCount} → {currentCellSlots} (+{actualIncrease})");
            Debug.Log($"[InventoryManagerExtensions] Total slots: {totalSlots} ({actionSlotCount} action + {currentCellSlots} cells)");

            // UI & 슬롯 추가
            NotifyInventoryExpansion(totalSlots, actualIncrease, showPrompt);

            // 저장 (PlayerPrefs 또는 서버 동기화)
            SaveInventorySize();
        }
        else
        {
            Debug.Log($"[InventoryManagerExtensions] Already at max cells ({absoluteMaxCells})");

            // 최대치 도달 알림
            ShowMaxSlotsReached();
        }
    }

    /// <summary>
    /// 현재 총 슬롯 수(Action + Cell) 가져오기
    /// </summary>
    public int GetCurrentMaxSlots()
    {
        return actionSlotCount + currentCellSlots;
    }

    public int GetCurrentCellSlots()
    {
        return currentCellSlots;
    }

    /// <summary>Called with an empty inventory before restoring a morning checkpoint.</summary>
    public void RestoreCheckpointCellCount(int cells)
    {
        currentCellSlots = Mathf.Clamp(cells, 0, absoluteMaxCells);
        CreateInventorySlots(absoluteMaxCells);
        ApplySlotProgressionStates();
        UpdateSlotCountUI(actionSlotCount + currentCellSlots);
        RefreshTacticalProgressUi();
    }

    public int GetAbsoluteMaxCells()
    {
        return absoluteMaxCells;
    }

    /// <summary>
    /// 남은 Cell 확장 가능 수
    /// </summary>
    public int GetRemainingExpansionSlots()
    {
        return absoluteMaxCells - currentCellSlots;
    }

    /// <summary>
    /// Cell 확장 가능 여부 확인
    /// </summary>
    public bool CanExpandInventory(int additionalCells = 1)
    {
        return (currentCellSlots + additionalCells) <= absoluteMaxCells;
    }

    #endregion

    #region Notifications

    /// <summary>
    /// 인벤토리 확장 알림
    /// </summary>
    private void NotifyInventoryExpansion(int totalSlotsAfterExpansion, int addedCells, bool showPrompt)
    {
        // 효과음 재생
        PlayExpansionSound();

        ApplySlotProgressionStates(animateNewlyUnlocked: true, newlyUnlockedCount: addedCells);

        // UI 텍스트/프로그레스 업데이트
        UpdateSlotCountUI(totalSlotsAfterExpansion);
        RefreshTacticalProgressUi();

        // 확장 이펙트 재생
        PlayExpansionEffect();

        // 메시지 표시
        var promptPresenter = FindObjectOfType<PromptPresenter>();
        if (showPrompt && promptPresenter != null)
        {
            promptPresenter.Show($"Inventory expanded! (+{addedCells} cells)");
        }
    }

    /// <summary>
    /// 최대 Cell 슬롯 도달 알림
    /// </summary>
    private void ShowMaxSlotsReached()
    {
        var promptPresenter = FindObjectOfType<PromptPresenter>();
        if (promptPresenter != null)
        {
            promptPresenter.Show($"Maximum inventory size reached! ({absoluteMaxCells} cells)");
        }
    }

    #endregion

    #region UI Management

    /// <summary>
    /// 인벤토리 Cell 슬롯 생성 (전체 확장 공간 포함)
    /// </summary>
    private void CreateInventorySlots(int totalCellCount)
    {
        if (slotPrefab == null || inventoryContainer == null)
        {
            Debug.LogWarning("[InventoryManagerExtensions] Slot prefab or container not assigned!");
            return;
        }

        for (int i = inventoryContainer.childCount - 1; i >= 0; i--)
        {
            Transform child = inventoryContainer.GetChild(i);
            if (child.GetComponent<ActionSlot>() != null)
                continue;

            if (child.TryGetComponent(out InventorySlot slot))
            {
                if (inventoryManager != null)
                    inventoryManager.UnregisterSlot(slot);

                // Destroy()는 프레임 끝까지 지연되므로, 먼저 계층에서 떼어내야
                // 뒤이어 만들어지는 슬롯의 sibling index와 계층 스캔 결과가 어긋나지 않는다.
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        inventorySlots.Clear();
        allVisualSlots.Clear();
        registeredSlots.Clear();

        for (int i = 0; i < totalCellCount; i++)
        {
            GameObject slotObject = Instantiate(slotPrefab, inventoryContainer);
            slotObject.name = $"Cell ({i}) (Inventory Slot)";

            InventorySlot slot = slotObject.GetComponent<InventorySlot>();
            if (slot == null)
                continue;

            allVisualSlots.Add(slot);

            var visual = slot.GetComponent<InventorySlotVisual>();
            if (visual == null)
            {
                Debug.LogError($"[InventoryManagerExtensions] Authored InventorySlotVisual is missing on {slotObject.name}.", slotObject);
                continue;
            }

            visual.Initialize(slot.GetComponent<Image>(), false);

            if (i < currentCellSlots)
            {
                UnlockSlot(slot, registerToManager: true);
            }
            else
            {
                SetLockedSlot(slot);
            }
        }

        if (inventoryManager != null)
            inventoryManager.RefreshSlotRevealCache();

        Debug.Log($"[InventoryManagerExtensions] Prepared {totalCellCount} cell slots ({currentCellSlots} unlocked / {absoluteMaxCells} total)");
    }

    private void ApplySlotProgressionStates(bool animateNewlyUnlocked = false, int newlyUnlockedCount = 0)
    {
        int firstNewlyUnlocked = Mathf.Max(0, currentCellSlots - newlyUnlockedCount);

        for (int i = 0; i < allVisualSlots.Count; i++)
        {
            InventorySlot slot = allVisualSlots[i];
            if (slot == null)
                continue;

            if (i < currentCellSlots)
            {
                UnlockSlot(slot, registerToManager: true);

                if (animateNewlyUnlocked && i >= firstNewlyUnlocked)
                    PlayUnlockPulse(slot);
            }
            else
            {
                SetLockedSlot(slot);
            }
        }
    }

    private void UnlockSlot(InventorySlot slot, bool registerToManager)
    {
        slot.enabled = true;

        CanvasGroup canvasGroup = slot.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            Debug.LogError($"[InventoryManagerExtensions] Authored CanvasGroup is missing on {slot.name}.", slot);
            return;
        }

        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;

        var visual = slot.GetComponent<InventorySlotVisual>();
        if (visual != null)
        {
            visual.SetProgressionState(InventorySlotVisual.ProgressionState.Unlocked);
            visual.SetOccupied(!slot.isEmpty);
        }

        if (!inventorySlots.Contains(slot))
            inventorySlots.Add(slot);

        if (registerToManager && inventoryManager != null && !registeredSlots.Contains(slot))
        {
            inventoryManager.RegisterExtraSlot(slot);
            registeredSlots.Add(slot);
        }
    }

    private void SetLockedSlot(InventorySlot slot)
    {
        // 잠긴 슬롯도 포인터 이벤트를 받아 드롭 불가 상태를 전달해야 한다.
        // 실제 이동은 InventorySlot이 InventorySlotVisual.IsLocked를 확인해 거부한다.
        slot.enabled = true;
        slot.SetItem(null);

        CanvasGroup canvasGroup = slot.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            Debug.LogError($"[InventoryManagerExtensions] Authored CanvasGroup is missing on {slot.name}.", slot);
            return;
        }

        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;

        var visual = slot.GetComponent<InventorySlotVisual>();
        if (visual != null)
        {
            visual.SetProgressionState(InventorySlotVisual.ProgressionState.Locked);
        }

        inventorySlots.Remove(slot);
    }

    private void PlayUnlockPulse(InventorySlot slot)
    {
        if (slot == null)
            return;

        InventorySlotVisual visual = slot.GetComponent<InventorySlotVisual>();
        if (visual != null)
            visual.PlayUnlockPulse(Mathf.Max(0.16f, slotAnimationDuration));
    }


    /// <summary>
    /// 슬롯 카운트/프로그레스 UI 업데이트
    /// (표시는 Cell 기준: "사용 Cell / 최대 Cell")
    /// </summary>
    private void UpdateSlotCountUI(int totalSlots)
    {
        // 퀵슬롯은 항상 4칸으로 고정되어 있으므로, 헤더에는 성장 가능한 Cell만 표시한다.
        if (maxSlotsText != null)
        {
            maxSlotsText.text = $"{currentCellSlots} / {absoluteMaxCells}";
            if (theme != null)
                maxSlotsText.color = theme.textSecondary;
        }

        // 프로그레스 바 업데이트 (슬롯 해금 진행도 기준)
        if (inventoryProgressBar != null)
        {
            float fill = absoluteMaxCells > 0 ? (float)currentCellSlots / absoluteMaxCells : 0f;
            inventoryProgressBar.fillAmount = fill;
        }

        RefreshTacticalProgressUi();
    }

    /// <summary>
    /// 사용 중인 Cell 슬롯 수 계산
    /// </summary>
    private int CountUsedSlots()
    {
        int count = 0;
        foreach (var slot in inventorySlots)
        {
            if (slot != null && !slot.isEmpty)
                count++;
        }
        return count;
    }

    private void RefreshTacticalProgressUi()
    {
        if (totalWeightText != null && inventoryManager != null)
        {
            float totalWeight = inventoryManager.GetTotalWeight();
            totalWeightText.text =
                $"CARRY LOAD   {totalWeight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} KG";
            if (theme != null)
                totalWeightText.color = theme.textSecondary;
        }

        if (!useTacticalProgressUi)
            return;

        if (tacticalProgressText != null)
        {
            tacticalProgressText.text = $"CAPACITY   {currentCellSlots:00} / {absoluteMaxCells:00}";
            if (theme != null)
                tacticalProgressText.color = theme.textSecondary;
        }

        if (theme == null)
            return;

        if (_headerStrip != null)
            _headerStrip.color = theme.surface1;
        if (_headerDivider != null)
            _headerDivider.color = theme.borderSubtle;
    }

    #endregion

    #region Visual/Audio Effects

    /// <summary>
    /// 확장 효과음 재생
    /// </summary>
    private void PlayExpansionSound()
    {
        if (expansionSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(expansionSound);
        }
    }

    /// <summary>
    /// 확장 시각 효과 재생
    /// </summary>
    private void PlayExpansionEffect()
    {
        if (expansionEffectPrefab != null)
        {
            // UI 캔버스에 이펙트 생성
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                GameObject effect = Instantiate(expansionEffectPrefab, canvas.transform);

                // 중앙에 위치
                RectTransform rectTransform = effect.GetComponent<RectTransform>();
                if (rectTransform != null)
                {
                    rectTransform.anchoredPosition = Vector2.zero;
                }

                Destroy(effect, 2f);
            }
        }
    }

    #endregion

    #region Save/Load

    /// <summary>
    /// 인벤토리 Cell 크기 저장 (로컬 또는 서버)
    /// </summary>
    private void SaveInventorySize()
    {
        /*
        // PlayerPrefs에 저장 (로컬)
        PlayerPrefs.SetInt("InventoryMaxCells", currentCellSlots);
        PlayerPrefs.Save();

        // 또는 서버에 저장하는 로직 추가 가능*/
    }

    /// <summary>
    /// 인벤토리 Cell 크기 로드 (게임 시작 시)
    /// </summary>
    private void LoadInventorySize()
    {
        /*if (PlayerPrefs.HasKey("InventoryMaxCells"))
        {
            currentCellSlots = PlayerPrefs.GetInt("InventoryMaxCells", defaultCellSlots);
            currentCellSlots = Mathf.Clamp(currentCellSlots, defaultCellSlots, absoluteMaxCells);
        }
        else
        {
            currentCellSlots = defaultCellSlots;
        }*/
    }

    #endregion

    #region Debug Methods

    /// <summary>
    /// 디버그: 로컬 플레이어 인벤토리 Cell 확장
    /// </summary>
    [ContextMenu("Debug - Expand Local Inventory (+4 Cells)")]
    private void DebugExpandLocalInventory()
    {
        if (Application.isPlaying)
        {
            ExpandLocalPlayerInventory(4);
        }
    }

    /// <summary>
    /// 디버그: 현재 상태 출력
    /// </summary>
    [ContextMenu("Debug - Print Inventory Status")]
    private void DebugPrintStatus()
    {
        if (Application.isPlaying)
        {
            int totalSlots = GetCurrentMaxSlots();
            int usedCells = CountUsedSlots();
            int remainingCells = GetRemainingExpansionSlots();

            Debug.Log($"[InventoryManagerExtensions] Status:");
            Debug.Log($"  - Total Slots: {totalSlots} ({actionSlotCount} action + {currentCellSlots} cells)");
            Debug.Log($"  - Used Cells: {usedCells}/{currentCellSlots}");
            Debug.Log($"  - Remaining Cell Expansion: {remainingCells} cells");
        }
    }

    #endregion
}
