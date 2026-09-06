using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Demo.Scripts.Runtime.Character;
using Demo.Scripts.Runtime.Item;
using PurrNet;

/// <summary>
/// Canvas UI에 탄약 정보를 표시하는 컴포넌트
/// - FPSWeaponManager의 활성 무기 추적
/// - Weapon일 때만 탄약 UI 표시
/// - Fist 상태에서는 UI 숨김
/// </summary>
public class WeaponUI : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private string ammoFormat = "{0} / {1}";

    [Header("AAA Ammo HUD")]
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private TMP_Text currentAmmoText;
    [SerializeField] private TMP_Text reserveAmmoText;
    [SerializeField] private TMP_Text fireModeText;
    [SerializeField] private string fireModeLabel = "SEMI";

    // 동적으로 탐지되는 참조들
    private FPSWeaponManager weaponManager;
    private Component _textComponent; // Text 또는 TextMeshProUGUI
    private Weapon _currentWeapon;
    private InventoryManager _inventoryManager;

    private int _lastCurrentAmmo = -1;
    private int _lastReserveAmmo = -1;

    private bool HasStructuredHud => currentAmmoText != null && reserveAmmoText != null;

    private void Awake()
    {
        ResolveHudReferences();
        HideAmmoUI();
    }

    private void Start()
    {
        ResolveHudReferences();

        if (!HasStructuredHud && _textComponent == null)
        {
            Debug.LogError("[WeaponUI] 탄약 HUD 텍스트 참조를 찾지 못했습니다.");
            enabled = false;
            return;
        }

        // 초기 상태: UI 숨김
        HideAmmoUI();
    }

    private void ResolveHudReferences()
    {
        if (visualRoot == null)
        {
            Transform child = transform.Find("AmmoHudVisual");
            if (child != null)
                visualRoot = child.gameObject;
        }

        if (visualRoot != null)
        {
            if (currentAmmoText == null)
                currentAmmoText = visualRoot.transform.Find("CurrentAmmoText")?.GetComponent<TMP_Text>();
            if (reserveAmmoText == null)
                reserveAmmoText = visualRoot.transform.Find("ReserveAmmoText")?.GetComponent<TMP_Text>();
            if (fireModeText == null)
                fireModeText = visualRoot.transform.Find("FireModeText")?.GetComponent<TMP_Text>();
        }

        if (!HasStructuredHud)
        {
            _textComponent = GetComponent<TextMeshProUGUI>();
            if (_textComponent == null)
                _textComponent = GetComponent<Text>();
        }

        if (fireModeText != null)
            RefreshFireModeUI();
    }

    private void OnEnable()
    {
        NetworkPlayer.OnLocalPlayerSpawned += HandleLocalPlayerSpawned;
        NetworkPlayer.OnLocalPlayerDespawned += HandleLocalPlayerDespawned;

        // UI가 늦게 켜져서 이벤트를 놓친 경우 대비(선택)
        if (Camera.main != null)
            HandleLocalPlayerSpawned(Camera.main);
    }

    private void HandleLocalPlayerSpawned(Camera localCamera)
    {
        if (localCamera == null) return;

        var manager = ResolveWeaponManagerFromCamera(localCamera);
        if (manager == null)
        {
            Debug.LogWarning("[WeaponUI] localCamera 기준으로 FPSWeaponManager를 찾지 못했습니다.");
            return;
        }

        BindToWeaponManager(manager);
    }

    private void HandleLocalPlayerDespawned()
    {
        UnsubscribeFromWeapon();
        UnbindFromWeaponManager();
        HideAmmoUI();
    }

    private FPSWeaponManager ResolveWeaponManagerFromCamera(Camera cam)
    {
        // 1) 카메라의 부모 체인에서 찾기
        var mgr = cam.GetComponentInParent<FPSWeaponManager>();
        if (mgr != null) return mgr;

        // 2) 카메라 루트(플레이어 루트) 아래에서 찾기
        return cam.transform.root.GetComponentInChildren<FPSWeaponManager>(true);
    }

    private void OnDisable()
    {
        NetworkPlayer.OnLocalPlayerSpawned -= HandleLocalPlayerSpawned;
        NetworkPlayer.OnLocalPlayerDespawned -= HandleLocalPlayerDespawned;
    }

    private void FindLocalWeaponManager()
    {
        // 모든 FPSWeaponManager 찾기
        FPSWeaponManager[] allManagers = FindObjectsOfType<FPSWeaponManager>();
        
        Debug.Log($"[WeaponUI] 총 {allManagers.Length}개의 FPSWeaponManager 발견");
        
        foreach (var manager in allManagers)
        {
            Debug.Log($"[WeaponUI] 확인 중: {manager.gameObject.name}, isOwner={manager.isOwner}");
            
            // FPSWeaponManager는 NetworkBehaviour를 상속받으므로 직접 isOwner 체크
            if (manager.isOwner)
            {
                Debug.Log($"[WeaponUI] 로컬 FPSWeaponManager 찾음: {manager.gameObject.name}");
                BindToWeaponManager(manager);
                return;
            }
        }
        
        Debug.LogWarning("[WeaponUI] 로컬 FPSWeaponManager를 찾지 못했습니다!");
    }

    private void BindToWeaponManager(FPSWeaponManager manager)
    {
        if (manager == null) return;

        // 이미 같은 매니저면 스킵
        if (weaponManager == manager) return;

        UnbindFromWeaponManager();

        weaponManager = manager;
        weaponManager.OnWeaponEquipped += HandleWeaponEquipped;
        weaponManager.OnWeaponUnequipped += HandleWeaponUnequipped;

        // 현재 상태를 1회 즉시 반영(초기 UI)
        HandleWeaponEquipped(weaponManager.GetActiveItem());
    }

    private void HandleWeaponEquipped(FPSItem item)
    {
        // 이전 무기 이벤트 해제
        UnsubscribeFromWeapon();
        _currentWeapon = null;

        if (item is Weapon weapon)
        {
            _currentWeapon = weapon;
            SubscribeToWeapon();

            // 장착 순간 1회 즉시 갱신(Weapon이 이미 캐시 계산 + OnAmmoChanged 준비해둔 상태)
            UpdateAmmoUI(_currentWeapon.CurrentAmmo, _currentWeapon.GetReserveAmmoFromInventory());
            RefreshFireModeUI();
        }
        else
        {
            HideAmmoUI(); // Fist 포함
        }
    }

    private void HandleWeaponUnequipped()
    {
        UnsubscribeFromWeapon();
        _currentWeapon = null;
        HideAmmoUI();
    }

    private void UnbindFromWeaponManager()
    {
        if (weaponManager == null) return;

        weaponManager.OnWeaponEquipped -= HandleWeaponEquipped;
        weaponManager.OnWeaponUnequipped -= HandleWeaponUnequipped;
        weaponManager = null;
    }
    private void SubscribeToWeapon()
    {
        if (_currentWeapon != null)
        {
            _currentWeapon.OnAmmoChanged += UpdateAmmoUI;
            _currentWeapon.OnFireModeChanged += RefreshFireModeUI;
            Debug.Log("[WeaponUI] Weapon 이벤트 구독 완료");
        }
    }
    
    private void UnsubscribeFromWeapon()
    {
        if (_currentWeapon != null)
        {
            _currentWeapon.OnAmmoChanged -= UpdateAmmoUI;
            _currentWeapon.OnFireModeChanged -= RefreshFireModeUI;
            Debug.Log("[WeaponUI] Weapon 이벤트 구독 해제");
        }
    }

    private void UpdateAmmoUI(int currentAmmo, int reserveAmmo)
    {
        _lastCurrentAmmo = currentAmmo;
        _lastReserveAmmo = reserveAmmo;

        if (HasStructuredHud)
        {
            if (visualRoot != null)
                visualRoot.SetActive(true);

            currentAmmoText.SetText("{0}", currentAmmo);
            reserveAmmoText.SetText("/ {0}", reserveAmmo);
            RefreshFireModeUI();
            return;
        }

        SetTextEnabled(true);
        SetText(string.Format(ammoFormat, currentAmmo, reserveAmmo));
    }

    private void HideAmmoUI()
    {
        if (visualRoot != null)
            visualRoot.SetActive(false);

        if (_textComponent != null)
        {
            SetTextEnabled(false);
        }
    }

    private void RefreshFireModeUI()
    {
        if (fireModeText == null)
            return;

        fireModeText.text = _currentWeapon != null
            ? _currentWeapon.FireModeLabel
            : fireModeLabel;
    }

    private void OnDestroy()
    {
        NetworkPlayer.OnLocalPlayerSpawned -= HandleLocalPlayerSpawned;
        NetworkPlayer.OnLocalPlayerDespawned -= HandleLocalPlayerDespawned;

        UnsubscribeFromWeapon();
        UnbindFromWeaponManager();
    }


    // ============ Text 헬퍼 메서드 ============
    private void SetText(string value)
    {
        if (_textComponent is TextMeshProUGUI tmp)
        {
            tmp.text = value;
        }
        else if (_textComponent is Text text)
        {
            text.text = value;
        }
    }
    
    private void SetTextEnabled(bool enabled)
    {
        if (_textComponent is TextMeshProUGUI tmp)
        {
            tmp.enabled = enabled;
        }
        else if (_textComponent is Text text)
        {
            text.enabled = enabled;
        }
    }
}
