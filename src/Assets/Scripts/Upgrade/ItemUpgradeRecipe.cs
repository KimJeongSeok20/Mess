using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무기 강화 레시피 (공식 기반, +999까지 무한 강화 지원)
///
/// 두 층으로 이루어진다.
///   1) 연속 스케일링: 티어마다 데미지/연사/탄창/탄속/집탄이 조금씩 오른다.
///   2) 마일스톤: 특정 티어에서 이름 붙은 특성이 열린다 (예: +3 "더블 탭" = 한 번에 2발).
///
/// 강화 규칙:
///   최고 티어 무기 + 같은 종류의 미강화(+0) 무기 1개 → 한 단계 강화 시도
///   예: AK +5 + AK +0 → AK +6 시도
/// </summary>
[CreateAssetMenu(fileName = "UpgradeChain", menuName = "Upgrade/Item Upgrade Recipe")]
public class ItemUpgradeRecipe : ScriptableObject
{
    public enum MilestoneEffect
    {
        /// <summary>한 번의 트리거에 총알 +value발 (히트스캔은 펠릿, 발사체는 추가 발사체).</summary>
        ExtraShot,
        /// <summary>탄창 +value발.</summary>
        MagazineBonus,
        /// <summary>탄속 ×(1+value).</summary>
        ProjectileSpeedBonus,
        /// <summary>데미지 ×(1+value).</summary>
        DamageBonus,
        /// <summary>연사 ×(1+value).</summary>
        FireRateBonus,
        /// <summary>퍼짐 ×(1-value).</summary>
        SpreadReduction,
        /// <summary>폭발 반경 ×(1+value) (발사체 무기).</summary>
        ExplosionRadiusBonus
    }

    [Serializable]
    public struct Milestone
    {
        [Tooltip("이 티어에 도달하면 열린다")]
        [Min(1)] public int tier;
        [Tooltip("UI에 표시할 이름 (예: 더블 탭)")]
        public string label;
        public MilestoneEffect effect;
        [Tooltip("ExtraShot/MagazineBonus는 정수 개수, 나머지는 비율(0.3 = 30%)")]
        public float value;
    }

    /// <summary>티어를 적용한 최종 발사 스탯. 기본 WeaponData 값에 곱/합한 결과.</summary>
    public struct WeaponUpgradeStats
    {
        public int damage;
        public int explosionDamage;
        public float fireRateRpm;
        public int magazineSize;
        public float projectileSpeed;
        public float spreadDeg;
        public float explosionRadius;
        public int extraShotsPerTrigger;
    }

    [Header("Base Weapon")]
    [Tooltip("무기의 기본 아이템 이름 (Item.ItemName과 일치해야 함)")]
    public string baseItemName;

    [Tooltip("무기의 기본 프리팹 (WeaponItem 컴포넌트 포함)")]
    public GameObject baseItemPrefab;

    [Header("Stat Scaling — 비율 기반")]
    [Tooltip("티어당 데미지 증가율 (0.1 = 티어당 +10%)")]
    [Range(0f, 1f)]
    public float damageBonusPerTier = 0.1f;

    [Tooltip("티어당 연사속도(RPM) 증가율 (0.05 = 티어당 +5%)")]
    [Range(0f, 1f)]
    public float fireRateBonusPerTier = 0.05f;

    [Tooltip("티어당 탄창 추가 발수")]
    [Min(0)]
    public int magazineBonusPerTier = 2;

    [Tooltip("티어당 탄속 증가율 (발사체 무기만)")]
    [Range(0f, 1f)]
    public float projectileSpeedBonusPerTier = 0.06f;

    [Tooltip("티어당 퍼짐 감소율. 누적 최대 60%까지만 줄어든다")]
    [Range(0f, 0.5f)]
    public float spreadReductionPerTier = 0.05f;

    [Header("Milestones — 티어별 특성")]
    [Tooltip("비워 두면 기본 마일스톤(+3 더블 탭, +5 확장 탄창, +7 고속탄, +9 트리플 탭)을 쓴다")]
    public List<Milestone> milestones = new();

