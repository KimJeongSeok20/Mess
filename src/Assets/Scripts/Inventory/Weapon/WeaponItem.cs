using UnityEngine;
using PurrNet;

/// <summary>
/// 총기 바닥 아이템 (Fist는 이 클래스를 사용하지 않음)
/// NetworkBehaviour를 상속하여 자동 네트워크 동기화
/// </summary>
public class WeaponItem : Item
{
    [SerializeField] private WeaponData weaponData;

    private readonly SyncVar<int> _currentAmmo = new();
    private readonly SyncVar<int> _upgradeTier = new(0);

    public WeaponData WeaponData => weaponData;
    public int CurrentAmmo => _currentAmmo.value;
    public int UpgradeTier => _upgradeTier.value;

    /// <summary>
    /// 화면 표시용 이름: "AK +5" (티어 0이면 기본 이름)
    /// </summary>
    public override string DisplayName => _upgradeTier.value > 0
        ? $"{base.DisplayName} +{_upgradeTier.value}"
        : base.DisplayName;

    public void SetAmmoServer(int ammo)
    {
        if (isSpawned && !isServer) return;
        _currentAmmo.value = ammo;
    }

    public void SetAmmoImmediateOnServer(int ammo)
    {
        if (isSpawned && !isServer) return;
        _currentAmmo.value = ammo;
    }

    public void SetUpgradeTierServer(int tier)
    {
        if (isSpawned && !isServer) return;
        _upgradeTier.value = tier;
    }

    public void SetUpgradeTierImmediateOnServer(int tier)
    {
        if (isSpawned && !isServer) return;
        _upgradeTier.value = tier;
    }

    public void InitializeLocal(WeaponData data)
    {
        weaponData = data;
    }
}
