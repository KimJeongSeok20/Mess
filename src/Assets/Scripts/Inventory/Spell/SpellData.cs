using UnityEngine;

/// <summary>
/// 마법 스킬 정의 (불변 데이터).
/// WeaponData처럼 ScriptableObject로 에디터에서 생성/관리.
/// 런타임 가변 상태(잔여 충전 등)는 SpellRuntimeState에서 관리.
/// </summary>
[CreateAssetMenu(fileName = "SpellData", menuName = "Inventory/Spell Data")]
public class SpellData : ScriptableObject
{
    public enum SpellType
    {
        Projectile,     // 전방 발사 → 레이캐스트 데미지 (파이어볼, 빙창 등)
        AreaOfEffect,   // 범위 폭발/지속 (지면 AoE)
        SelfBuff,       // 자신에게 버프 (미래 확장)
        Heal            // 자신/아군 회복
    }

    [Header("Basic Info")]
    public string spellName = "Spell";
    public Sprite icon;

    [Header("Spell Type")]
    public SpellType spellType = SpellType.Projectile;

    [Header("Charges")]
    [Tooltip("이 마법을 사용할 수 있는 최대 횟수")]
    [Min(1)] public int maxCharges = 3;

    [Tooltip("한 번 사용 후 다음 사용까지 대기 시간(초)")]
    [Min(0f)] public float cooldownPerCast = 1.5f;

    [Header("Damage (Projectile / AoE)")]
    [Min(0)] public int damage = 50;

    [Tooltip("사거리 (발사체 최대 거리 또는 AoE 시전 거리)")]
    [Min(0f)] public float range = 40f;

    [Tooltip("AoE 반경 (0이면 단일 대상)")]
    [Min(0f)] public float radius = 0f;

    [Tooltip("데미지 판정 레이어")]
    public LayerMask hitMask = ~0;

    [Header("Heal / Buff")]
    [Min(0)] public int healAmount = 0;
    [Min(0f)] public float buffDuration = 0f;

    [Header("VFX — Magic Effects Pack v5 Prefabs")]
    [Tooltip("캐스팅 시 손에 부착되는 이펙트 (HandEffect/)")]
    public GameObject handEffectPrefab;

    [Tooltip("실제 마법 이펙트 — 발사체/AoE (MainEffects/)")]
    public GameObject mainEffectPrefab;

    [Tooltip("자신에게 적용되는 이펙트 — 버프/실드 (CharacterEffect/)")]
    public GameObject characterEffectPrefab;

    [Tooltip("이펙트 자동 파괴 시간")]
    [Min(0.1f)] public float effectLifetime = 5f;

    [Tooltip("MagicFX5 발사체 비주얼 속도 (서버는 레이캐스트 사용)")]
    [Min(0f)] public float projectileVisualSpeed = 20f;

    [Header("SFX")]
    public AudioClip castSound;
    [Range(0f, 1f)] public float castVolume = 1f;

    [Header("Weapon System Integration")]
    [Tooltip("FPS 뷰에서 사용할 스펠 아이템 프리팹 (SpellItem 컴포넌트 포함)")]
    public GameObject spellWeaponPrefab;
}