    [Header("Stat Scaling — 커스텀 커브 (선택)")]
    [Tooltip("true면 위 비율 대신 아래 커브를 사용")]
    public bool useCustomCurve = false;

    [Tooltip("X축=티어(0~1 범위로 maxTier 정규화), Y축=배율(1.0=기본)\n" +
             "예: (0, 1.0) → (0.5, 1.5) → (1.0, 3.0)")]
    public AnimationCurve damageCurve = AnimationCurve.Linear(0f, 1f, 1f, 3f);

    [Tooltip("X축=티어(0~1 범위로 maxTier 정규화), Y축=배율")]
    public AnimationCurve fireRateCurve = AnimationCurve.Linear(0f, 1f, 1f, 2f);

    [Header("Limits")]
    [Tooltip("최대 강화 티어 (0 = 999까지 무제한)")]
    [Min(0)]
    public int maxTier = 999;

    [Header("Roll — 스타포스식 확률 강화")]
    [Tooltip("결과 티어별 성공률 (index 0 = +1). 비우면 기본표: 100/95/90/80/70/60/50/45/40/35/30/25, 이후 20%에서 2%씩 감소, 최저 10%")]
    public List<float> successChanceByTier = new();

    [Tooltip("이 결과 티어부터는 실패 시 한 단계 하락한다 (0 = 하락 없음)")]
    [Min(0)]
    public int downgradeFromTier = 7;

    [Tooltip("같은 무기 1개를 재료로 넣었을 때 성공률 보너스")]
    [Range(0f, 0.5f)]
    public float materialSuccessBonus = 0.15f;

    [Tooltip("같은 단계 연속 실패마다 성공률 보너스 (2회 실패 → +10%, 3회 실패 → 확정)")]
    [Range(0f, 0.5f)]
    public float pityBonusPerFail = 0.05f;

    [Min(1)]
    public int pityGuaranteeAfterFails = 3;

    [Header("Cost")]
    [Tooltip("+1 강화 비용 (0 = 무료). 이후 티어는 upgradeCostGrowthPerTier만큼 비싸진다.")]
    [Min(0)]
    public int upgradeCost = 800;

    [Tooltip("티어당 비용 증가율. 0.5 = +1 800, +2 1200, +3 1600 ...")]
    [Min(0f)]
    public float upgradeCostGrowthPerTier = 0.5f;

    private static readonly Milestone[] DefaultMilestones =
    {
        new Milestone { tier = 3, label = "Double Tap", effect = MilestoneEffect.ExtraShot, value = 1f },
        new Milestone { tier = 5, label = "Extended Mag", effect = MilestoneEffect.MagazineBonus, value = 10f },
        new Milestone { tier = 7, label = "High Velocity", effect = MilestoneEffect.ProjectileSpeedBonus, value = 0.35f },
        new Milestone { tier = 9, label = "Triple Tap", effect = MilestoneEffect.ExtraShot, value = 1f },
        new Milestone { tier = 12, label = "Overclock", effect = MilestoneEffect.FireRateBonus, value = 0.25f },
    };

    // ═══════════ Properties ═══════════

    public int EffectiveMaxTier => maxTier <= 0 ? 999 : maxTier;

    public IReadOnlyList<Milestone> EffectiveMilestones =>
        milestones != null && milestones.Count > 0 ? milestones : DefaultMilestones;

    /// <summary>
    /// 결과 티어(resultTier)로 강화할 때 드는 비용. 무기 강화는 돈이 빠져나가는 몇 안 되는 통로라
    /// 세금 곡선과 같이 오르게 선형 증가시킨다.
    /// </summary>
    public int GetUpgradeCost(int resultTier)
    {
        if (upgradeCost <= 0)
            return 0;

        int tierSteps = Mathf.Max(0, resultTier - 1);
        double cost = upgradeCost * (1.0 + Mathf.Max(0f, upgradeCostGrowthPerTier) * tierSteps);
        return cost >= int.MaxValue ? int.MaxValue : Mathf.Max(0, (int)System.Math.Round(cost));
    }

    private static readonly float[] DefaultSuccessChance =
        { 1.00f, 0.95f, 0.90f, 0.80f, 0.70f, 0.60f, 0.50f, 0.45f, 0.40f, 0.35f, 0.30f, 0.25f };

