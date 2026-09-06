using UnityEngine;

/// <summary>
/// 간단한 발사체 스크립트
/// - 생성 시 앞으로 날아감
/// - 충돌 시 파괴 (Collision + Trigger 둘 다 처리)
/// - 일정 시간 후 자동 파괴
/// </summary>
public class SimpleProjectile : MonoBehaviour
{
    [SerializeField] private float speed = 50f;
    [SerializeField] private float lifetime = 5f;
    
    private Rigidbody rb;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }
    
    private void Start()
    {
        // 앞 방향으로 발사
        if (rb != null)
        {
            rb.linearVelocity = transform.forward * speed;
        }
        
        // 일정 시간 후 자동 파괴
        Destroy(gameObject, lifetime);
    }
    
    private void OnCollisionEnter(Collision collision)
    {
        // 충돌 시 즉시 파괴
        Debug.Log($"[Projectile] Collision hit: {collision.gameObject.name} (Layer: {collision.gameObject.layer})");
        Destroy(gameObject);
    }
    
    private void OnTriggerEnter(Collider other)
    {
        // Trigger 충돌 시에도 파괴 (Collider가 Trigger인 경우 대비)
        Debug.Log($"[Projectile] Trigger hit: {other.gameObject.name} (Layer: {other.gameObject.layer})");
        Destroy(gameObject);
    }
}