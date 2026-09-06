using UnityEngine;

/// <summary>
/// 프로젝트 기본 등급 확률표 접근점.
///
/// 아이템은 월드 어디서나 스폰되므로 씬 참조로 확률표를 넘길 수 없다.
/// <c>Resources/ItemRarityTable.asset</c>을 한 번 읽어 캐시한다.
/// 아이템별로 다른 표를 쓰려면 <see cref="ItemDefinition.rarityTable"/>에 지정하면 된다.
/// </summary>
public static class ItemRarityDefaults
{
    public const string ResourcePath = "ItemRarityTable";

    private static ItemRarityTable _table;
    private static bool _lookupAttempted;

    public static ItemRarityTable Table
    {
        get
        {
            if (_table != null)
                return _table;

            if (_lookupAttempted)
                return null;

            _lookupAttempted = true;
            _table = Resources.Load<ItemRarityTable>(ResourcePath);

            if (_table == null)
            {
                Debug.LogWarning(
                    $"[ItemRarityDefaults] Resources/{ResourcePath}.asset 를 찾지 못했습니다. " +
                    "등급 무작위 굴림이 비활성화되고 정의의 기본 등급이 그대로 쓰입니다.");
            }

            return _table;
        }
    }

    /// <summary>도메인 리로드가 꺼져 있어도 캐시가 남지 않도록 초기화한다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        _table = null;
        _lookupAttempted = false;
    }
}