    /// <summary>결과 티어 resultTier(+1, +2, ...)로 올릴 때의 기본 성공률 (0~1).</summary>
    public float GetBaseSuccessChance(int resultTier)
    {
        if (resultTier <= 0)
            return 1f;

        int index = resultTier - 1;
        if (successChanceByTier != null && successChanceByTier.Count > 0)
        {
            if (index < successChanceByTier.Count)
                return Mathf.Clamp01(successChanceByTier[index]);
            return Mathf.Clamp01(successChanceByTier[successChanceByTier.Count - 1]);
        }

        if (index < DefaultSuccessChance.Length)
            return DefaultSuccessChance[index];

        return Mathf.Max(0.10f, 0.20f - 0.02f * (index - DefaultSuccessChance.Length));
    }

    /// <summary>실패 시 하락하는 구간인지.</summary>
    public bool DowngradesOnFail(int resultTier)
    {
        return downgradeFromTier > 0 && resultTier >= downgradeFromTier;
    }

    /// <summary>
    /// 최종 성공률: 기본 + 재료 + 연속 실패 보정 + 플레이어 퍼크. pity 횟수를 넘으면 확정.
    /// </summary>
    public float GetSuccessChance(int resultTier, bool withMaterial, int consecutiveFails, float perkBonus)
    {
        if (consecutiveFails >= pityGuaranteeAfterFails)
            return 1f;

        float chance = GetBaseSuccessChance(resultTier);
        if (withMaterial)
            chance += materialSuccessBonus;
        chance += pityBonusPerFail * Mathf.Max(0, consecutiveFails);
        chance += Mathf.Max(0f, perkBonus);
        return Mathf.Clamp01(chance);
    }

    // ═══════════ Validation ═══════════

    public bool IsValid()
    {
        if (string.IsNullOrEmpty(baseItemName)) return false;
        if (baseItemPrefab == null) return false;
        if (baseItemPrefab.GetComponent<Item>() == null) return false;
        return true;
    }

    // ═══════════ Icon / Name ═══════════

    /// <summary>
    /// 기본 무기 아이콘
    /// </summary>
    public Sprite GetBaseIcon()
    {
        if (baseItemPrefab == null) return null;
        var item = baseItemPrefab.GetComponent<Item>();
        return item != null ? item.ItemPicture : null;
    }

    /// <summary>
    /// 티어별 표시 이름 ("AK", "AK +1", "AK +5" ...)
    /// </summary>
    public string GetTierDisplayName(int tier)
    {
        return tier > 0 ? $"{baseItemName} +{tier}" : baseItemName;
    }

    // ═══════════ Stat Calculation ═══════════

    /// <summary>
    /// 특정 티어의 데미지 계산
    /// </summary>
    public int CalcDamage(int baseDamage, int tier)
    {
        if (tier <= 0) return baseDamage;

        float multiplier;
        if (useCustomCurve)
        {
            float t = Mathf.Clamp01((float)tier / EffectiveMaxTier);
            multiplier = damageCurve.Evaluate(t);
        }
        else
        {
            multiplier = 1f + tier * damageBonusPerTier;
        }

        multiplier *= 1f + SumMilestone(MilestoneEffect.DamageBonus, tier);
        return Mathf.Max(1, Mathf.RoundToInt(baseDamage * multiplier));
    }

    /// <summary>
    /// 특정 티어의 연사속도(RPM) 계산
    /// </summary>
    public float CalcFireRate(float baseRpm, int tier)
    {
        if (tier <= 0) return baseRpm;

        float multiplier;
        if (useCustomCurve)
        {
            float t = Mathf.Clamp01((float)tier / EffectiveMaxTier);
            multiplier = fireRateCurve.Evaluate(t);
        }
        else
        {
            multiplier = 1f + tier * fireRateBonusPerTier;
        }

        multiplier *= 1f + SumMilestone(MilestoneEffect.FireRateBonus, tier);
        return baseRpm * multiplier;
    }

