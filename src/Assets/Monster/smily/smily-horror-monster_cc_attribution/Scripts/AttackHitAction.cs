using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "AttackHit", story: "AttackHitbox and store result in [AttackHitSuccess]", category: "Action", id: "read_attack_hit_123456")]
public partial class ReadAttackHitAction : Action
{
    [SerializeReference] public BlackboardVariable<AttackHitbox> AttackHitbox;
    [SerializeReference] public BlackboardVariable<bool> AttackHitSuccess;

    // 얼마나 오래까지 히트 체크를 할지 (점프 길이 + 약간 여유)
    [SerializeReference]
    public BlackboardVariable<float> WatchDuration
        = new BlackboardVariable<float> { Value = 0.7f };

    private float _elapsed;

    protected override Status OnStart()
    {
        _elapsed = 0f;

        if (AttackHitbox == null || AttackHitbox.Value == null || AttackHitSuccess == null)
            return Status.Failure;

        AttackHitSuccess.Value = false;

        // 새 공격 시작이니까 이전 히트 정보 리셋
        AttackHitbox.Value.BeginAttackWindow(WatchDuration?.Value ?? 0.7f);
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (AttackHitbox == null || AttackHitbox.Value == null || AttackHitSuccess == null)
            return Status.Failure;

        _elapsed += Time.deltaTime;

        // 1) 플레이어를 맞춘 경우
        if (AttackHitbox.Value.HasHitThisAttack)
        {
            AttackHitSuccess.Value = true;
            AttackHitbox.Value.EndAttackWindow();
            return Status.Success; // 감시 종료
        }

        // 2) 제한 시간 초과 – 못 맞춤
        if (_elapsed >= (WatchDuration?.Value ?? 0.7f))
        {
            AttackHitSuccess.Value = false;
            AttackHitbox.Value.EndAttackWindow();
            return Status.Success; // “이번 공격은 빗나감” 이라는 의미로 종료
        }

        // 3) 아직 점프/공격 중 → 계속 감시
        return Status.Running;
    }

    protected override void OnEnd()
    {
        // 안전하게 한 번 더 리셋
        if (AttackHitbox != null && AttackHitbox.Value != null)
            AttackHitbox.Value.EndAttackWindow();
    }
}
