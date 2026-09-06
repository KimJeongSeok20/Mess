using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class RandomTileVariation : MonoBehaviour
{
    // 한 번 랜덤 뽑고 나면 true로 바뀜 (씬/프리팹에 저장됨)
    [SerializeField, HideInInspector] private bool _initialized = false;

    // 저장되는 랜덤 값들
    [SerializeField, HideInInspector] private Vector2 _savedOffset;
    [SerializeField, HideInInspector] private int _savedRotationStep;

    static readonly int BaseMapSTID = Shader.PropertyToID("_BaseMap_ST");
    static readonly int MainTexSTID = Shader.PropertyToID("_MainTex_ST");

    void OnEnable()
    {
        Apply();
    }

    void Apply()
    {
        // 아직 한 번도 랜덤 안 뽑았으면 여기서 한 번만 생성
        if (!_initialized)
        {
            GenerateRandom();
        }

        var r = GetComponent<MeshRenderer>();
        if (r == null) return;

        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);

        // 저장된 offset을 머티리얼에 적용 (URP / Standard 둘 다 대응)
        Vector4 st = new Vector4(1f, 1f, _savedOffset.x, _savedOffset.y);
        block.SetVector(BaseMapSTID, st);
        block.SetVector(MainTexSTID, st);
        r.SetPropertyBlock(block);

        // 저장된 회전값 적용 (0, 90, 180, 270)
        transform.localRotation = Quaternion.Euler(0f, 90f * _savedRotationStep, 0f);
    }

    void GenerateRandom()
    {
        _savedOffset = new Vector2(Random.value, Random.value);
        _savedRotationStep = Random.Range(0, 4); // 0~3
        _initialized = true;
    }

#if UNITY_EDITOR
    // Inspector에서 컴포넌트 우클릭 → "Re-Roll Random" 눌러서 다시 뽑을 수 있음
    [ContextMenu("Re-Roll Random")]
    void Reroll()
    {
        _initialized = false;
        GenerateRandom();
        Apply();
    }
#endif
}
