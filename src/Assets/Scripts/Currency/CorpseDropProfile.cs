using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CorpseDropProfile", menuName = "Game/CorpseDropProfile")]
public class CorpseDropProfile : ScriptableObject
{
    [Header("직접 연결된 드롭 설정")]
    [Tooltip("MonsterHealth가 이름 매칭 없이 직접 사용할 드롭 목록")]
    public List<CorpseDropTable.DropInfo> possibleDrops = new();
}