    public int CalcMagazine(int baseMagazine, int tier)
    {
        if (tier <= 0) return baseMagazine;
        int bonus = magazineBonusPerTier * tier + Mathf.RoundToInt(SumMilestone(MilestoneEffect.MagazineBonus, tier));
        return Mathf.Max(1, baseMagazine + bonus);
    }

    public float CalcProjectileSpeed(float baseSpeed, int tier)
    {
        if (tier <= 0) return baseSpeed;
        float multiplier = (1f + tier * projectileSpeedBonusPerTier) * (1f + SumMilestone(MilestoneEffect.ProjectileSpeedBonus, tier));
        return baseSpeed * multiplier;
    }

    public float CalcSpread(float baseSpread, int tier)
    {
        if (tier <= 0) return baseSpread;
        float reduction = Mathf.Clamp(tier * spreadReductionPerTier + SumMilestone(MilestoneEffect.SpreadReduction, tier), 0f, 0.6f);
        return baseSpread * (1f - reduction);
    }

    public float CalcExplosionRadius(float baseRadius, int tier)
    {
        if (tier <= 0) return baseRadius;
        return baseRadius * (1f + SumMilestone(MilestoneEffect.ExplosionRadiusBonus, tier));
    }

    /// <summary>한 번의 트리거에 추가로 나가는 총알 수 (마일스톤 누적).</summary>
    public int CalcExtraShots(int tier)
    {
        if (tier <= 0) return 0;
        return Mathf.Max(0, Mathf.RoundToInt(SumMilestone(MilestoneEffect.ExtraShot, tier)));
    }

    /// <summary>티어를 적용한 전체 스탯을 한 번에 계산한다 (서버/클라 공통).</summary>
    public WeaponUpgradeStats CalcStats(WeaponData baseData, int tier)
    {
        var stats = new WeaponUpgradeStats
        {
            damage = baseData.damage,
            explosionDamage = baseData.explosionDamage,
            fireRateRpm = baseData.serverFireRateRpm,
            magazineSize = baseData.magazineSize,
            projectileSpeed = baseData.projectileSpeed,
            spreadDeg = baseData.spreadDeg,
            explosionRadius = baseData.explosionRadius,
            extraShotsPerTrigger = 0
        };

        if (baseData == null || tier <= 0)
            return stats;

        stats.damage = CalcDamage(baseData.damage, tier);
        stats.explosionDamage = CalcDamage(baseData.explosionDamage, tier);
        stats.fireRateRpm = CalcFireRate(baseData.serverFireRateRpm, tier);
        stats.magazineSize = CalcMagazine(baseData.magazineSize, tier);
        stats.projectileSpeed = CalcProjectileSpeed(baseData.projectileSpeed, tier);
        stats.spreadDeg = CalcSpread(baseData.spreadDeg, tier);
        stats.explosionRadius = CalcExplosionRadius(baseData.explosionRadius, tier);
        stats.extraShotsPerTrigger = CalcExtraShots(tier);
        return stats;
    }

    /// <summary>해당 티어에서 정확히 열리는 마일스톤 (없으면 null). UI 표시용.</summary>
    public Milestone? GetMilestoneAt(int tier)
    {
        var list = EffectiveMilestones;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].tier == tier)
                return list[i];
        }

        return null;
    }

    /// <summary>tier 다음에 오는 마일스톤 (없으면 null). "다음 특성까지 N단계" 표시용.</summary>
    public Milestone? GetNextMilestone(int tier)
    {
        Milestone? best = null;
        var list = EffectiveMilestones;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].tier > tier && (!best.HasValue || list[i].tier < best.Value.tier))
                best = list[i];
        }

        return best;
    }

    private float SumMilestone(MilestoneEffect effect, int tier)
    {
        float total = 0f;
        var list = EffectiveMilestones;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].effect == effect && list[i].tier <= tier)
                total += list[i].value;
        }

        return total;
    }

    /// <summary>
    /// 기본 WeaponData 가져오기 (프리팹에서)
    /// </summary>
    public WeaponData GetBaseWeaponData()
    {
        if (baseItemPrefab == null) return null;
        var wi = baseItemPrefab.GetComponent<WeaponItem>();
        return wi != null ? wi.WeaponData : null;
    }
}
