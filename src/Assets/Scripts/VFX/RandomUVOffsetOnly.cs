using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class RandomUVOffsetOnly : MonoBehaviour
{
    [SerializeField, HideInInspector] private bool _initialized = false;
    [SerializeField, HideInInspector] private Vector2 _savedOffset;

    static readonly int BaseMapSTID = Shader.PropertyToID("_BaseMap_ST");
    static readonly int MainTexSTID = Shader.PropertyToID("_MainTex_ST");

    void OnEnable()
    {
        Apply();
    }

    void Apply()
    {
        if (!_initialized)
        {
            GenerateRandom();
        }

        var r = GetComponent<MeshRenderer>();
        if (r == null) return;

        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);

        Vector4 st = new Vector4(1f, 1f, _savedOffset.x, _savedOffset.y);
        block.SetVector(BaseMapSTID, st);
        block.SetVector(MainTexSTID, st);

        r.SetPropertyBlock(block);
    }

    void GenerateRandom()
    {
        // 아주 작은 범위 내에서 UV 오프셋 랜덤 (과도하면 어색해져서 0~1 범위)
        _savedOffset = new Vector2(Random.value, Random.value);
        _initialized = true;
    }

#if UNITY_EDITOR
    [ContextMenu("Re-Roll Offset")]
    void Reroll()
    {
        _initialized = false;
        GenerateRandom();
        Apply();
    }
#endif
}
