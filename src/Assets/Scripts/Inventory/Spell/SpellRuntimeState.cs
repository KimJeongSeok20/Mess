using UnityEngine;

/// <summary>
/// 마법 런타임 상태 (가변 데이터).
/// SpellData(불변)와 분리하여 충전 리셋 정책(일일 리셋 / 소모품)을
/// SpellData 수정 없이 교체할 수 있도록 설계.
/// MonoBehaviour 아님 — 순수 C# 클래스.
/// </summary>
public class SpellRuntimeState
{
    public int MaxCharges { get; private set; }
    public int ChargesRemaining { get; private set; }
    public float LastCastTime { get; private set; }

    public SpellRuntimeState(int maxCharges)
    {
        MaxCharges = Mathf.Max(1, maxCharges);
        ChargesRemaining = MaxCharges;
        LastCastTime = -999f;
    }

    /// <summary>
    /// 현재 시전 가능 여부 (충전 남음 + 쿨다운 경과)
    /// </summary>
    public bool CanCast(float cooldown, float currentTime)
    {
        if (ChargesRemaining <= 0) return false;
        if (currentTime - LastCastTime < cooldown) return false;
        return true;
    }

    /// <summary>
    /// 시전 시도: CanCast 통과 시 충전 소모 + 시간 기록. 성공 여부 반환.
    /// </summary>
    public bool TryConsume(float cooldown, float currentTime)
    {
        if (!CanCast(cooldown, currentTime)) return false;

        ChargesRemaining--;
        LastCastTime = currentTime;
        return true;
    }

    /// <summary>
    /// 충전 완전 초기화 (일일 리셋 시나리오)
    /// </summary>
    public void ResetCharges()
    {
        ChargesRemaining = MaxCharges;
    }

    /// <summary>
    /// 외부에서 충전 수 직접 지정 (소모품 획득/네트워크 동기화)
    /// </summary>
    public void SetCharges(int count)
    {
        ChargesRemaining = Mathf.Clamp(count, 0, MaxCharges);
    }

    /// <summary>
    /// 남은 쿨다운 시간 (UI용)
    /// </summary>
    public float GetCooldownRemaining(float cooldown, float currentTime)
    {
        float elapsed = currentTime - LastCastTime;
        return Mathf.Max(0f, cooldown - elapsed);
    }
}
