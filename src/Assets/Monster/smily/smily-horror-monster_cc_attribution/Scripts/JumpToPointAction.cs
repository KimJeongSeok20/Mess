using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "JumpToPoint",
    story: "jump [Self] towards [JumpPoint]",
    category: "Action",
    id: "4c3a3c2b6a7f4b93b4c8e9a1f9d01234")]
public partial class JumpToPointAction : Action
{
    // ������ ������ ������ ������Ʈ (Self�� �پ� �ִ� JumpToPoint ����)
    [SerializeReference] public BlackboardVariable<JumpToPoint> Jumper;

    // AssignJumpPointAction���� ����� ���� ��ǥ ����
    [SerializeReference] public BlackboardVariable<Vector3> JumpPoint;

    // �ν����Ϳ��� Ʃ�׿�
    [SerializeReference]
    public BlackboardVariable<float> Duration
        = new BlackboardVariable<float> { Value = 0.4f };

    [SerializeReference]
    public BlackboardVariable<float> Height
        = new BlackboardVariable<float> { Value = 1.5f };

    // Telegraph + avoid window fields
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [SerializeReference] public BlackboardVariable<Animator> Animator;
    [SerializeReference] public BlackboardVariable<string> AttackTrigger = new BlackboardVariable<string> { Value = "Attack" };
    [SerializeReference] public BlackboardVariable<float> TelegraphDuration = new BlackboardVariable<float> { Value = 0.8f };
    [SerializeReference] public BlackboardVariable<float> MaxJumpDistance = new BlackboardVariable<float> { Value = 8f };
    [SerializeReference] public BlackboardVariable<float> AvoidDistance = new BlackboardVariable<float> { Value = 2.5f };

    private float _telegraphTimer;
    private Vector3 _telegraphStartPos;
    private bool _jumpStarted;
    private Animator _cachedAnimator;

     private Animator ResolveAnimator()
     {
         if (Animator != null && Animator.Value != null)
             return Animator.Value;

         if (_cachedAnimator != null)
             return _cachedAnimator;

         if (Jumper != null && Jumper.Value != null)
         {
             _cachedAnimator = Jumper.Value.GetComponent<Animator>();
             if (_cachedAnimator == null)
                 _cachedAnimator = Jumper.Value.GetComponentInParent<Animator>();
         }

         return _cachedAnimator;
     }

     protected override Status OnStart()
     {
         if (Jumper == null || Jumper.Value == null)
             return Status.Failure;
 
         if (Target == null || Target.Value == null || JumpPoint == null)
             return Status.Failure;
 
         // Initialize telegraph state
         _telegraphTimer = 0f;
         _telegraphStartPos = Target.Value.transform.position;
         _jumpStarted = false;
 
         return Status.Running; // Telegraph phase
     }

     protected override Status OnUpdate()
     {
         if (Jumper == null || Jumper.Value == null)
             return Status.Failure;
 
         if (Target == null || Target.Value == null || JumpPoint == null)
             return Status.Failure;
 
         // Telegraph phase: count down before jump starts
         if (!_jumpStarted)
         {
             // Check max jump distance to current target position
             float distanceToTarget = Vector3.Distance(Jumper.Value.transform.position, Target.Value.transform.position);
             float maxDist = MaxJumpDistance != null ? MaxJumpDistance.Value : 8f;
             if (distanceToTarget > maxDist)
                 return Status.Failure; // Too far, fall back to chase
 
             // Telegraph timer
             float telegraphDur = TelegraphDuration != null ? TelegraphDuration.Value : 0.8f;
             _telegraphTimer += Time.deltaTime;
 
             // Telegraph still running
             if (_telegraphTimer < telegraphDur)
                 return Status.Running;
 
             // Telegraph complete, check if target moved beyond avoid distance
             float targetMovement = Vector3.Distance(Target.Value.transform.position, _telegraphStartPos);
             float avoidDist = AvoidDistance != null ? AvoidDistance.Value : 2.5f;
              if (targetMovement > avoidDist)
                  return Status.Failure; // Target avoided, jump fails

              if (!Jumper.Value.CanStartAttack(Target.Value))
                  return Status.Failure;

              if (!Jumper.Value.TryStartJump(
                  JumpPoint.Value,
                  Duration != null ? Duration.Value : 0.4f,
                 Height != null ? Height.Value : 1.5f,
                 Target.Value.transform.position,
                 Target.Value
             ))
                  return Status.Failure;

              // All checks passed, start jump
              _jumpStarted = true;

              string triggerName = AttackTrigger != null ? AttackTrigger.Value : "Attack";
              if (!string.IsNullOrEmpty(triggerName))
              {
                  Animator animator = ResolveAnimator();
                  if (animator != null)
                      animator.SetTrigger(triggerName);
              }

             return Status.Running;
         }
 
         // Jump phase: wait for jump to complete
         return Jumper.Value.IsJumping ? Status.Running : Status.Success;
     }

    protected override void OnEnd()
    {
        // Ȥ�� �ٸ� ������ ����Ǿ��� ���� �����ϰ� NavMeshAgent ������
        // JumpToPoint �ʿ��� ó�� ���̶� ���⼱ ���� �۾� �ʿ� ����.
    }
}
