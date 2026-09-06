using UnityEngine;

public class DoorAnimatorOpener : MonoBehaviour
{
    [Header("Animator 설정")]
    public Animator animator;
    public string openBoolName = "Open";   // bool 파라미터 이름 (또는 Trigger 이름)

    private void Reset()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    public bool IsOpen
    {
        get
        {
            if (animator == null) return false;
            // Trigger를 쓰면 이 부분은 애니메이션 상태로 체크해야 함
            return animator.GetBool(openBoolName);
        }
    }

    /// <summary>
    /// 문을 연다: Animator의 Open bool/Trigger만 건드림
    /// 콜라이더 이동/회전은 애니메이션에 맡김
    /// </summary>
    public void OpenDoor()
    {
        if (animator == null) return;

        // bool 파라미터일 경우:
        animator.SetBool(openBoolName, true);

        // Trigger를 쓴다면:
        // animator.SetTrigger("Open");
    }
}
