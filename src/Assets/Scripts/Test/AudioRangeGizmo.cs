using UnityEngine;

#if UNITY_EDITOR
[ExecuteAlways]
#endif
public class AudioRangeGizmo : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private AudioSource audioSource;

    [Header("Optional Override (WeaponData SFX)")]
    [Tooltip("값을 WeaponData에서 읽고 싶으면 체크")]
    [SerializeField] private bool useWeaponDataSfx = false;

    [Tooltip("WeaponData를 참조할 수 있는 Weapon이 같은 계층에 있을 때만 의미 있음")]
    [SerializeField] private Demo.Scripts.Runtime.Item.Weapon weapon;

    [Header("Draw")]
    [SerializeField] private bool drawMinDistance = true;
    [SerializeField] private bool drawMaxDistance = true;

    private void OnValidate()
    {
        AutoFind();
    }

    private void Reset()
    {
        AutoFind();
    }

    private void AutoFind()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (weapon == null) weapon = GetComponentInParent<Demo.Scripts.Runtime.Item.Weapon>();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        AutoFind();

        float min = 0f;
        float max = 0f;

        if (useWeaponDataSfx && weapon != null && weapon.WeaponData != null)
        {
            var sfx = weapon.WeaponData.sfx;
            if (!sfx.use3D) return; // 2D면 거리 개념 없음
            min = sfx.minDistance;
            max = sfx.maxDistance;
        }
        else
        {
            if (audioSource == null) return;
            if (audioSource.spatialBlend <= 0.001f) return; // 2D면 굳이 안 그림
            min = audioSource.minDistance;
            max = audioSource.maxDistance;
        }

        if (max <= 0f) return;

        // Gizmos 색은 Unity 기본값 그대로 사용(선명하게만)
        if (drawMaxDistance)
        {
            Gizmos.DrawWireSphere(transform.position, max);
        }

        if (drawMinDistance && min > 0f)
        {
            Gizmos.DrawWireSphere(transform.position, min);
        }
    }
#endif
}
