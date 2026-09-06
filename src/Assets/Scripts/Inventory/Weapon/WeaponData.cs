using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 총기 전용 데이터 (Fist는 포함하지 않음)
/// WeaponItem, Weapon 클래스가 참조하는 공통 설정
/// </summary>
[CreateAssetMenu(fileName = "WeaponData", menuName = "Inventory/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("Basic Info")]
    public string weaponName = "Weapon";
    public Sprite icon;

    [Header("Prefab References")]
    [Tooltip("총기 인스턴스 프리팹 (FPS 시스템에서 사용)")]
    public GameObject weaponInstancePrefab;

    [Tooltip("바닥 아이템 프리팹 (WeaponItem 컴포넌트 포함)")]
    public GameObject itemPrefab;

    [Header("Ammo Settings")]
    [Tooltip("줄을 수 있는 탄약 아이템 (Drake12_Ammo_Item)")]
    public GameObject ammoItemPrefab;

    [Tooltip("발사되는 총알 프리팹 (Projectile)")]
    public GameObject projectilePrefab;

    [Tooltip("탄창 크기")]
    public int magazineSize = 30;

    // --- Fire / Hitscan / VFX 확장 (추가) ---

    public enum FireSimulationType
    {
        Hitscan,   // AK/권총/샷건 등: 레이캐스트
        Projectile // RPG 등: 프리팹 탄
    }

    [Header("Fire Simulation")]
    public FireSimulationType fireSimulation = FireSimulationType.Hitscan;

    [Header("Hitscan (Server Auth)")]
    [Tooltip("서버에서 발사 레이트 제한용 RPM")]
    public float serverFireRateRpm = 600f;

    [Tooltip("샷건 탄 퍼짐(펠릿 수). 일반 총은 1")]
    public int pellets = 1;

    [Tooltip("한 번의 트리거에 추가로 발사되는 발사체 수 (강화 마일스톤이 채움). 히트스캔은 pellets에 더해진다.")]
    [Min(0)]
    public int extraShotsPerTrigger = 0;

    [Tooltip("사거리")]
    public float range = 200f;

    [Tooltip("퍼짐 각도(도 단위). 0이면 정확")]
    public float spreadDeg = 0.3f;

    [Tooltip("레이캐스트 충돌 마스크")]
    public LayerMask hitMask = ~0;

    [Tooltip("피해량(펠릿 1발당)")]
    public int damage = 25;

    [Header("VFX - Muzzle / Trail / Impact")]
    [Tooltip("머즐 플래시 프리팹")]
    public GameObject muzzleFlashPrefab;

    [Tooltip("머즐 플래시 인스턴스 유지 시간(초)")]
    [Min(0.1f)]
    public float muzzleFlashLifetime = 2f;

    [Tooltip("총알 트레일 프리팹(예: TrailRenderer 포함)")]
    public GameObject bulletTrailPrefab;

    [Tooltip("트레일이 목표점까지 날아가는 시간(연출용)")]
    public float trailTravelTime = 0.05f;

    [Tooltip("탄착 VFX 프리팹(스파크/먼지 등)")]
    public GameObject impactEffectPrefab;

    [Tooltip("표면에서 살짝 띄워서 z-fighting 방지")]
    public float surfaceOffset = 0.01f;

    [Tooltip("탄착 VFX 유지 시간")]
    public float impactLifetime = 2f;

    // ===================== Projectile Settings =====================
    [Header("Projectile Settings (FireSimulation == Projectile)")]
    [Tooltip("발사체 초기 속도 (m/s)")]
    public float projectileSpeed = 30f;

    [Tooltip("발사체에 중력 적용 여부")]
    public bool projectileUseGravity = true;

    [Tooltip("발사체 최대 수명(초) — 자동 파괴")]
    public float projectileLifetime = 8f;

    [Header("Explosion (Projectile)")]
    [Tooltip("폭발 반경 (0이면 직격 데미지만)")]
    public float explosionRadius = 5f;

    [Tooltip("폭발 데미지 (반경 내 거리 감쇠 적용)")]
    public int explosionDamage = 150;

    [Tooltip("폭발 판정 레이어 마스크")]
    public LayerMask explosionHitMask = ~0;

    [Tooltip("폭발 VFX 프리팹")]
    public GameObject explosionEffectPrefab;

    [Tooltip("폭발 VFX 유지 시간")]
    public float explosionEffectLifetime = 3f;
    // ===================== Projectile Settings =====================


    // ===================== SFX =====================
    [System.Serializable]
    public class WeaponSfx
    {
        [Header("Fire")]
        public List<AudioClip> fireSounds = new();
        [Tooltip("샷마다 피치를 랜덤으로 흔들어 반복감을 줄임 (권장: 0.97~1.03)")]
        public Vector2 firePitchRange = new Vector2(0.97f, 1.03f);

        [Tooltip("샷마다 볼륨을 랜덤으로 흔들어 반복감을 줄임 (권장: 0.95~1.05)")]
        public Vector2 fireVolumeRange = new Vector2(0.95f, 1.05f);

        [Range(0f, 2f)]
        [Tooltip("이 무기 발사음 전체 볼륨 배수")]
        public float fireVolumeMultiplier = 1f;

        [Header("Reload / Event")]
        [Tooltip("너는 이벤트 사운드를 1개만 쓴다 했으니 단일 클립으로")]
        public AudioClip reloadSound;

        [Range(0f, 2f)]
        [Tooltip("이 무기 리로드/이벤트 사운드 볼륨 배수")]
        public float reloadVolumeMultiplier = 1f;

        [Header("3D Distance Attenuation")]
        [Tooltip("켜면 3D로 거리 감쇠(구 형태로 퍼짐). 끄면 2D로 고정.")]
        public bool use3D = true;

        [Tooltip("이 거리까지는 거의 풀 볼륨")]
        public float minDistance = 1.5f;

        [Tooltip("이 거리 즈음이면 거의 안 들림")]
        public float maxDistance = 30f;

        public AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;

        [Tooltip("RolloffMode가 Custom일 때만 사용 (0~1 거리 비율 -> 볼륨)")]
        public AnimationCurve customRolloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    }

    [Header("SFX")]
    public WeaponSfx sfx = new WeaponSfx();
    // ===================== SFX =====================


    /// <summary>
    /// FPSWeaponManager가 인스턴스화할 프리팹을 반환합니다.
    /// (Resources.Load 의존 제거: 반드시 직접 프리팹 참조를 넣어야 합니다)
    /// </summary>
    public GameObject GetWeaponPrefab()
    {
        if (weaponInstancePrefab != null) return weaponInstancePrefab;

        Debug.LogError($"[WeaponData] {weaponName}: weaponInstancePrefab이 비어있습니다!");
        return null;
    }

    public string GetAmmoItemNameKey()
    {
        if (ammoItemPrefab == null) return null;

        var item = ammoItemPrefab.GetComponent<Item>();
        if (item != null && !string.IsNullOrEmpty(item.ItemName))
            return item.ItemName;

        Debug.LogWarning("[WeaponData] ammoItemPrefab에 Item(ItemName)이 없습니다.");
        return null;
    }

    public StatLine[] CreateStatLines()
    {
        CultureInfo invariant = CultureInfo.InvariantCulture;

        return new[]
        {
            new StatLine("Damage", damage.ToString(invariant)),
            new StatLine("Magazine", magazineSize.ToString(invariant)),
            new StatLine("Range", $"{range.ToString("0.##", invariant)} m"),
            new StatLine("Pellets", pellets.ToString(invariant)),
            new StatLine("Spread", $"{spreadDeg.ToString("0.##", invariant)} deg"),
            new StatLine("Fire Rate", $"{serverFireRateRpm.ToString("0.##", invariant)} RPM")
        };
    }

}
