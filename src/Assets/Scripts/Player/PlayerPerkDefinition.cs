using UnityEngine;

public enum PlayerPerkKind
{
    /// <summary>걷기 속도 +value/레벨 (0.08 = 8%)</summary>
    WalkSpeed,
    /// <summary>달리기 속도 +value/레벨</summary>
    SprintSpeed,
    /// <summary>점프 높이 +value/레벨</summary>
    JumpHeight,
    /// <summary>공중 추가 점프 +value/레벨 (정수)</summary>
    ExtraAirJump,
    /// <summary>최대 체력 +value/레벨</summary>
    MaxHealth,
    /// <summary>최대 스태미나 +value/레벨</summary>
    MaxStamina,
    /// <summary>스태미나 회복 +value/레벨 (초당)</summary>
    StaminaRegen,
    /// <summary>받는 피해 -value/레벨 (0.1 = 10%)</summary>
    DamageReduction,
    /// <summary>점프 착지 시 반경 3m 몬스터에게 value 피해/레벨 (추가 공격 요소)</summary>
    GroundSlam,
    /// <summary>피격 후 3초간 달리기 속도 +value/레벨 (아드레날린)</summary>
    Adrenaline,

    // ── 강화 미니게임 / 죽음에 개입하는 퍼크 (서버가 판정) ──
    /// <summary>강화 성공률 +value/레벨 (0.05 = 5%)</summary>
    SteadyHands,
    /// <summary>하락 구간 실패 시 하루 1회 하락 방지</summary>
    SafetyNet,
    /// <summary>실패 직후 같은 단계 1회 무료 재시도 (하루 1회)</summary>
    SecondChance,
    /// <summary>매일 첫 강화 무료</summary>
    FreeForge,
    /// <summary>재료 보너스 +value (0.10 = 재료 성공률 +10%p 추가)</summary>
    MasterSmith,
    /// <summary>전멸 = 런 종료 → 하루 손실로 완화 (팀 중 1명만 있어도 적용)</summary>
    TeamInsurance,
    /// <summary>하루 1회 죽은 자리에서 5초 뒤 HP 30%로 자동 부활</summary>
    LastStand,
    /// <summary>부활 비용 -value (0.2 = 20%)</summary>
    Haggler,
    /// <summary>던전 전력(배터리) 용량 +value/레벨 (팀 합산)</summary>
    BatteryCapacity
}

/// <summary>서버로 보고되는 퍼크 플래그 (판정은 항상 서버).</summary>
[System.Flags]
public enum ServerPerkFlags
{
    None = 0,
    SafetyNet = 1 << 0,
    SecondChance = 1 << 1,
    FreeForge = 1 << 2,
    MasterSmith = 1 << 3,
    TeamInsurance = 1 << 4,
    LastStand = 1 << 5
}

/// <summary>
/// SkillWeb 노드 이름 ↔ 실제 효과 매핑. Resources/Perks 폴더에 두면 자동 로드되고,
/// 하나도 없으면 PlayerPerks의 코드 기본표를 쓴다.
/// </summary>
[CreateAssetMenu(fileName = "Perk", menuName = "Player/Perk Definition")]
public class PlayerPerkDefinition : ScriptableObject
{
    [Tooltip("SkillWeb 스킬 이름과 정확히 일치해야 한다 (예: Fleet Foot I)")]
    public string skillName;
    public PlayerPerkKind kind;
    [Tooltip("레벨당 효과량. 비율형은 0.1 = 10%, 정수형은 개수/포인트")]
    public float valuePerLevel = 0.1f;
    [TextArea] public string description;
}
